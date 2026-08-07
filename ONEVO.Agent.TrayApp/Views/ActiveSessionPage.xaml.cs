namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.Controls;
using ONEVO.Agent.TrayApp.ViewModels;

public partial class ActiveSessionPage : ContentPage
{
    public ActiveSessionPage()
    {
        InitializeComponent();
        ResponsiveTwoPane.Attach(this, PaneGrid, LeftPane, RightPane);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (BindingContext is null && Handler?.MauiContext?.Services is { } sp)
            BindingContext = sp.GetRequiredService<ActiveSessionViewModel>();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ActiveSessionViewModel vm)
            vm.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is ActiveSessionViewModel vm)
            vm.OnDisappearing();
    }
}
