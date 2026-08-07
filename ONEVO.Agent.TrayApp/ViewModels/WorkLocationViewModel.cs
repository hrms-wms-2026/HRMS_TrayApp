namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.TrayApp.Services;

public sealed partial class WorkLocationViewModel : BaseViewModel
{
    private readonly ILocationService _location;

    public IReadOnlyList<WorkLocationOption> ApprovedLocations { get; } =
    [
        // Approx office pins for nearest-match against live GPS.
        new("Chennai Office",   "CHENNAI",   "Tamil Nadu, India",  13.0827, 80.2707),
        new("Bangalore Office", "BANGALORE", "Karnataka, India",  12.9716, 77.5946),
        new("Hyderabad Office", "HYDERABAD", "Telangana, India",  17.3850, 78.4867),
        new("Work From Home",   "WFH",       "Remote Location",   null,    null)
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveAndContinueCommand))]
    private WorkLocationOption? _selectedLocation;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isDetectingLocation;
    [ObservableProperty] private string _liveLocationStatus = "Live location not detected yet.";
    [ObservableProperty] private string? _liveCoordsText;
    [ObservableProperty] private double? _liveLatitude;
    [ObservableProperty] private double? _liveLongitude;

    /// <summary>Offices farther than this (km) default to Work From Home suggestion.</summary>
    public const double NearestOfficeMaxKm = 80;

    public IEnumerable<WorkLocationOption> FilteredLocations =>
        string.IsNullOrWhiteSpace(SearchText)
            ? ApprovedLocations
            : ApprovedLocations.Where(l =>
                l.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || l.SubTitle.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

    partial void OnSearchTextChanged(string value) =>
        OnPropertyChanged(nameof(FilteredLocations));

    partial void OnSelectedLocationChanged(WorkLocationOption? value)
    {
        foreach (var loc in ApprovedLocations)
            loc.IsSelected = ReferenceEquals(loc, value);
    }

    public WorkLocationViewModel(ILocationService location)
    {
        Title = "Select Your Work Location";
        _location = location;
    }

    /// <summary>Parameterless for existing unit tests — uses a no-op location source.</summary>
    public WorkLocationViewModel() : this(new NullLocationService()) { }

    public Task OnAppearingAsync() => DetectLiveLocationAsync();

    [RelayCommand]
    private async Task DetectLiveLocationAsync()
    {
        if (IsDetectingLocation) return;

        IsDetectingLocation = true;
        LiveLocationStatus = "Detecting live location…";
        LiveCoordsText = null;
        try
        {
            var point = await _location.GetCurrentAsync();
            if (point is null)
            {
                LiveLocationStatus = "Could not get live location. Enable Location for Onexso, or pick manually.";
                LiveLatitude = LiveLongitude = null;
                return;
            }

            LiveLatitude = point.Latitude;
            LiveLongitude = point.Longitude;
            LiveCoordsText = $"{point.Latitude:F5}, {point.Longitude:F5}";
            if (point.AccuracyMeters is { } acc)
                LiveCoordsText += $" (±{acc:F0} m)";

            var match = FindNearestOffice(point.Latitude, point.Longitude);
            if (match is null)
            {
                LiveLocationStatus = $"Live location: {LiveCoordsText}. No office nearby — pick a location.";
                return;
            }

            SelectedLocation = match.Option;
            LiveLocationStatus = match.IsRemoteFallback
                ? $"Live location: {LiveCoordsText}. Far from offices — suggested Work From Home."
                : $"Live location: {LiveCoordsText}. Nearest: {match.Option.DisplayName} ({match.DistanceKm:F1} km).";
        }
        catch (Exception ex)
        {
            LiveLocationStatus = $"Live location failed: {ex.Message}";
        }
        finally
        {
            IsDetectingLocation = false;
        }
    }

    /// <summary>Picks nearest office with coordinates; falls back to WFH if all are far.</summary>
    public NearestMatch? FindNearestOffice(double lat, double lon)
    {
        WorkLocationOption? best = null;
        var bestKm = double.MaxValue;

        foreach (var loc in ApprovedLocations)
        {
            if (loc.Latitude is null || loc.Longitude is null)
                continue;

            var km = HaversineKm(lat, lon, loc.Latitude.Value, loc.Longitude.Value);
            if (km < bestKm)
            {
                bestKm = km;
                best = loc;
            }
        }

        if (best is null)
            return null;

        if (bestKm > NearestOfficeMaxKm)
        {
            var wfh = ApprovedLocations.FirstOrDefault(l => l.Code == "WFH");
            if (wfh is not null)
                return new NearestMatch(wfh, bestKm, IsRemoteFallback: true);
        }

        return new NearestMatch(best, bestKm, IsRemoteFallback: false);
    }

    public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        static double Rad(double d) => d * Math.PI / 180.0;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2))
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task SaveAndContinue()
    {
        try
        {
            Preferences.Set("onevo.work_location_code",    SelectedLocation!.Code);
            Preferences.Set("onevo.work_location_display", SelectedLocation.DisplayName);
            if (LiveLatitude is { } la && LiveLongitude is { } lo)
            {
                Preferences.Set("onevo.live_latitude",  la);
                Preferences.Set("onevo.live_longitude", lo);
            }
        }
        catch { /* no MAUI Preferences host in unit tests */ }

        try { await Shell.Current.GoToAsync("//photo"); }
        catch { /* unit tests */ }
    }

    private bool HasSelection => SelectedLocation is not null;

    public sealed record NearestMatch(WorkLocationOption Option, double DistanceKm, bool IsRemoteFallback);

    private sealed class NullLocationService : ILocationService
    {
        public Task<GeoPoint?> GetCurrentAsync(CancellationToken ct = default) =>
            Task.FromResult<GeoPoint?>(null);
    }
}

public sealed partial class WorkLocationOption : ObservableObject
{
    public WorkLocationOption(string displayName, string code, string subTitle,
        double? latitude = null, double? longitude = null)
    {
        DisplayName = displayName;
        Code = code;
        SubTitle = subTitle;
        Latitude = latitude;
        Longitude = longitude;
    }

    public string DisplayName { get; }
    public string Code { get; }
    public string SubTitle { get; }
    public double? Latitude { get; }
    public double? Longitude { get; }

    [ObservableProperty] private bool _isSelected;
}
