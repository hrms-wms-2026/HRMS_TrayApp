namespace ONEVO.Agent.TrayApp.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.Shared.IPC;

public sealed partial class ActiveSessionViewModel : BaseViewModel, IAsyncDisposable
{
    private readonly INamedPipeClient _pipe;
    private readonly System.Timers.Timer _clockTimer;
    private DateTimeOffset _clockInTime;

    [ObservableProperty] private string _elapsedDisplay     = "00:00:00";
    [ObservableProperty] private string _clockInTimeDisplay = string.Empty;
    [ObservableProperty] private string _breakTimeDisplay   = "00:00:00";
    [ObservableProperty] private string _activeTimeDisplay  = "00:00:00";
    [ObservableProperty] private bool   _isOnBreak;
    [ObservableProperty] private string? _syncMessage;

    public ActiveSessionViewModel(INamedPipeClient pipe)
    {
        Title       = "Your Work Session Is Active";
        _pipe       = pipe;
        _clockTimer = new System.Timers.Timer(1_000) { AutoReset = true };
        _clockTimer.Elapsed += (_, _) => UpdateElapsed();
    }

    public void StartSession(DateTimeOffset clockIn)
    {
        _clockInTime       = clockIn;
        ClockInTimeDisplay = clockIn.ToLocalTime().ToString("HH:mm");
        _clockTimer.Start();
    }

    private void UpdateElapsed()
    {
        var elapsed    = DateTimeOffset.UtcNow - _clockInTime;
        ElapsedDisplay    = elapsed.ToString(@"hh\:mm\:ss");
        ActiveTimeDisplay = elapsed.ToString(@"hh\:mm\:ss");
    }

    [RelayCommand]
    private async Task StartBreakAsync(CancellationToken ct)
    {
        IsOnBreak   = true;
        SyncMessage = "Break started…";
        var envelope = new IpcEnvelope { Type = IpcMessageTypes.StatusRequest };
        await _pipe.SendEnvelopeAsync(envelope, ct);
    }

    [RelayCommand]
    private static void EndWorkSession()
    {
        // Navigate to EndSessionPage
    }

    public async ValueTask DisposeAsync()
    {
        _clockTimer.Stop();
        _clockTimer.Dispose();
        await Task.CompletedTask;
    }
}
