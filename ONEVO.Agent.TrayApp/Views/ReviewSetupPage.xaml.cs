namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.Controls;
using ONEVO.Agent.TrayApp.ViewModels;

public partial class ReviewSetupPage : ContentPage
{
    public ReviewSetupPage(ReviewSetupViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        ResponsiveTwoPane.Attach(this, PaneGrid, LeftPane, RightPane);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ReviewSetupViewModel vm)
            vm.OnAppearing();
    }
}
