namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.Controls;
using ONEVO.Agent.TrayApp.ViewModels;

public partial class ReviewSetupPage : ContentPage
{
    public ReviewSetupPage()
    {
        InitializeComponent();
        ResponsiveTwoPane.Attach(this, PaneGrid, LeftPane, RightPane);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (BindingContext is null && Handler?.MauiContext?.Services is { } sp)
            BindingContext = sp.GetRequiredService<ReviewSetupViewModel>();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ReviewSetupViewModel vm)
            vm.OnAppearing();
    }
}
