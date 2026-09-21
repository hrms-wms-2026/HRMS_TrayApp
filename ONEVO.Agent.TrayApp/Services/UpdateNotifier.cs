namespace ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Periodically asks the backend (via <see cref="IUpdateChecker"/>) whether a newer installer exists and
/// raises one informational notification per new version. Silent on failure by design: an update
/// check must never disturb clocking in/out. The actionable download button lives on the connect screen.
/// </summary>
public sealed class UpdateNotifier
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IUpdateChecker _checker;
    private readonly Action<string, string> _show;
    private readonly ILogger<UpdateNotifier> _logger;
    private string? _lastNotifiedVersion;

    public UpdateNotifier(IUpdateChecker checker, NotificationService notifications, ILogger<UpdateNotifier> logger)
        : this(checker, notifications.ShowInfo, logger) { }

    public UpdateNotifier(IUpdateChecker checker, Action<string, string> show, ILogger<UpdateNotifier> logger)
    {
        _checker = checker;
        _show = show;
        _logger = logger;
    }

    /// <summary>Runs until cancelled: first check shortly after startup (the pipe needs a moment), then every 6 hours.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(InitialDelay, ct);
            using var timer = new PeriodicTimer(Interval);
            do
            {
                await CheckOnceAsync(ct);
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    public async Task CheckOnceAsync(CancellationToken ct)
    {
        try
        {
            var update = await _checker.CheckAsync(ct);
            if (update?.LatestVersion is null || update.LatestVersion == _lastNotifiedVersion)
                return;

            _lastNotifiedVersion = update.LatestVersion;
            _show(
                "ONEVO update available",
                update.Mandatory
                    ? $"A required update (v{update.LatestVersion}) is available. Open ONEVO WorkPulse to download it."
                    : $"Version {update.LatestVersion} is available. Open ONEVO WorkPulse to download it.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Update check failed");
        }
    }
}
