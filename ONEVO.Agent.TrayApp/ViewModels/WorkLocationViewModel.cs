namespace ONEVO.Agent.TrayApp.ViewModels;

public sealed partial class WorkLocationViewModel : BaseViewModel
{
    public IReadOnlyList<WorkLocationOption> ApprovedLocations { get; } =
    [
        new("Chennai Office",   "CHENNAI",   "Tamil Nadu, India"),
        new("Bangalore Office", "BANGALORE", "Karnataka, India"),
        new("Hyderabad Office", "HYDERABAD", "Telangana, India"),
        new("Work From Home",   "WFH",       "Remote Location")
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveAndContinueCommand))]
    private WorkLocationOption? _selectedLocation;

    [ObservableProperty] private string _searchText = string.Empty;

    public IEnumerable<WorkLocationOption> FilteredLocations =>
        string.IsNullOrWhiteSpace(SearchText)
            ? ApprovedLocations
            : ApprovedLocations.Where(l =>
                l.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

    partial void OnSearchTextChanged(string value) =>
        OnPropertyChanged(nameof(FilteredLocations));

    public WorkLocationViewModel() { Title = "Select Your Work Location"; }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task SaveAndContinue()
    {
        Preferences.Set("onevo.work_location_code",    SelectedLocation!.Code);
        Preferences.Set("onevo.work_location_display", SelectedLocation.DisplayName);
        try { await Shell.Current.GoToAsync("//photo"); }
        catch { /* unit tests */ }
    }

    private bool HasSelection => SelectedLocation is not null;
}

public sealed record WorkLocationOption(string DisplayName, string Code, string SubTitle);
