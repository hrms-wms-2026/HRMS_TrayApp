namespace ONEVO.Agent.TrayApp.Controls;

public sealed class SplitDonutChart : GraphicsView
{
    public static readonly BindableProperty PrimaryFractionProperty =
        BindableProperty.Create(
            nameof(PrimaryFraction),
            typeof(double),
            typeof(SplitDonutChart),
            1d,
            propertyChanged: static (bindable, _, _) => ((SplitDonutChart)bindable).Invalidate());

    public double PrimaryFraction
    {
        get => (double)GetValue(PrimaryFractionProperty);
        set => SetValue(PrimaryFractionProperty, value);
    }

    public SplitDonutChart()
    {
        Drawable = new DonutPainter(this);
        HeightRequest = 128;
        WidthRequest = 128;
        HorizontalOptions = LayoutOptions.Center;
        VerticalOptions = LayoutOptions.Center;
    }

    private sealed class DonutPainter(SplitDonutChart owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var size = Math.Min(dirtyRect.Width, dirtyRect.Height);
            var stroke = size * 0.16f;
            var pad = stroke / 2f + 2f;
            var rect = new RectF(
                dirtyRect.Center.X - size / 2f + pad,
                dirtyRect.Center.Y - size / 2f + pad,
                size - pad * 2f,
                size - pad * 2f);

            var primary = (float)Math.Clamp(owner.PrimaryFraction, 0, 1);

            canvas.StrokeSize = stroke;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeColor = Color.FromArgb("#FDBA74");
            canvas.DrawArc(rect.X, rect.Y, rect.Width, rect.Height, 0, 360, false, false);

            if (primary <= 0.001f)
                return;

            canvas.StrokeColor = Color.FromArgb("#14B8A6");
            canvas.DrawArc(rect.X, rect.Y, rect.Width, rect.Height, -90, -90 + primary * 360f, false, false);
        }
    }
}
