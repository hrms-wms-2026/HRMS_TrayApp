namespace ONEVO.Agent.TrayApp.Security;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ONEVO.Agent.TrayApp.Interop;

/// <summary>
/// Minimizes interactive-session data before IPC/disk.
/// Process identity = file name only; never path, never raw window title.
/// </summary>
public static partial class PrivacyScrubber
{
    private static readonly Regex SafeProcessName = SafeProcessNameRegex();

    /// <summary>
    /// Returns the foreground process file name (e.g. "code.exe") or null.
    /// Never returns a full path or window title.
    /// </summary>
    public static string? GetForegroundProcessNameSafe()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;

            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return null;

            string? name;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                name = process.ProcessName;
            }
            catch (ArgumentException)      { return null; }
            catch (InvalidOperationException) { return null; }

            return SanitizeProcessName(name);
        }
        catch { return null; }
    }

    /// <summary>Normalizes and validates a process name. Returns null if unsafe.</summary>
    internal static string? SanitizeProcessName(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return null;

        var name = rawName.Trim().ToLowerInvariant();

        // Strip .exe, truncate base to 96 chars, re-append — so total ≤ 100
        var baseName = name.EndsWith(".exe", StringComparison.Ordinal) ? name[..^4] : name;
        if (baseName.Length > 96)
            baseName = baseName[..96];
        name = baseName + ".exe";

        if (!SafeProcessName.IsMatch(name))
            return null;

        if (name.Contains('\\') || name.Contains('/') || name.Contains(':'))
            return null;

        return name;
    }

    /// <summary>Seconds since last keyboard/mouse input (system-wide), via GetLastInputInfo.</summary>
    public static int GetSecondsSinceLastInput()
    {
        try
        {
            var info = new LastInputInfo { CbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (!NativeMethods.GetLastInputInfo(ref info))
                return 0;

            var idleMs = NativeMethods.GetTickCount() - info.DwTime;
            return (int)Math.Clamp(idleMs / 1000u, 0, int.MaxValue);
        }
        catch
        {
            return 0;
        }
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]{0,98}\.exe$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeProcessNameRegex();
}
