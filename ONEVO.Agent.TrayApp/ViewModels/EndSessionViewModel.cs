namespace ONEVO.Agent.TrayApp.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.Shared.IPC;

public sealed partial class EndSessionViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;

    [ObservableProperty] private string _clockInDisplay     = string.Empty;
    [ObservableProperty] private string _clockOutDisplay    = string.Empty;
    [ObservableProperty] private string _breakTimeDisplay   = "00:00:00";
    [ObservableProperty] private string _afkTimeDisplay     = "00:00:00";
    [ObservableProperty] private string _meetingTimeDisplay = "00:00:00";
    [ObservableProperty] private string _workingTimeDisplay = "00:00:00";
    [ObservableProperty] private string _accuracyScore      = "—";
    [ObservableProperty] private bool   _isConfirming;
    [ObservableProperty] private string? _errorMessage;

    public EndSessionViewModel(INamedPipeClient pipe)
    {
        Title = "End Work Session";
        _pipe = pipe;
    }

    public void LoadSummary(DateTimeOffset clockIn, DateTimeOffset clockOut,
        TimeSpan breakTime, TimeSpan afkTime, TimeSpan meetingTime)
    {
        ClockInDisplay     = clockIn.ToLocalTime().ToString("HH:mm");
        ClockOutDisplay    = clockOut.ToLocalTime().ToString("HH:mm");
        BreakTimeDisplay   = breakTime.ToString(@"hh\:mm\:ss");
        AfkTimeDisplay     = afkTime.ToString(@"hh\:mm\:ss");
        MeetingTimeDisplay = meetingTime.ToString(@"hh\:mm\:ss");
        var working        = (clockOut - clockIn) - breakTime - afkTime;
        WorkingTimeDisplay = working.ToString(@"hh\:mm\:ss");
    }

    [RelayCommand]
    private static void ReturnToWork()
    {
        // Navigate back to ActiveSessionPage
    }

    [RelayCommand]
    private static void VerifyIdentity()
    {
        // Navigate to PhotoCapturePage for clock-out verification
    }

    [RelayCommand]
    private async Task ConfirmClockOutAsync(CancellationToken ct)
    {
        IsConfirming = true;
        ErrorMessage = null;
        try
        {
            var envelope = new IpcEnvelope { Type = IpcMessageTypes.StatusRequest };
            await _pipe.SendEnvelopeAsync(envelope, ct);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsConfirming = false; }
    }
}
