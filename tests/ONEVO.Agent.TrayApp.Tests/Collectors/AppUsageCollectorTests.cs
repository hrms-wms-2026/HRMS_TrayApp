using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Collectors;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using Xunit;

namespace ONEVO.Agent.TrayApp.Tests.Collectors;

public class AppUsageCollectorTests
{
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    private static readonly AgentPolicy Enabled = new() { Version = "v1", AppUsageEnabled = true };

    private sealed class ScriptedProbe
    {
        private readonly Queue<ForegroundApp?> _script = new();

        public void Enqueue(string? process, int polls, string titleHash = "hash")
        {
            for (var i = 0; i < polls; i++)
                _script.Enqueue(new ForegroundApp(new IntPtr(1), process, titleHash));
        }

        public void EnqueueNoWindow(int polls)
        {
            for (var i = 0; i < polls; i++)
                _script.Enqueue(null);
        }

        public ForegroundApp? Next() => _script.Count > 0 ? _script.Dequeue() : null;
    }

    private sealed class RecordingIconCache : IAppIconCache
    {
        public List<string> Cached { get; } = [];
        public ImageSource? GetIcon(string processName) => null;
        public void TryCacheFromForegroundWindow(IntPtr hwnd, string processName) => Cached.Add(processName);
    }

    private static (AppUsageCollector Collector, FakeNamedPipeClient Pipe, FakeSessionDayMetrics Metrics, RecordingIconCache Icons)
        Build(ScriptedProbe probe)
    {
        var pipe = new FakeNamedPipeClient();
        var metrics = new FakeSessionDayMetrics();
        var icons = new RecordingIconCache();
        var collector = new AppUsageCollector(
            NullLogger<AppUsageCollector>.Instance, pipe, metrics, icons, probe.Next, Poll, Window);
        return (collector, pipe, metrics, icons);
    }

    private static async Task PollAsync(AppUsageCollector collector, int times)
    {
        for (var i = 0; i < times; i++)
            await collector.PollForTestAsync(CancellationToken.None);
    }

    private static List<AppUsageSnapshotPayload> Snapshots(FakeNamedPipeClient pipe) =>
        pipe.Submitted.SelectMany(batch => batch)
            .Where(r => r.RecordType == CollectionRecordTypes.AppUsageSnapshot)
            .Select(r => r.Payload.Deserialize<AppUsageSnapshotPayload>()!)
            .ToList();

    [Fact]
    public async Task ManyAppSwitchesInOneWindow_EmitExactlyOneSnapshot()
    {
        // Backend counts every snapshot as one minute — switches must not inflate usage.
        var probe = new ScriptedProbe();
        for (var i = 0; i < 6; i++)
        {
            probe.Enqueue("chrome", 1);
            probe.Enqueue("code", 1);
        }
        var (collector, pipe, _, _) = Build(probe);
        await collector.StartAsync(Enabled, CancellationToken.None);

        await PollAsync(collector, 12);

        Assert.Single(Snapshots(pipe));
    }

    [Fact]
    public async Task NoSnapshotBeforeWindowCloses()
    {
        var probe = new ScriptedProbe();
        probe.Enqueue("chrome", 11);
        var (collector, pipe, _, _) = Build(probe);
        await collector.StartAsync(Enabled, CancellationToken.None);

        await PollAsync(collector, 11);

        Assert.Empty(Snapshots(pipe));
    }

    [Fact]
    public async Task Snapshot_ReportsAppWithMostForegroundTime()
    {
        var probe = new ScriptedProbe();
        probe.Enqueue("code", 4, "code-hash");
        probe.Enqueue("chrome", 7, "chrome-hash");
        probe.Enqueue("code", 1, "code-hash-2");
        var (collector, pipe, _, _) = Build(probe);
        await collector.StartAsync(Enabled, CancellationToken.None);

        await PollAsync(collector, 12);

        var snapshot = Assert.Single(Snapshots(pipe));
        Assert.Equal("chrome", snapshot.ProcessName);
        Assert.Equal("chrome-hash", snapshot.WindowTitleHash);
    }

    [Fact]
    public async Task DayMetrics_ReceiveActualSecondsPerApp()
    {
        var probe = new ScriptedProbe();
        probe.Enqueue("code", 3);
        probe.Enqueue("chrome", 9);
        var (collector, _, metrics, _) = Build(probe);
        await collector.StartAsync(Enabled, CancellationToken.None);

        await PollAsync(collector, 12);

        Assert.Contains(("code", TimeSpan.FromSeconds(15)), metrics.AppUsageSamples);
        Assert.Contains(("chrome", TimeSpan.FromSeconds(45)), metrics.AppUsageSamples);
    }

    [Fact]
    public async Task AppChange_CachesIconOncePerSwitch_NotEveryPoll()
    {
        var probe = new ScriptedProbe();
        probe.Enqueue("code", 5);
        probe.Enqueue("chrome", 5);
        probe.Enqueue("code", 2);
        var (collector, _, _, icons) = Build(probe);
        await collector.StartAsync(Enabled, CancellationToken.None);

        await PollAsync(collector, 12);

        Assert.Equal(["code", "chrome", "code"], icons.Cached);
    }

    [Fact]
    public async Task WindowWithNoForegroundApp_EmitsNothing()
    {
        var probe = new ScriptedProbe();
        probe.EnqueueNoWindow(12);
        var (collector, pipe, metrics, _) = Build(probe);
        await collector.StartAsync(Enabled, CancellationToken.None);

        await PollAsync(collector, 12);

        Assert.Empty(Snapshots(pipe));
        Assert.Empty(metrics.AppUsageSamples);
    }

    [Fact]
    public async Task Stop_DropsPartialWindow()
    {
        var probe = new ScriptedProbe();
        probe.Enqueue("chrome", 6);
        probe.Enqueue("code", 12);
        var (collector, pipe, _, _) = Build(probe);
        await collector.StartAsync(Enabled, CancellationToken.None);

        await PollAsync(collector, 6);
        await collector.StopAsync(CancellationToken.None);
        await collector.StartAsync(Enabled, CancellationToken.None);
        await PollAsync(collector, 12);

        var snapshot = Assert.Single(Snapshots(pipe));
        Assert.Equal("code", snapshot.ProcessName);
    }
}
