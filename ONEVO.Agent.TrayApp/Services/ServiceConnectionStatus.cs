namespace ONEVO.Agent.TrayApp.Services;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;

/// <summary>
/// App-wide IPC connection state for the footer status bar. Created once at startup (before the
/// pipe starts) so it sees every transition: any message from the Service means connected,
/// <see cref="INamedPipeClient.OnDisconnected"/> means offline.
/// </summary>
public sealed partial class ServiceConnectionStatus : ObservableObject
{
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _label = "Connecting…";

    /// <summary>Unwired instance for view models built without DI (unit tests).</summary>
    public ServiceConnectionStatus() { }

    public ServiceConnectionStatus(INamedPipeClient pipe)
    {
        if (pipe.LastKnownStatus is not null)
            Apply(true);

        pipe.OnStatusReceived += OnStatus;
        pipe.OnStateReceived += OnState;
        pipe.OnDisconnected += OnDisconnected;
    }

    private void OnStatus(StatusResponsePayload _) => Post(true);
    private void OnState(MonitoringState _) => Post(true);
    private void OnDisconnected() => Post(false);

    private void Post(bool connected)
    {
        try
        {
            if (MainThread.IsMainThread) Apply(connected);
            else MainThread.BeginInvokeOnMainThread(() => Apply(connected));
        }
        catch { Apply(connected); /* no MAUI dispatcher in unit tests */ }
    }

    private void Apply(bool connected)
    {
        IsConnected = connected;
        Label = connected ? "Connected" : "Offline";
    }
}
