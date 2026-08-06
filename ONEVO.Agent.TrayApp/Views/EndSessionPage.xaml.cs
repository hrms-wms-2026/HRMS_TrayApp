namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.ViewModels;

public partial class EndSessionPage : ContentPage
{
    public EndSessionPage()
    {
        InitializeComponent();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (BindingContext is null && Handler?.MauiContext?.Services is { } sp)
            BindingContext = sp.GetRequiredService<EndSessionViewModel>();
    }
}
