namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.Controls;
using ONEVO.Agent.TrayApp.ViewModels;

public partial class ActiveSessionPage : ContentPage
{
    private CancellationTokenSource? _statusPulse;

    public ActiveSessionPage(ActiveSessionViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        ResponsiveTwoPane.Attach(this, PaneGrid, LeftPane, RightPane);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ActiveSessionViewModel vm)
            vm.OnAppearing();

        _ = PageAnimations.EntranceAsync(LeftPane, RightPane);
        _statusPulse = PageAnimations.StartPulse(StatusDot, scaleTo: 1.35, duration: 850);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is ActiveSessionViewModel vm)
            vm.OnDisappearing();

        PageAnimations.StopPulse(_statusPulse, StatusDot);
    }
}
