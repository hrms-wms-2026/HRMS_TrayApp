namespace ONEVO.Agent.TrayApp.Controls;

public partial class FooterStatusBar : ContentView
{
    public static readonly BindableProperty VersionTextProperty =
        BindableProperty.Create(nameof(VersionText), typeof(string), typeof(FooterStatusBar), "Version 1.0.0");

    public static readonly BindableProperty ConnectionLabelProperty =
        BindableProperty.Create(nameof(ConnectionLabel), typeof(string), typeof(FooterStatusBar), "Connected");

    public static readonly BindableProperty IsConnectedProperty =
        BindableProperty.Create(nameof(IsConnected), typeof(bool), typeof(FooterStatusBar), true);

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

    public FooterStatusBar() => InitializeComponent();
}
