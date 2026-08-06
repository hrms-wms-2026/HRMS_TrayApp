namespace ONEVO.Agent.TrayApp.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class WorkLocationViewModel : BaseViewModel
{
    public IReadOnlyList<WorkLocationOption> ApprovedLocations { get; } =
    [
        new("Central Office",   "HQ"),
        new("Singapore Office", "SG"),
        new("Hyderabad Office", "HYD"),
        new("Remote Work",      "REMOTE")
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
    private static void SaveAndContinue()
    {
        // Navigation wired in AppShell
    }

    private bool HasSelection => SelectedLocation is not null;
}

public sealed record WorkLocationOption(string DisplayName, string Code);
