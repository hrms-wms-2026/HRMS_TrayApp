namespace ONEVO.Agent.Shared.Tests;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using ONEVO.Agent.Shared.Diagnostics;
using Xunit;

public sealed class JsonFileLoggerProviderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "onevo-json-log-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private JsonFileLoggerProvider Create(long maxFileBytes = JsonFileLoggerProvider.DefaultMaxFileBytes, int retainedFiles = 3) =>
        new(_dir, "test.log", maxFileBytes, retainedFiles);

    private static List<JsonElement> ReadLines(JsonFileLoggerProvider provider) =>
        File.ReadAllLines(provider.FilePath)
            .Select(l => JsonDocument.Parse(l).RootElement.Clone())
            .ToList();

    [Fact]
    public void Log_WritesStructuredJsonLine()
    {
        var provider = Create();
        provider.CreateLogger("ONEVO.Test").LogInformation(new EventId(7), "Collector started: {Name} {Count} {On}", "AppUsage", 3, true);

        var line = Assert.Single(ReadLines(provider));
        Assert.Equal(Path.Combine(_dir, "test.log"), provider.FilePath);
        Assert.Equal("Information", line.GetProperty("level").GetString());
        Assert.Equal("ONEVO.Test", line.GetProperty("category").GetString());
        Assert.Equal(7, line.GetProperty("eventId").GetInt32());
        Assert.Equal("Collector started: AppUsage 3 True", line.GetProperty("msg").GetString());
        var props = line.GetProperty("props");
        Assert.Equal("AppUsage", props.GetProperty("Name").GetString());
        Assert.Equal(3, props.GetProperty("Count").GetInt32());
        Assert.True(props.GetProperty("On").GetBoolean());
        Assert.False(props.TryGetProperty("{OriginalFormat}", out _));
        Assert.True(DateTimeOffset.TryParse(line.GetProperty("ts").GetString(), out _));
    }

    [Fact]
    public void Log_KeepsTextReadable()
    {
        var provider = Create();
        provider.CreateLogger("T").LogInformation("it's +05:30");

        Assert.Contains("it's +05:30", File.ReadAllText(provider.FilePath));
    }

    [Fact]
    public void Log_IncludesExceptionText()
    {
        var provider = Create();
        provider.CreateLogger("T").LogError(new InvalidOperationException("boom"), "failed");

        var line = Assert.Single(ReadLines(provider));
        Assert.Contains("InvalidOperationException: boom", line.GetProperty("ex").GetString());
    }

    [Fact]
    public void LogLevelNone_IsNotWritten()
    {
        var provider = Create();
        var logger = provider.CreateLogger("T");
        Assert.False(logger.IsEnabled(LogLevel.None));
        logger.Log(LogLevel.None, "never");

        Assert.False(File.Exists(provider.FilePath));
    }

    [Fact]
    public void Scopes_AppearOnLine()
    {
        var provider = Create();
        var logger = provider.CreateLogger("T");
        using (logger.BeginScope(AgentCorrelation.Scope("abc123")))
        using (logger.BeginScope("clock-in"))
            logger.LogInformation("scoped");
        logger.LogInformation("unscoped");

        var lines = ReadLines(provider);
        Assert.Equal("abc123", lines[0].GetProperty("scope").GetProperty(AgentCorrelation.ScopeKey).GetString());
        Assert.Equal("clock-in", lines[0].GetProperty("scopes")[0].GetString());
        Assert.False(lines[1].TryGetProperty("scope", out _));
        Assert.False(lines[1].TryGetProperty("scopes", out _));
    }

    [Fact]
    public void Roll_AtSizeLimit_KeepsRetainedFilesAndDropsOldest()
    {
        var provider = Create(maxFileBytes: 300, retainedFiles: 3);
        var logger = provider.CreateLogger("T");
        for (var i = 0; i < 40; i++)
            logger.LogInformation("line {Index}", i);

        var files = Directory.GetFiles(_dir).Select(Path.GetFileName).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "test.log", "test.log.1", "test.log.2" }, files);
        Assert.All(files, f => Assert.True(new FileInfo(Path.Combine(_dir, f!)).Length <= 300));
        // The newest line is in the live file; the earliest ones have rolled off.
        Assert.Equal("line 39", ReadLines(provider).Last().GetProperty("msg").GetString());
        var all = Directory.GetFiles(_dir).SelectMany(File.ReadAllLines).ToList();
        Assert.DoesNotContain(all, l => l.Contains("\"line 0\""));
    }

    [Fact]
    public void Roll_WithSingleRetainedFile_StartsOver()
    {
        var provider = Create(maxFileBytes: 300, retainedFiles: 1);
        var logger = provider.CreateLogger("T");
        for (var i = 0; i < 10; i++)
            logger.LogInformation("line {Index}", i);

        Assert.Equal(new[] { "test.log" }, Directory.GetFiles(_dir).Select(Path.GetFileName).ToArray());
        Assert.Equal("line 9", ReadLines(provider).Last().GetProperty("msg").GetString());
    }

    [Fact]
    public async Task ConcurrentWriters_ProduceWholeLines()
    {
        var provider = Create();
        var logger = provider.CreateLogger("T");
        await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
                logger.LogInformation("writer {Thread} line {Index}", t, i);
        })));

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
            Create().CreateLogger("T").LogError(new Exception("x"), "still fine");
        }
        finally
        {
            File.Delete(_dir);
        }
    }

    [Fact]
    public void ReaderHoldingFileOpen_DoesNotBlockWrites()
    {
        var provider = Create();
        var logger = provider.CreateLogger("T");
        logger.LogInformation("first");

        using (new FileStream(provider.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            logger.LogInformation("second");

        Assert.Equal(2, ReadLines(provider).Count);
    }
}
