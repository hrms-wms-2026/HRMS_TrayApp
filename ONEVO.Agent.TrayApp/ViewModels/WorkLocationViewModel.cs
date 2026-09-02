namespace ONEVO.Agent.TrayApp.ViewModels;

using System.Globalization;
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
public sealed partial class WorkLocationViewModel : BaseViewModel
{
    private readonly ILocationService _location;
    private readonly IWorkLocationStore _store;
    private readonly IPreferencesStore _preferences;

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

    public WorkLocationViewModel(ILocationService location, IWorkLocationStore store, IPreferencesStore preferences)
    {
        Title = "Confirm Today's Work Location";
        _location = location;
        _store = store;
        _preferences = preferences;
    }

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

    private bool CanConfirmLocation => SelectedOption is not null && CurrentFix is not null;

    [RelayCommand(CanExecute = nameof(CanConfirmLocation))]
    private async Task ConfirmLocation()
    {
        var option = SelectedOption!;
        var fix = CurrentFix!;

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

        // Legacy flat keys, still read directly by PhotoCaptureWindowViewModel when it submits
        // the setup face photo record — keep them in sync alongside the typed reference above.
        _preferences.Set(SessionPreferenceKeys.WorkLocationCode, option.Code);
        _preferences.Set(SessionPreferenceKeys.WorkLocationDisplay, option.DisplayName);
        _preferences.Set(SessionPreferenceKeys.LiveLatitude, fix.Latitude.ToString("G17", CultureInfo.InvariantCulture));
        _preferences.Set(SessionPreferenceKeys.LiveLongitude, fix.Longitude.ToString("G17", CultureInfo.InvariantCulture));
        WorkLocationFlow.MarkConfirmedToday(_preferences);

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
}
