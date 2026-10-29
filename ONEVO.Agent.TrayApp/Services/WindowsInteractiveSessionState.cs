namespace ONEVO.Agent.TrayApp.Services;

using Microsoft.Win32;

/// <summary>
/// Tracks lock / unlock and user-switch of the Windows session the tray runs in, from
/// <see cref="SystemEvents.SessionSwitch"/>. That event is reliable here (an interactive process with
/// a message loop), unlike in the Session-0 Service. Assumes unlocked at start: the tray is started by
/// the signed-in user.
/// </summary>
public sealed class WindowsInteractiveSessionState : IInteractiveSessionState, IDisposable
{
    private volatile bool _locked;

    public WindowsInteractiveSessionState() => SystemEvents.SessionSwitch += OnSessionSwitch;

    public bool IsLocked => _locked;

    public event Action<bool>? LockChanged;

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (IsLockedAfter(e.Reason) is not { } locked || locked == _locked)
            return;

        _locked = locked;
        LockChanged?.Invoke(locked);
    }

    /// <summary>
    /// Locked after a lock, sign-off, or this session losing the console or remote connection (user
    /// switch); unlocked after the reverse. Null for changes that are neither (remote-control).
    /// </summary>
    internal static bool? IsLockedAfter(SessionSwitchReason reason) => reason switch
    {
        SessionSwitchReason.SessionLock
            or SessionSwitchReason.ConsoleDisconnect
            or SessionSwitchReason.RemoteDisconnect
            or SessionSwitchReason.SessionLogoff => true,
        SessionSwitchReason.SessionUnlock
            or SessionSwitchReason.ConsoleConnect
            or SessionSwitchReason.RemoteConnect
            or SessionSwitchReason.SessionLogon => false,
        _ => null
    };

    public void Dispose() => SystemEvents.SessionSwitch -= OnSessionSwitch;
}
