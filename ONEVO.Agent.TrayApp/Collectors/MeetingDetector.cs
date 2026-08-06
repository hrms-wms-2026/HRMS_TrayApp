namespace ONEVO.Agent.TrayApp.Collectors;

using System.Diagnostics;
using ONEVO.Agent.Shared.Models;

/// <summary>
/// Phase 1 probabilistic meeting detection via known process names (§7.4).
/// Process found ≠ actively in meeting; result is a hint, not proof.
/// </summary>
public sealed class MeetingDetector : IAgentCollector, IAsyncDisposable
{
    private static readonly HashSet<string> MeetingProcessNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "teams",   "teams.exe",
            "zoom",    "zoom.exe",
            "webex",   "webex.exe",
            "slack",   "slack.exe",
            "msteams", "msteams.exe"
        };

    public string Name => "MeetingDetector";

    private readonly ILogger<MeetingDetector> _logger;

    public MeetingDetector(ILogger<MeetingDetector> logger) => _logger = logger;

    public Task StartAsync(AgentPolicy policy, CancellationToken ct)
    {
        _logger.LogInformation("{Name}: started", Name);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _logger.LogInformation("{Name}: stopped", Name);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns true if a known meeting-app process is running.
    /// Probabilistic — background process ≠ active meeting.
    /// </summary>
    public static bool IsMeetingAppRunning()
    {
        try
        {
            return Process.GetProcesses()
                .Any(p => MeetingProcessNames.Contains(p.ProcessName));
        }
        catch { return false; }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
