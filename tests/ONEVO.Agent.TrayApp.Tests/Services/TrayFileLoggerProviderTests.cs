namespace ONEVO.Agent.TrayApp.Tests.Services;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using ONEVO.Agent.Shared.Diagnostics;
using ONEVO.Agent.TrayApp.Services.Diagnostics;

public sealed class TrayFileLoggerProviderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "onevo-tray-log-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static ILoggerFactory Factory(TrayFileLoggerProvider provider) =>
        LoggerFactory.Create(b => b.AddTrayFileLogging(provider));

    private List<JsonElement> ReadLines(TrayFileLoggerProvider provider) =>
        File.ReadAllLines(provider.FilePath)
            .Select(l => JsonDocument.Parse(l).RootElement.Clone())
            .ToList();

    [Fact]
    public void Log_WritesStructuredJsonLine()
    {
        var provider = new TrayFileLoggerProvider(_dir);
        using (var factory = Factory(provider))
            factory.CreateLogger("ONEVO.Agent.TrayApp.Test").LogInformation("Collector started: {Name}", "AppUsage");

        var line = Assert.Single(ReadLines(provider));
        Assert.Equal("Information", line.GetProperty("level").GetString());
        Assert.Equal("ONEVO.Agent.TrayApp.Test", line.GetProperty("category").GetString());
        Assert.Equal("Collector started: AppUsage", line.GetProperty("msg").GetString());
        Assert.Equal("AppUsage", line.GetProperty("props").GetProperty("Name").GetString());
        Assert.False(line.GetProperty("props").TryGetProperty("{OriginalFormat}", out _));
        Assert.True(DateTimeOffset.TryParse(line.GetProperty("ts").GetString(), out _));
    }

    [Fact]
    public void Filters_DropDebugAndFrameworkInformation_KeepFrameworkWarnings()
    {
        var provider = new TrayFileLoggerProvider(_dir);
        using (var factory = Factory(provider))
        {
            factory.CreateLogger("ONEVO.Agent.TrayApp.Test").LogDebug("debug");
            factory.CreateLogger("System.Net.Http.HttpClient.Default").LogInformation("GET https://host/path?q=secret");
            factory.CreateLogger("Microsoft.Maui").LogInformation("noise");
            factory.CreateLogger("Microsoft.Maui").LogWarning("framework warning");
            factory.CreateLogger("ONEVO.Agent.TrayApp.Test").LogInformation("kept");
        }

        var messages = ReadLines(provider).Select(l => l.GetProperty("msg").GetString()).ToList();
        Assert.Equal(new[] { "framework warning", "kept" }, messages);
    }

    [Fact]
    public void Log_IncludesExceptionText()
    {
        var provider = new TrayFileLoggerProvider(_dir);
        using (var factory = Factory(provider))
            factory.CreateLogger("T").LogError(new InvalidOperationException("boom"), "failed");

        var line = Assert.Single(ReadLines(provider));
        Assert.Contains("InvalidOperationException: boom", line.GetProperty("ex").GetString());
    }

    [Fact]
    public void LogCrash_WritesCriticalWithSource()
    {
        var provider = new TrayFileLoggerProvider(_dir);
        using (var factory = Factory(provider))
            factory.CreateLogger(TrayLogging.CrashCategory).LogCrash("XamlDispatcher", new Exception("fatal"), isTerminating: true);

        var line = Assert.Single(ReadLines(provider));
        Assert.Equal("Critical", line.GetProperty("level").GetString());
        Assert.Equal("XamlDispatcher", line.GetProperty("props").GetProperty("Source").GetString());
        Assert.True(line.GetProperty("props").GetProperty("IsTerminating").GetBoolean());
    }

    [Fact]
    public void Scopes_AppearOnLine()
    {
        var provider = new TrayFileLoggerProvider(_dir);
        using (var factory = Factory(provider))
        {
            var logger = factory.CreateLogger("T");
            using (logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = "abc123" }))
            using (logger.BeginScope("clock-in"))
                logger.LogInformation("scoped");
            logger.LogInformation("unscoped");
        }

        var lines = ReadLines(provider);
        Assert.Equal("abc123", lines[0].GetProperty("scope").GetProperty("CorrelationId").GetString());
        Assert.Equal("clock-in", lines[0].GetProperty("scopes")[0].GetString());
        Assert.False(lines[1].TryGetProperty("scope", out _));
    }

    [Fact]
    public void Roll_AtSizeLimit_KeepsRetainedFilesAndDropsOldest()
    {
        var provider = new TrayFileLoggerProvider(_dir, maxFileBytes: 300, retainedFiles: 3);
        using (var factory = Factory(provider))
        {
            var logger = factory.CreateLogger("T");
            for (var i = 0; i < 40; i++)
                logger.LogInformation("line {Index}", i);
        }

        var files = Directory.GetFiles(_dir).Select(Path.GetFileName).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "tray.log", "tray.log.1", "tray.log.2" }, files);
        Assert.All(files, f => Assert.True(new FileInfo(Path.Combine(_dir, f!)).Length <= 300));
        // The newest line is in the live file; the earliest ones have rolled off.
        Assert.Equal("line 39", ReadLines(provider).Last().GetProperty("msg").GetString());
        var all = Directory.GetFiles(_dir).SelectMany(File.ReadAllLines).ToList();
        Assert.DoesNotContain(all, l => l.Contains("\"line 0\""));
    }

    [Fact]
    public async Task ConcurrentWriters_ProduceWholeLines()
    {
        var provider = new TrayFileLoggerProvider(_dir);
        using (var factory = Factory(provider))
        {
            var logger = factory.CreateLogger("T");
            await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
            {
                for (var i = 0; i < 50; i++)
                    logger.LogInformation("writer {Thread} line {Index}", t, i);
            })));
        }

        Assert.Equal(400, ReadLines(provider).Count);
    }

    [Fact]
    public void UnwritableDirectory_DoesNotThrow()
    {
        // A file where the log directory should be makes every write fail.
        Directory.CreateDirectory(Path.GetDirectoryName(_dir)!);
        File.WriteAllText(_dir, "not a directory");
        try
        {
            var provider = new TrayFileLoggerProvider(_dir);
            using var factory = Factory(provider);
            factory.CreateLogger("T").LogError(new Exception("x"), "still fine");
        }
        finally
        {
            File.Delete(_dir);
        }
    }

    [Fact]
    public async Task NamedPipeClientRequest_LogsUnderItsCorrelationId()
    {
        var provider = new TrayFileLoggerProvider(_dir);
        using (var factory = Factory(provider))
        {
            // Never connected: the request is logged as not sent, inside its correlation scope.
            await using var client = new ONEVO.Agent.TrayApp.Services.NamedPipeClient(
                factory.CreateLogger<ONEVO.Agent.TrayApp.Services.NamedPipeClient>(), $"onevo-test-{Guid.NewGuid():N}");
            Assert.Null(await client.SendLogoutAsync(CancellationToken.None));
        }

        var line = Assert.Single(ReadLines(provider), l => l.GetProperty("msg").GetString()!.StartsWith("Logout not sent"));
        var cid = line.GetProperty("scope").GetProperty(AgentCorrelation.ScopeKey).GetString();
        Assert.True(AgentCorrelation.IsValid(cid));
    }

    [Fact]
    public void ReaderHoldingFileOpen_DoesNotBlockWrites()
    {
        var provider = new TrayFileLoggerProvider(_dir);
        using var factory = Factory(provider);
        var logger = factory.CreateLogger("T");
        logger.LogInformation("first");

        using (new FileStream(provider.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            logger.LogInformation("second");

        Assert.Equal(2, ReadLines(provider).Count);
    }
}
