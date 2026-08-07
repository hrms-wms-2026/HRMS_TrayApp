namespace ONEVO.Agent.TrayApp.Controls;

public partial class AppHeaderBar : ContentView
{
    public static readonly BindableProperty SubtitleProperty =
        BindableProperty.Create(nameof(Subtitle), typeof(string), typeof(AppHeaderBar), "Your Workplace. Simplified.");

    public static readonly BindableProperty ShowSubtitleProperty =
        BindableProperty.Create(nameof(ShowSubtitle), typeof(bool), typeof(AppHeaderBar), true);

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public bool ShowSubtitle
    {
        get => (bool)GetValue(ShowSubtitleProperty);
        set => SetValue(ShowSubtitleProperty, value);
    }

    public AppHeaderBar() => InitializeComponent();
}
