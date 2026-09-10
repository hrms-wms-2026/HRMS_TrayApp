namespace ONEVO.Agent.TrayApp.ViewModels;

using System.Globalization;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;

/// <summary>One of the three approved work-location kinds shown on the confirmation screen.</summary>
public sealed partial class WorkLocationOption : ObservableObject
{
    public WorkLocationOption(
        WorkLocationKind kind,
        string code,
        string displayName,
        string subtitle,
        double radiusMeters,
        string iconSource)
    {
        Kind = kind;
        Code = code;
        DisplayName = displayName;
        Subtitle = subtitle;
        RadiusMeters = radiusMeters;
        IconSource = iconSource;
    }

    public WorkLocationKind Kind { get; }
    public string Code { get; }
    public string DisplayName { get; }
    public string Subtitle { get; }
    public double RadiusMeters { get; }
    public string IconSource { get; }

    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// Lets the employee confirm today's work location during setup: pick Office/Work From
/// Home/Other, capture one live GPS fix, and save it as the reference used later to verify
/// Clock In location. Read-once by design — no background or continuous location tracking.
/// </summary>
public sealed partial class WorkLocationViewModel : BaseViewModel, IDisposable
{
    private readonly ILocationService _location;
    private readonly IWorkLocationStore _store;
    private readonly IPreferencesStore _preferences;
    private readonly INamedPipeClient _pipe;
    private AgentPolicy? _currentPolicy;

    /// <summary>Safe default (off) when no policy has arrived yet, mirroring the backend's own
    /// "no config row = false" convention for this same WorkLocationVerification capability.</summary>
    private bool LocationTrackingEnabled => _currentPolicy?.LocationTrackingEnabled ?? false;

    public IReadOnlyList<WorkLocationOption> Options { get; } =
    [
        new(WorkLocationKind.Office, "OFFICE", "Office", "At your registered office", 300,
            "icon_office_building.png"),
        new(WorkLocationKind.WorkFromHome, "WFH", "Work From Home", "Remote location", 250,
            "icon_home_house.png"),
        new(WorkLocationKind.OtherApprovedLocation, "OTHER", "Other Approved Location",
            "Client site or approved external workplace", 250, "icon_office_building.png"),
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmLocationCommand))]
    private WorkLocationOption? _selectedOption;

    [ObservableProperty] private bool _isDetecting;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmLocationCommand))]
    private GeoLocationFix? _currentFix;

    [ObservableProperty] private string _statusText = "Detecting your current location…";
    [ObservableProperty] private string _detectionTitle = "Detecting location";
    [ObservableProperty] private string _detectionDetail = "Finding your current position…";
    [ObservableProperty] private bool _isLocationVerified;
    [ObservableProperty] private bool _hasDetectionError;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isConfirmed;

    private string _afterConfirmRoute = WorkLocationFlow.PrepareRoute;

    public WorkLocationViewModel(
        ILocationService location, IWorkLocationStore store, IPreferencesStore preferences, INamedPipeClient pipe)
    {
        Title = "Confirm Today's Work Location";
        _location = location;
        _store = store;
        _preferences = preferences;
        _pipe = pipe;
        _currentPolicy = pipe.LastKnownPolicy;
        _pipe.OnPolicyReceived += HandlePolicyReceived;
    }

    private void HandlePolicyReceived(AgentPolicy policy)
    {
        _currentPolicy = policy;
        ConfirmLocationCommand.NotifyCanExecuteChanged();
    }

    public void Dispose() => _pipe.OnPolicyReceived -= HandlePolicyReceived;

    public void SetNextRoute(string? next) =>
        _afterConfirmRoute = WorkLocationFlow.ResolveNextRoute(next);

    [RelayCommand]
    private async Task DetectLocation()
    {
        IsDetecting = true;
        ErrorMessage = null;
        HasDetectionError = false;
        IsLocationVerified = false;
        DetectionTitle = "Detecting location";
        DetectionDetail = "Finding your current position…";
        StatusText = "Detecting your current location…";

        if (!LocationTrackingEnabled)
        {
            // Location tracking is off for this account - do not even request the OS location
            // permission. The employee can still confirm a work location below without a GPS fix.
            CurrentFix = null;
            IsLocationVerified = false;
            DetectionTitle = "Location tracking is off";
            DetectionDetail = "Your organization has turned off location tracking. Pick your work location below to continue.";
            StatusText = "Location tracking is turned off for your account.";
            IsDetecting = false;
            return;
        }

        try
        {
            var result = await _location.GetCurrentAsync();
            if (result.IsSuccess)
            {
                CurrentFix = result.Fix;
                StatusText = FormatFixText(result.Fix!);
                DetectionTitle = "Detected location";
                DetectionDetail =
                    "You are outside your registered office location. Remote work is available based on your policy.";
                IsLocationVerified = true;
            }
            else
            {
                CurrentFix = null;
                ErrorMessage = DescribeFailure(result.Failure!.Value);
                StatusText = "Location unavailable.";
                DetectionTitle = "Location unavailable";
                DetectionDetail = ErrorMessage;
                HasDetectionError = true;
                IsLocationVerified = false;
            }
        }
        finally
        {
            IsDetecting = false;
        }
    }

    [RelayCommand]
    private void SelectOption(WorkLocationOption option)
    {
        foreach (var opt in Options)
            opt.IsSelected = ReferenceEquals(opt, option);

        SelectedOption = option;
    }

    private bool CanConfirmLocation =>
        SelectedOption is not null && (CurrentFix is not null || !LocationTrackingEnabled);

    [RelayCommand(CanExecute = nameof(CanConfirmLocation))]
    private async Task ConfirmLocation()
    {
        var option = SelectedOption!;
        var fix = CurrentFix;

        // Legacy flat keys, still read directly by PhotoCaptureWindowViewModel when it submits
        // the setup face photo record — keep them in sync alongside the typed reference below.
        _preferences.Set(SessionPreferenceKeys.WorkLocationCode, option.Code);
        _preferences.Set(SessionPreferenceKeys.WorkLocationDisplay, option.DisplayName);

        if (fix is not null)
        {
            var reference = new WorkLocationReference(
                option.Kind,
                option.Code,
                option.DisplayName,
                fix.Latitude,
                fix.Longitude,
                fix.AccuracyMeters,
                option.RadiusMeters,
                DateTimeOffset.UtcNow);

            _store.Save(reference);
            _preferences.Set(SessionPreferenceKeys.LiveLatitude, fix.Latitude.ToString("G17", CultureInfo.InvariantCulture));
            _preferences.Set(SessionPreferenceKeys.LiveLongitude, fix.Longitude.ToString("G17", CultureInfo.InvariantCulture));
        }
        // Location tracking is off for this account: no GPS fix was captured, so there is nothing
        // to save as a WorkLocationReference or Live* coordinates — the code/display keys above are
        // enough to record which option the employee picked.

        var locationType = ToBackendLocationType(option.Code);
        var backendAccepted = await TrySendConfirmationAsync(locationType, fix);

        if (backendAccepted)
        {
            // Only record the day as confirmed once the backend has actually accepted it. A failed
            // or timed-out pipe send leaves the day unmarked so the screen re-prompts next time,
            // rather than silently skipping backend registration - which would leave the backend's
            // LocationRuleEvaluatorJob treating the employee as never having confirmed today.
            WorkLocationFlow.MarkConfirmedToday(_preferences);
        }

        IsConfirmed = true;

        try { await Shell.Current.GoToAsync(_afterConfirmRoute); }
        catch { /* unit tests */ }
    }

    [RelayCommand]
    private async Task NavigateBack()
    {
        try { await Shell.Current.GoToAsync(_afterConfirmRoute); }
        catch { /* unit tests */ }
    }

    private static string FormatFixText(GeoLocationFix fix)
    {
        var text = $"Detected: {fix.Latitude:F5}, {fix.Longitude:F5}";
        if (fix.AccuracyMeters is { } accuracy)
            text += $" (±{accuracy:F0} m)";
        return text;
    }

    private static string DescribeFailure(LocationCaptureFailure failure) => failure switch
    {
        LocationCaptureFailure.PermissionDenied =>
            "Location permission was denied. Enable it in Windows Settings > Privacy > Location for OneXso WorkPulse, then retry.",
        LocationCaptureFailure.ServicesDisabled =>
            "Windows Location Services are turned off. Enable Location in Windows Settings, then retry.",
        LocationCaptureFailure.NotSupported =>
            "This device does not support location detection.",
        LocationCaptureFailure.TimedOut =>
            "Location detection timed out. Please retry.",
        _ => "Could not detect your location. Please retry."
    };

    private static string ToBackendLocationType(string code) => code switch
    {
        "OFFICE" => "office",
        "WFH" => "home",
        "OTHER" => "other",
        _ => "other"
    };

    /// <summary>Sends today's confirmation to the Service process and reports whether the backend
    /// accepted it. A null result (pipe timeout / no response) or Success=false counts as not
    /// accepted, and any transport exception is swallowed the same way.</summary>
    private async Task<bool> TrySendConfirmationAsync(string locationType, GeoLocationFix? fix)
    {
        try
        {
            var result = await _pipe.SendWorkLocationConfirmAsync(
                locationType, fix?.Latitude, fix?.Longitude, fix?.AccuracyMeters, CancellationToken.None);
            return result is { Success: true };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WorkLocationConfirm send failed: {ex.Message}");
            return false;
        }
    }
}
