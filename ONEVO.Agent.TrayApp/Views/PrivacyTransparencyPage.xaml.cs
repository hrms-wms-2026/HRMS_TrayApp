namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.Controls;
using ONEVO.Agent.TrayApp.ViewModels;

public partial class PrivacyTransparencyPage : ContentPage
{
    public PrivacyTransparencyPage(PrivacyTransparencyViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        ResponsiveTwoPane.Attach(this, PaneGrid, LeftPane, RightPane, narrowLeftMaxHeight: 240);
    }
}
