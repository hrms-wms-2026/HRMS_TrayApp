namespace ONEVO.Agent.Service.Api;

public static class AgentApiRoutes
{
    public const string ActivitySnapshots   = "/api/v1/monitoring/activity/snapshots";
    public const string AppUsageSnapshots   = "/api/v1/monitoring/app-usage/snapshots";
    public const string DeviceStateSnapshots = "/api/v1/monitoring/device-state/snapshots";
    public const string WorkSessionSubmit   = "/api/v1/monitoring/work-sessions";
    public const string ScreenshotSubmit    = "/api/v1/monitoring/tray/screenshots";
    public const string TrayPolicy          = "/api/v1/monitoring/tray/policy";

    public const string ActivationExchange = "/api/v1/monitoring/activation/exchange";
    public const string ActivationRefresh  = "/api/v1/monitoring/activation/refresh";
    public const string ActivationRevoke   = "/api/v1/monitoring/activation/revoke";

    public const string BiometricEnrollmentAttemptCreate   = "/api/v1/monitoring/biometrics/enrollment-attempts";
    public const string BiometricEnrollmentAttemptComplete = "/api/v1/monitoring/biometrics/enrollment-attempts/{0}/complete";
}
