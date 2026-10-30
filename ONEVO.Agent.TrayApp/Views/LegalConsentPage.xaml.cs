namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Controls;
using ONEVO.Agent.TrayApp.ViewModels;

public partial class LegalConsentPage : ContentPage
{
    private readonly LegalConsentViewModel _vm;

    public LegalConsentPage(LegalConsentViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        ResponsiveTwoPane.Attach(this, PaneGrid, LeftPane, RightPane, narrowLeftMaxHeight: 240);
    }

    private void OnViewDocumentClicked(object? sender, EventArgs e)
    {
        if (sender is BindableObject { BindingContext: PendingLegalDocumentPayload document })
            _vm.OpenDocumentCommand.Execute(document);
    }
}
