namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.ViewModels;

public partial class ActiveSessionPage : ContentPage
{
    public ActiveSessionPage()
    {
        InitializeComponent();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (BindingContext is null && Handler?.MauiContext?.Services is { } sp)
        {
            var vm = sp.GetRequiredService<ActiveSessionViewModel>();
            BindingContext = vm;
            vm.StartSession(DateTimeOffset.UtcNow);
        }
    }
}
