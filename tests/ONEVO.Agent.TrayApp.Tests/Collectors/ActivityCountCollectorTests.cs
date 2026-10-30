using System.Runtime.InteropServices;
using ONEVO.Agent.TrayApp.Services;

namespace ONEVO.Agent.TrayApp.Tests.Collectors;

// Injects real (synthetic) input, so it needs an interactive desktop session — filter out on headless CI
// with: --filter "Category!=InteractiveDesktop".
[Trait("Category", "InteractiveDesktop")]
public class ActivityCountCollectorTests
{
    private const byte VkF24 = 0x87; // no physical key and no default binding — safe to inject
    private const uint KeyEventKeyUp = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [Fact]
    public async Task Hooks_CountInput_OnTheirOwnThread_WithoutTheCallerPumpingMessages()
    {
        var collector = new ActivityCountCollector(
            NullLogger<ActivityCountCollector>.Instance,
            new NamedPipeClient(NullLogger<NamedPipeClient>.Instance));
        ActivitySnapshotPayload? finalSnapshot = null;
        collector.SnapshotEmitted += (payload, _) => finalSnapshot = payload;

        await collector.StartAsync(
            new AgentPolicy { Version = "v1", ActivitySignalEnabled = true },
            CancellationToken.None);

        // This test thread never pumps messages. Low-level hook callbacks are only delivered via
        // the installing thread's message loop, so a counted key here proves the hooks live on
        // the collector's dedicated thread (not on whichever thread called StartAsync — which in
        // the app used to be the UI thread, stalling input system-wide whenever the UI was busy).
        keybd_event(VkF24, 0, 0, UIntPtr.Zero);
        keybd_event(VkF24, 0, KeyEventKeyUp, UIntPtr.Zero);
        Thread.Sleep(300);

        await collector.StopAsync(CancellationToken.None);

        Assert.NotNull(finalSnapshot);
        Assert.True(finalSnapshot!.KeyboardEventsCount >= 1,
            $"expected the injected key to be counted, got {finalSnapshot.KeyboardEventsCount}");
    }

    [Fact]
    public async Task StopAsync_ThenStartAgain_ReinstallsHooks()
    {
        var collector = new ActivityCountCollector(
            NullLogger<ActivityCountCollector>.Instance,
            new NamedPipeClient(NullLogger<NamedPipeClient>.Instance));
        var policy = new AgentPolicy { Version = "v1", ActivitySignalEnabled = true };

        await collector.StartAsync(policy, CancellationToken.None);
        await collector.StopAsync(CancellationToken.None);

        ActivitySnapshotPayload? finalSnapshot = null;
        collector.SnapshotEmitted += (payload, _) => finalSnapshot = payload;
        await collector.StartAsync(policy, CancellationToken.None);
        keybd_event(VkF24, 0, 0, UIntPtr.Zero);
        keybd_event(VkF24, 0, KeyEventKeyUp, UIntPtr.Zero);
        Thread.Sleep(300);
        await collector.StopAsync(CancellationToken.None);

        Assert.True(finalSnapshot?.KeyboardEventsCount >= 1);
    }
}
