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
            if (hwnd == IntPtr.Zero)
                return null;

            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0)
                return null;

            using var process = Process.GetProcessById((int)pid);
            var name = process.ProcessName;
            if (string.IsNullOrWhiteSpace(name))
                return null;

            // Process.ProcessName omits extension; normalize to *.exe for backend consistency.
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name += ".exe";

            name = name.ToLowerInvariant();
            if (name.Length > 100)
                name = name[..100];

            if (!SafeProcessName.IsMatch(name))
                return null;

            // Reject path separators / drive letters if anything slipped through.
            if (name.Contains('\\') || name.Contains('/') || name.Contains(':'))
                return null;

            return name;
        }
        catch
        {
            return null;
        }
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
