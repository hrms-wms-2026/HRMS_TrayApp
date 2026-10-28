namespace ONEVO.Agent.TrayApp.Services.Diagnostics;

public static class TrayLogging
{
    public const string CrashCategory = "ONEVO.Agent.TrayApp.Crash";
    public const string FileName = "tray.log";

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ONEVO", "Agent", "logs");

    public static JsonFileLoggerProvider CreateFileProvider(string? directory = null) =>
        new(directory ?? DefaultDirectory, FileName);

    /// <summary>
    /// Routes Tray logs to <paramref name="provider"/> at Information and above. Framework
    /// categories are held to Warning: HttpClient logs full request URIs at Information, and
    /// MAUI/Microsoft noise would crowd out the Tray's own lines.
    /// </summary>
    public static ILoggingBuilder AddTrayFileLogging(this ILoggingBuilder logging, JsonFileLoggerProvider provider)
    {
        logging.SetMinimumLevel(LogLevel.Information);
        logging.AddFilter("Microsoft", LogLevel.Warning);
        logging.AddFilter("System", LogLevel.Warning);
        logging.AddProvider(provider);
        return logging;
    }

    /// <summary>Records an exception that is about to (or may) take the Tray down.</summary>
    public static void LogCrash(this ILogger logger, string source, Exception? exception, bool isTerminating)
    {
        try
        {
            logger.LogCritical(exception, "Unhandled exception from {Source} (terminating={IsTerminating})",
                source, isTerminating);
        }
        catch
        {
            // Crash reporting must never raise a second exception.
        }
    }
}
