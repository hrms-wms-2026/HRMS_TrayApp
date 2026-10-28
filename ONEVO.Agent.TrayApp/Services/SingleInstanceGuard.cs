namespace ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Keeps one tray app per Windows session. Two copies both connect to the Service pipe, so every
/// pushed notification was shown twice (seen live: one break alert produced 6 toasts). A second
/// launch signals the running copy to show its window, then exits.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    // "Local\" scopes both objects to the current logon session, so other signed-in users on a
    // shared PC still get their own tray app.
    public const string DefaultName = @"Local\ONEVO.Agent.TrayApp";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private RegisteredWaitHandle? _activateWait;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle activate)
    {
        _mutex = mutex;
        _activate = activate;
    }

    /// <summary>
    /// Returns the guard when this process is the first instance, or null after signalling the
    /// running instance to show its window.
    /// </summary>
    public static SingleInstanceGuard? TryAcquire(string name = DefaultName)
    {
        var mutex = new Mutex(initiallyOwned: false, name);
        bool owned;
        try
        {
            owned = mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // The previous owner crashed without releasing; ownership passes to us.
            owned = true;
        }

        var activate = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Activate");
        if (owned)
            return new SingleInstanceGuard(mutex, activate);

        activate.Set();
        activate.Dispose();
        mutex.Dispose();
        return null;
    }

    /// <summary>Runs <paramref name="onActivate"/> on a pool thread each time another launch is attempted.</summary>
    public void OnActivationRequested(Action onActivate)
    {
        _activateWait?.Unregister(null);
        _activateWait = ThreadPool.RegisterWaitForSingleObject(
            _activate, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _activateWait?.Unregister(null);
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Released from a different thread than the one that acquired it; process exit frees it.
        }
        _mutex.Dispose();
        _activate.Dispose();
    }
}
