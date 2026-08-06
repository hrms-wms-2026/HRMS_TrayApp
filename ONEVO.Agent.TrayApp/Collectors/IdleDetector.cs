namespace ONEVO.Agent.TrayApp.Collectors;

using ONEVO.Agent.TrayApp.Security;

public static class IdleDetector
{
    public const int IdleThresholdSeconds = 120;

    public static bool IsIdle() =>
        PrivacyScrubber.GetSecondsSinceLastInput() >= IdleThresholdSeconds;

    public static int GetIdleSeconds() =>
        PrivacyScrubber.GetSecondsSinceLastInput();
}
