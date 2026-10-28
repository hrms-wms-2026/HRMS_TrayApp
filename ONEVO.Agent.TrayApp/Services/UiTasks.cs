namespace ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Runs the work of an <c>async void</c> page or handler method so a failure is logged instead of
/// reaching the dispatcher's unhandled-exception path, which can terminate the tray app (AUD-032).
/// Awaiting an AsyncRelayCommand rethrows the command's exception, so page lifecycle methods that
/// await commands need this too.
/// </summary>
public static class UiTasks
{
    private static readonly Action<string, Exception> DefaultReport = WriteBootLog;

    /// <summary>Where failures go; tests replace it.</summary>
    internal static Action<string, Exception> Report { get; set; } = DefaultReport;

    public static async Task RunSafelyAsync(Func<Task> work, string context)
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
            // The page went away or the operation was cancelled — nothing to report.
        }
        catch (Exception ex)
        {
            try { Report(context, ex); }
            catch { /* reporting must never throw back into the UI */ }
        }
    }

    internal static void ResetForTests() => Report = DefaultReport;

    private static void WriteBootLog(string context, Exception ex) =>
        TrayBootLog.Write("UiTasks", $"{context} failed: {ex}", LogLevel.Error);
}
