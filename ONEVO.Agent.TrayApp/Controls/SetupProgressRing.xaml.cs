namespace ONEVO.Agent.TrayApp.Controls;

public partial class SetupProgressRing : ContentView
{
    public static readonly BindableProperty ProgressProperty =
        BindableProperty.Create(nameof(Progress), typeof(double), typeof(SetupProgressRing), 0d,
            propertyChanged: OnVisualChanged);

    public static readonly BindableProperty SizeProperty =
        BindableProperty.Create(nameof(Size), typeof(double), typeof(SetupProgressRing), 160d,
            propertyChanged: OnVisualChanged);

    public static readonly BindableProperty StrokeThicknessProperty =
        BindableProperty.Create(nameof(StrokeThickness), typeof(double), typeof(SetupProgressRing), 12d,
            propertyChanged: OnVisualChanged);

    public static readonly BindableProperty ShowCheckProperty =
        BindableProperty.Create(nameof(ShowCheck), typeof(bool), typeof(SetupProgressRing), false);

    public static readonly BindableProperty ShowPercentLabelProperty =
        BindableProperty.Create(nameof(ShowPercentLabel), typeof(bool), typeof(SetupProgressRing), false);

    public static readonly BindableProperty ShowCompletedCaptionProperty =
        BindableProperty.Create(nameof(ShowCompletedCaption), typeof(bool), typeof(SetupProgressRing), false);

    private readonly SetupProgressDrawable _drawable = new();

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public bool ShowCheck
    {
        get => (bool)GetValue(ShowCheckProperty);
        set => SetValue(ShowCheckProperty, value);
    }

    public bool ShowPercentLabel
    {
        get => (bool)GetValue(ShowPercentLabelProperty);
        set => SetValue(ShowPercentLabelProperty, value);
    }

    public bool ShowCompletedCaption
    {
        get => (bool)GetValue(ShowCompletedCaptionProperty);
        set => SetValue(ShowCompletedCaptionProperty, value);
    }

    public SetupProgressRing()
    {
        InitializeComponent();
        Canvas.Drawable = _drawable;
        ApplyVisual();
    }

    private static void OnVisualChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is SetupProgressRing ring)
            ring.ApplyVisual();
    }

    private void ApplyVisual()
    {
        WidthRequest = Size;
        HeightRequest = Size;
        _drawable.Progress = Progress;
        _drawable.StrokeWidth = (float)StrokeThickness;
        _drawable.TrackColor = Token("Separator", "#E4E9F2");
        _drawable.StartColor = Token("PrimaryGradientStart", "#22C7F0");
        _drawable.MidColor = Token("PrimaryGradientMid", "#175CFF");
        _drawable.EndColor = Token("PrimaryGradientEnd", "#6D28D9");
        Canvas.Invalidate();
    }

    private static Color Token(string key, string fallback)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color)
            return color;
        return Color.FromArgb(fallback);
    }
}

internal sealed class SetupProgressDrawable : IDrawable
{
    public double Progress { get; set; }
    public float StrokeWidth { get; set; } = 12f;
    public Color TrackColor { get; set; } = Color.FromArgb("#E4E9F2");
    public Color StartColor { get; set; } = Color.FromArgb("#22C7F0");
    public Color MidColor { get; set; } = Color.FromArgb("#175CFF");
    public Color EndColor { get; set; } = Color.FromArgb("#6D28D9");

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
            return;

        var pad = StrokeWidth / 2f + 1f;
        var rect = new RectF(pad, pad, dirtyRect.Width - pad * 2, dirtyRect.Height - pad * 2);
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        canvas.Antialias = true;
        canvas.StrokeSize = StrokeWidth;
        canvas.StrokeLineCap = LineCap.Round;

        canvas.StrokeColor = TrackColor;
        canvas.DrawEllipse(rect);

        var sweep = 360f * (float)Math.Clamp(Progress / 100.0, 0, 1);
        if (sweep <= 0.5f)
            return;

        const int maxSegments = 72;
        var segments = Math.Max(1, (int)Math.Ceiling(sweep / (360f / maxSegments)));
        var step = sweep / segments;
        var angle = -90f;

        for (var i = 0; i < segments; i++)
        {
            var t = segments == 1 ? 1f : (float)i / (segments - 1);
            canvas.StrokeColor = Lerp3(StartColor, MidColor, EndColor, t);
            var next = angle + step;
            canvas.DrawArc(rect, angle, next + 0.4f, true, false);
            angle = next;
        }
    }

    internal static Color Lerp3(Color a, Color b, Color c, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t < 0.5f ? Lerp(a, b, t * 2f) : Lerp(b, c, (t - 0.5f) * 2f);
    }

    internal static Color Lerp(Color a, Color b, float t)
        => new(
            a.Red + ((b.Red - a.Red) * t),
            a.Green + ((b.Green - a.Green) * t),
            a.Blue + ((b.Blue - a.Blue) * t),
            a.Alpha + ((b.Alpha - a.Alpha) * t));
}
