namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.Controls;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.ViewModels;

public partial class ActiveSessionPage : ContentPage
{
    private CancellationTokenSource? _statusPulse;
    private bool _onScreen;

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

        _onScreen = true;
        TrayWindowVisibility.Changed -= OnWindowVisibilityChanged; // static event: never double-root
        TrayWindowVisibility.Changed += OnWindowVisibilityChanged;

        _ = PageAnimations.EntranceAsync(LeftPane, RightPane);
        OnWindowVisibilityChanged(TrayWindowVisibility.IsVisible);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is ActiveSessionViewModel vm)
            vm.OnDisappearing();

        _onScreen = false;
        TrayWindowVisibility.Changed -= OnWindowVisibilityChanged;
        StopStatusPulse();
    }

    // Close-to-tray hides the window without OnDisappearing, so the infinite pulse and the 1s
    // timer would otherwise animate an invisible page for the whole workday.
    private void OnWindowVisibilityChanged(bool visible)
    {
        if (!_onScreen)
            return;

        if (BindingContext is ActiveSessionViewModel vm)
            vm.SetWindowVisible(visible);

        if (visible)
            _statusPulse ??= PageAnimations.StartPulse(StatusDot, scaleTo: 1.35, duration: 850);
        else
            StopStatusPulse();
    }

    private void StopStatusPulse()
    {
        PageAnimations.StopPulse(_statusPulse, StatusDot);
        _statusPulse = null;
    }
}
