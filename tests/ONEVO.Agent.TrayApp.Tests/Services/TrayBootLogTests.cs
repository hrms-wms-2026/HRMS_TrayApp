namespace ONEVO.Agent.TrayApp.Tests.Services;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using ONEVO.Agent.Shared.Diagnostics;
using ONEVO.Agent.TrayApp.Services.Diagnostics;

/// <summary>
/// TrayBootLog is process-wide static state, so these tests never run in parallel with each other.
/// Collector tests in other classes still call TrayBootLog.Write in parallel, and those lines land in
/// whichever file these tests have pointed it at. So every assertion looks only at lines carrying
/// this test's own marker, never at whole files.
/// </summary>
[Collection(nameof(TrayBootLogTests))]
public sealed class TrayBootLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "onevo-bootlog-tests", Guid.NewGuid().ToString("N"));
    private readonly string _originalFallback = TrayBootLog.FallbackPath;
    private readonly string _marker = Guid.NewGuid().ToString("N");

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
        TrayBootLog.Write("Startup", $"CreateMauiApp enter {_marker}");

        var line = Assert.Single(OwnLines(TrayBootLog.FallbackPath));
        Assert.EndsWith($"[Startup] CreateMauiApp enter {_marker}", line);
    }

    [Fact]
    public void AfterAttach_WritesTrayLog_AndNotTheFallback()
    {
        var provider = TrayLogging.CreateFileProvider(_dir);
        using (var factory = LoggerFactory.Create(b => b.AddTrayFileLogging(provider)))
        {
            TrayBootLog.Attach(factory);
            TrayBootLog.Write("Coordinator", $"State=Active {_marker}");
            TrayBootLog.Write("UiTasks", $"Load failed: boom {_marker}", LogLevel.Error);
        }
        TrayBootLog.Detach();

        Assert.Empty(OwnLines(TrayBootLog.FallbackPath));
        var lines = OwnLines(provider.FilePath).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal(TrayBootLog.Category, l.GetProperty("category").GetString()));
        Assert.Equal($"[Coordinator] State=Active {_marker}", lines[0].GetProperty("msg").GetString());
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
                TrayBootLog.Write("App", $"State received: Active {_marker}");
        }
        TrayBootLog.Detach();

        var line = JsonDocument.Parse(Assert.Single(OwnLines(provider.FilePath))).RootElement;
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
        const long oversized = 2 * 1024 * 1024 + 1;
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(TrayBootLog.FallbackPath, new byte[oversized]);

        TrayBootLog.RollFallbackIfLarge();

        // Parallel writers may append to the big file before the roll, or start a fresh one after it.
        var current = new FileInfo(TrayBootLog.FallbackPath);
        Assert.True(!current.Exists || current.Length < oversized);
        Assert.True(new FileInfo(TrayBootLog.FallbackPath + ".old").Length >= oversized);
    }

    [Fact]
    public void RollFallbackIfLarge_LeavesASmallFile()
    {
        TrayBootLog.Write("Startup", $"small {_marker}");

        TrayBootLog.RollFallbackIfLarge();

        Assert.Single(OwnLines(TrayBootLog.FallbackPath));
        Assert.False(File.Exists(TrayBootLog.FallbackPath + ".old"));
    }

    /// <summary>This test's lines only; the file may be missing or still being appended to.</summary>
    private List<string> OwnLines(string path)
    {
        if (!File.Exists(path))
            return [];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Contains(_marker, StringComparison.Ordinal))
                lines.Add(line);
        }
        return lines;
    }
}
