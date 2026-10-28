namespace ONEVO.Agent.TrayApp.Tests.Services;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using ONEVO.Agent.Shared.Diagnostics;
using ONEVO.Agent.TrayApp.Services.Diagnostics;

/// <summary>
/// Tray wiring of the shared file logger: filters, crash lines and correlation scopes. The
/// provider itself is covered by JsonFileLoggerProviderTests in ONEVO.Agent.Shared.Tests.
/// </summary>
public sealed class TrayFileLoggingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "onevo-tray-log-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static ILoggerFactory Factory(JsonFileLoggerProvider provider) =>
        LoggerFactory.Create(b => b.AddTrayFileLogging(provider));

    private static List<JsonElement> ReadLines(JsonFileLoggerProvider provider) =>
        File.ReadAllLines(provider.FilePath)
            .Select(l => JsonDocument.Parse(l).RootElement.Clone())
            .ToList();

    [Fact]
    public void CreateFileProvider_WritesTrayLog()
    {
        Assert.Equal(Path.Combine(_dir, TrayLogging.FileName), TrayLogging.CreateFileProvider(_dir).FilePath);
        Assert.EndsWith(Path.Combine("ONEVO", "Agent", "logs"), TrayLogging.DefaultDirectory);
    }

    [Fact]
    public void Filters_DropDebugAndFrameworkInformation_KeepFrameworkWarnings()
    {
        var provider = TrayLogging.CreateFileProvider(_dir);
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
    public void LogCrash_WritesCriticalWithSource()
    {
        var provider = TrayLogging.CreateFileProvider(_dir);
        using (var factory = Factory(provider))
            factory.CreateLogger(TrayLogging.CrashCategory).LogCrash("XamlDispatcher", new Exception("fatal"), isTerminating: true);

        var line = Assert.Single(ReadLines(provider));
        Assert.Equal("Critical", line.GetProperty("level").GetString());
        Assert.Equal("XamlDispatcher", line.GetProperty("props").GetProperty("Source").GetString());
        Assert.True(line.GetProperty("props").GetProperty("IsTerminating").GetBoolean());
    }

    [Fact]
    public async Task NamedPipeClientRequest_LogsUnderItsCorrelationId()
    {
        var provider = TrayLogging.CreateFileProvider(_dir);
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
}
