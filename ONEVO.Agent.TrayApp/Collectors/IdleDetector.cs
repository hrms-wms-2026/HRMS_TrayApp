namespace ONEVO.Agent.TrayApp.Collectors;

using ONEVO.Agent.TrayApp.Security;

public static class IdleDetector
{
    /// <summary>Used only before a real server policy has arrived (see
    /// CollectorCoordinator.LocalDefaultPolicy) - callers with an AgentPolicy in hand must pass its
    /// own IdleThresholdMinutes * 60 instead of relying on this.</summary>
    public const int DefaultIdleThresholdSeconds = 120;

    public static bool IsIdle(int thresholdSeconds) =>
        PrivacyScrubber.GetSecondsSinceLastInput() >= thresholdSeconds;

    public static int GetIdleSeconds() =>
        PrivacyScrubber.GetSecondsSinceLastInput();
}
