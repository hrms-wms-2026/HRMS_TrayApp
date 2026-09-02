namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.Controls;
using ONEVO.Agent.TrayApp.ViewModels;

public partial class WorkLocationPage : ContentPage, IQueryAttributable
{
    private readonly WorkLocationViewModel _vm;

    public WorkLocationPage(WorkLocationViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        ResponsiveTwoPane.Attach(this, PaneGrid, LeftPane, RightPane, narrowLeftMaxHeight: 240);
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        query.TryGetValue("next", out var next);
        _vm.SetNextRoute(next?.ToString());
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await PageAnimations.EntranceAsync(LeftPane, RightPane);
        await _vm.DetectLocationCommand.ExecuteAsync(null);
    }
}
