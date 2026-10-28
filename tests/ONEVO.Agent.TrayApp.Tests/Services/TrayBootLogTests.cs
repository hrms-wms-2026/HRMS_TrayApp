namespace ONEVO.Agent.TrayApp.Tests.Services;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using ONEVO.Agent.Shared.Diagnostics;
using ONEVO.Agent.TrayApp.Services.Diagnostics;

/// <summary>TrayBootLog is process-wide static state, so these tests never run in parallel with each other.</summary>
[Collection(nameof(TrayBootLogTests))]
public sealed class TrayBootLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "onevo-bootlog-tests", Guid.NewGuid().ToString("N"));
    private readonly string _originalFallback = TrayBootLog.FallbackPath;

    public TrayBootLogTests()
    {
        TrayBootLog.Detach();
        TrayBootLog.FallbackPath = Path.Combine(_dir, "tray-boot.log");
    }

    public void Dispose()
    {
        TrayBootLog.Detach();
        TrayBootLog.FallbackPath = _originalFallback;
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void BeforeAttach_WritesTheFallbackFile()
    {
        TrayBootLog.Write("Startup", "CreateMauiApp enter");

        var line = Assert.Single(File.ReadAllLines(TrayBootLog.FallbackPath));
        Assert.EndsWith("[Startup] CreateMauiApp enter", line);
    }

    [Fact]
    public void AfterAttach_WritesTrayLog_AndNotTheFallback()
    {
        var provider = TrayLogging.CreateFileProvider(_dir);
        using (var factory = LoggerFactory.Create(b => b.AddTrayFileLogging(provider)))
        {
            TrayBootLog.Attach(factory);
            TrayBootLog.Write("Coordinator", "State=Active");
            TrayBootLog.Write("UiTasks", "Load failed: boom", LogLevel.Error);
        }

        Assert.False(File.Exists(TrayBootLog.FallbackPath));
        var lines = File.ReadAllLines(provider.FilePath).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.All(lines, l => Assert.Equal(TrayBootLog.Category, l.GetProperty("category").GetString()));
        Assert.Equal("[Coordinator] State=Active", lines[0].GetProperty("msg").GetString());
        Assert.Equal("Coordinator", lines[0].GetProperty("props").GetProperty("Source").GetString());
        Assert.Equal("Information", lines[0].GetProperty("level").GetString());
        Assert.Equal("Error", lines[1].GetProperty("level").GetString());
    }

    [Fact]
    public void AfterAttach_LinesCarryTheCorrelationScope()
    {
        var provider = TrayLogging.CreateFileProvider(_dir);
        using (var factory = LoggerFactory.Create(b => b.AddTrayFileLogging(provider)))
        {
            TrayBootLog.Attach(factory);
            using (factory.CreateLogger("x").BeginScope(AgentCorrelation.Scope("cid-1")))
                TrayBootLog.Write("App", "State received: Active");
        }

        var line = JsonDocument.Parse(Assert.Single(File.ReadAllLines(provider.FilePath))).RootElement;
        Assert.Equal("cid-1", line.GetProperty("scope").GetProperty(AgentCorrelation.ScopeKey).GetString());
    }

    [Fact]
    public void UnwritableFallback_DoesNotThrow()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "blocker"), "x");
        TrayBootLog.FallbackPath = Path.Combine(_dir, "blocker", "tray-boot.log");

        TrayBootLog.Write("Startup", "still fine");
    }

    [Fact]
    public void RollFallbackIfLarge_MovesAnOversizedFileAside()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(TrayBootLog.FallbackPath, new byte[2 * 1024 * 1024 + 1]);

        TrayBootLog.RollFallbackIfLarge();

        Assert.False(File.Exists(TrayBootLog.FallbackPath));
        Assert.True(File.Exists(TrayBootLog.FallbackPath + ".old"));
    }

    [Fact]
    public void RollFallbackIfLarge_LeavesASmallFile()
    {
        TrayBootLog.Write("Startup", "small");

        TrayBootLog.RollFallbackIfLarge();

        Assert.True(File.Exists(TrayBootLog.FallbackPath));
    }
}
