namespace ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Shared once-per-local-day gate for the "Break time exceeded" toast. Both sources go through
/// <see cref="TryClaim"/>: the active session's live break timer and the polled server
/// notification. Whichever arrives first shows the toast and the other one stays silent.
/// The claim lapses at local midnight, so a tray app left running overnight still alerts the
/// next day.
/// </summary>
public static class BreakAllowanceAlert
{
    private static readonly object Gate = new();
    private static DateOnly? _claimedOn;

    /// <summary>Overridable for tests; the employee's local calendar day.</summary>
    internal static Func<DateOnly> Today { get; set; } = () => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>Returns true for the first caller today, which should then show the toast.</summary>
    public static bool TryClaim()
    {
        lock (Gate)
        {
            var today = Today();
            if (_claimedOn == today)
                return false;

            _claimedOn = today;
            return true;
        }
    }

    internal static void ResetForTests()
    {
        lock (Gate)
            _claimedOn = null;
        Today = () => DateOnly.FromDateTime(DateTime.Now);
    }
}
