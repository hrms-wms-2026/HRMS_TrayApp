using System.Windows.Input;

namespace ONEVO.Agent.TrayApp.Controls;

public partial class FooterStatusBar : ContentView
{
    public static readonly BindableProperty VersionTextProperty =
        BindableProperty.Create(nameof(VersionText), typeof(string), typeof(FooterStatusBar), "Version 1.0.0");

    public static readonly BindableProperty ConnectionLabelProperty =
        BindableProperty.Create(nameof(ConnectionLabel), typeof(string), typeof(FooterStatusBar), "Connected");

    public static readonly BindableProperty IsConnectedProperty =
        BindableProperty.Create(nameof(IsConnected), typeof(bool), typeof(FooterStatusBar), true);

    public static readonly BindableProperty ShowSignOutProperty =
        BindableProperty.Create(nameof(ShowSignOut), typeof(bool), typeof(FooterStatusBar), false);

    public static readonly BindableProperty SignOutCommandProperty =
        BindableProperty.Create(nameof(SignOutCommand), typeof(ICommand), typeof(FooterStatusBar));

    public string VersionText
    {
        get => (string)GetValue(VersionTextProperty);
        set => SetValue(VersionTextProperty, value);
    }

    public string ConnectionLabel
    {
        get => (string)GetValue(ConnectionLabelProperty);
        set => SetValue(ConnectionLabelProperty, value);
    }

    public bool IsConnected
    {
        get => (bool)GetValue(IsConnectedProperty);
        set => SetValue(IsConnectedProperty, value);
    }

    public bool ShowSignOut
    {
        get => (bool)GetValue(ShowSignOutProperty);
        set => SetValue(ShowSignOutProperty, value);
    }

    public ICommand SignOutCommand
    {
        get => (ICommand)GetValue(SignOutCommandProperty);
        set => SetValue(SignOutCommandProperty, value);
    }

    public FooterStatusBar() => InitializeComponent();
}
