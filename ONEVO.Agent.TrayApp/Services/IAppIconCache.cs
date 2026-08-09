namespace ONEVO.Agent.TrayApp.Services;

/// <summary>In-memory only — extracted icons never touch IPC or disk (§8).</summary>
public interface IAppIconCache
{
    ImageSource? GetIcon(string processName);

    /// <summary>Best-effort extraction from the foreground window's owning process.
    /// No-op if already attempted (success or permanent miss) for this process name.</summary>
    void TryCacheFromForegroundWindow(IntPtr hwnd, string processName);
}
