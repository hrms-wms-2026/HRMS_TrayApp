namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.Controls;
using ONEVO.Agent.TrayApp.ViewModels;

public partial class WorkLocationPage : ContentPage
{
    public WorkLocationPage()
    {
        InitializeComponent();
        ResponsiveTwoPane.Attach(this, PaneGrid, LeftPane, RightPane);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (BindingContext is null && Handler?.MauiContext?.Services is { } sp)
            BindingContext = sp.GetRequiredService<WorkLocationViewModel>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is WorkLocationViewModel vm)
            await vm.OnAppearingAsync();
    }
}
