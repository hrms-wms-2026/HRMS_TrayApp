namespace ONEVO.Agent.TrayApp.Controls;

/// <summary>Thin rounded track that fills to <see cref="Fraction"/> (0–1).</summary>
public sealed class FractionBar : GraphicsView
{
    public static readonly BindableProperty FractionProperty =
        BindableProperty.Create(
            nameof(Fraction),
            typeof(double),
            typeof(FractionBar),
            0d,
            propertyChanged: static (bindable, _, _) => ((FractionBar)bindable).Invalidate());

    public static readonly BindableProperty FillColorProperty =
        BindableProperty.Create(
            nameof(FillColor),
            typeof(Color),
            typeof(FractionBar),
            Color.FromArgb("#22C7F0"),
            propertyChanged: static (bindable, _, _) => ((FractionBar)bindable).Invalidate());

    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public Color FillColor
    {
        get => (Color)GetValue(FillColorProperty);
        set => SetValue(FillColorProperty, value);
    }

    public FractionBar()
    {
        Drawable = new Painter(this);
        HeightRequest = 8;
        HorizontalOptions = LayoutOptions.Fill;
        InputTransparent = true;
    }

    private sealed class Painter(FractionBar owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            const float barHeight = 6f;
            var h = Math.Min(dirtyRect.Height, barHeight);
            if (dirtyRect.Width < 2 || h < 2)
                return;

            var y = dirtyRect.Y + (dirtyRect.Height - h) / 2f;
            var radius = h / 2f;
            canvas.FillColor = Color.FromArgb("#E8EDF5");
            canvas.FillRoundedRectangle(dirtyRect.X, y, dirtyRect.Width, h, radius);

            var fraction = (float)Math.Clamp(owner.Fraction, 0, 1);
            var w = dirtyRect.Width * fraction;
            if (w < 1f)
                return;

            if (w < h)
                w = Math.Min(h, dirtyRect.Width);

            canvas.FillColor = owner.FillColor;
            canvas.FillRoundedRectangle(dirtyRect.X, y, w, h, radius);
        }
    }
}
