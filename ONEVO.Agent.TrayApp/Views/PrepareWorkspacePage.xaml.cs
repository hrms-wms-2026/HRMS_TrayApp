namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.ViewModels;

public partial class PrepareWorkspacePage : ContentPage
{
    public PrepareWorkspacePage()
    {
        InitializeComponent();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (BindingContext is null && Handler?.MauiContext?.Services is { } sp)
            BindingContext = sp.GetRequiredService<PrepareWorkspaceViewModel>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is PrepareWorkspaceViewModel vm)
            await vm.LoadAsync();
    }
}
