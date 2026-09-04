namespace ONEVO.Agent.TrayApp.Controls;

public partial class PopupInfoRow : ContentView
{
    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(nameof(Icon), typeof(string), typeof(PopupInfoRow), string.Empty);

    public static readonly BindableProperty PrefixProperty =
        BindableProperty.Create(nameof(Prefix), typeof(string), typeof(PopupInfoRow), string.Empty,
            propertyChanged: OnTextChanged);

    public static readonly BindableProperty ValueProperty =
        BindableProperty.Create(nameof(Value), typeof(string), typeof(PopupInfoRow), string.Empty,
            propertyChanged: OnTextChanged);

    public static readonly BindableProperty CaptionProperty =
        BindableProperty.Create(nameof(Caption), typeof(string), typeof(PopupInfoRow), string.Empty,
            propertyChanged: OnTextChanged);

    public static readonly BindableProperty ValueColorProperty =
        BindableProperty.Create(nameof(ValueColor), typeof(Color), typeof(PopupInfoRow), Color.FromArgb("#1E1B4B"),
            propertyChanged: OnTextChanged);

    public static readonly BindableProperty ShowDividerProperty =
        BindableProperty.Create(nameof(ShowDivider), typeof(bool), typeof(PopupInfoRow), true);

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Prefix
    {
        get => (string)GetValue(PrefixProperty);
        set => SetValue(PrefixProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public Color ValueColor
    {
        get => (Color)GetValue(ValueColorProperty);
        set => SetValue(ValueColorProperty, value);
    }

    public bool ShowDivider
    {
        get => (bool)GetValue(ShowDividerProperty);
        set => SetValue(ShowDividerProperty, value);
    }

    public PopupInfoRow()
    {
        InitializeComponent();
        Loaded += (_, _) => RebuildText();
    }

    private static void OnTextChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is PopupInfoRow row)
            row.RebuildText();
    }

    private void RebuildText()
    {
        if (TextLabel is null)
            return;

        if (!string.IsNullOrEmpty(Caption))
        {
            TextLabel.FormattedText = null;
            TextLabel.Text = Caption;
            return;
        }

        var secondary = ResolveColor("TextSecondary", Color.FromArgb("#6B7280"));
        TextLabel.Text = null;
        TextLabel.FormattedText = new FormattedString
        {
            Spans =
            {
                new Span { Text = Prefix ?? string.Empty, TextColor = secondary },
                new Span { Text = Value ?? string.Empty, FontAttributes = FontAttributes.Bold, TextColor = ValueColor }
            }
        };
    }

    private static Color ResolveColor(string key, Color fallback)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true
            && value is Color color)
            return color;
        return fallback;
    }
}
