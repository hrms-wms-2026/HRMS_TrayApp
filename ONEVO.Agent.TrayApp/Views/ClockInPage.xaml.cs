namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.ViewModels;

public partial class ClockInPage : ContentPage
{
    public ClockInPage()
    {
        InitializeComponent();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (BindingContext is null && Handler?.MauiContext?.Services is { } sp)
            BindingContext = sp.GetRequiredService<ClockInViewModel>();
    }
}
