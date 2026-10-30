namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.Controls;
using ONEVO.Agent.TrayApp.Services;
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

    private void OnOptionTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject { BindingContext: WorkLocationOption option })
            _vm.SelectOptionCommand.Execute(option);
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        query.TryGetValue("next", out var next);
        _vm.SetNextRoute(next?.ToString());
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await UiTasks.RunSafelyAsync(async () =>
        {
            await PageAnimations.EntranceAsync(LeftPane, RightPane);
            await _vm.DetectLocationCommand.ExecuteAsync(null);
        }, $"{nameof(WorkLocationPage)}.{nameof(OnAppearing)}");
    }
}
