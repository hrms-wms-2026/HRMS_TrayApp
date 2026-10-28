namespace ONEVO.Agent.TrayApp.Services.Diagnostics;

/// <summary>
/// The one place the Tray's startup and lifecycle trace lines go. Eight components used to append
/// to %LOCALAPPDATA%\ONEVO\Agent\tray-boot.log with their own copy of the same helper, outside
/// logging: no levels, no rotation beyond a launch-time roll, no correlation id (AUD-018).
/// Once <see cref="Attach"/> runs (right after the DI container is built) every line goes to
/// tray.log through ILogger under <see cref="Category"/>. Before that, only the first few startup
/// lines (single-instance check, CreateMauiApp) still need tray-boot.log, because a crash there
/// happens before any logger exists. Never throws.
/// </summary>
public static class TrayBootLog
{
    public const string Category = "ONEVO.Agent.TrayApp.Boot";
    private const long MaxFallbackBytes = 2 * 1024 * 1024;

    private static readonly object FallbackGate = new();
    private static volatile ILogger? _logger;

    /// <summary>Tests redirect the pre-DI file; production uses the default.</summary>
    internal static string FallbackPath { get; set; } = DefaultFallbackPath;

    public static string DefaultFallbackPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ONEVO", "Agent", "tray-boot.log");

    /// <summary>From now on, lines go to tray.log.</summary>
    public static void Attach(ILoggerFactory loggerFactory)
    {
        try
        {
            _logger = loggerFactory.CreateLogger(Category);
        }
        catch
        {
            // Keep using the fallback file.
        }
    }

    /// <summary>Tests only: back to the pre-DI behaviour.</summary>
    internal static void Detach() => _logger = null;

    public static void Write(string source, string message, LogLevel level = LogLevel.Information)
    {
        var logger = _logger;
        if (logger is not null)
        {
            try
            {
                logger.Log(level, "[{Source}] {Message}", source, message);
                return;
            }
            catch
            {
                // Fall through to the file.
            }
        }

        lock (FallbackGate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FallbackPath)!);
                File.AppendAllText(FallbackPath, $"{DateTimeOffset.Now:O} [{source}] {message}{Environment.NewLine}");
            }
            catch
            {
                // This IS the last-resort diagnostic path; nothing further to fall back to.
            }
        }
    }

    /// <summary>Rolls tray-boot.log to .old once it passes 2 MB, so it stays bounded.</summary>
    public static void RollFallbackIfLarge()
    {
        lock (FallbackGate)
        {
            try
            {
                var log = new FileInfo(FallbackPath);
                if (log.Exists && log.Length > MaxFallbackBytes)
                    log.MoveTo(FallbackPath + ".old", overwrite: true);
            }
            catch
            {
                // Locked or missing: keep appending to the current file.
            }
        }
    }
}
