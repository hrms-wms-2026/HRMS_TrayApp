namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.Controls;
using ONEVO.Agent.TrayApp.ViewModels;

public partial class EndSessionPage : ContentPage
{
    public EndSessionPage(EndSessionViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        ResponsiveTwoPane.Attach(this, PaneGrid, LeftPane, RightPane);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is EndSessionViewModel vm)
            vm.OnAppearing();

        _ = PageAnimations.EntranceAsync(LeftPane, RightPane);
        _ = PageAnimations.PopAsync(CompletedBadge);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is EndSessionViewModel vm)
            vm.OnDisappearing();
    }
}
