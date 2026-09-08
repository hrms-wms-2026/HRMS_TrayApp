namespace ONEVO.Agent.TrayApp.Controls;

public partial class PopupClockBadge : ContentView
{
    public static readonly BindableProperty ImageSourceProperty =
        BindableProperty.Create(nameof(ImageSource), typeof(string), typeof(PopupClockBadge), "icon3d_clock.png");

    public string ImageSource
    {
        get => (string)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    public PopupClockBadge() => InitializeComponent();
}
