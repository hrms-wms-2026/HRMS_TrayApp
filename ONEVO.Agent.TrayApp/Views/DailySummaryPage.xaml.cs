namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.ViewModels;

public partial class DailySummaryPage : ContentPage
{
    public DailySummaryPage(DailySummaryViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is DailySummaryViewModel vm)
            vm.OnAppearing();
    }
}
