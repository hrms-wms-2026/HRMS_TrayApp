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
        HeightRequest = 140;
        WidthRequest = 140;
        HorizontalOptions = LayoutOptions.Center;
        VerticalOptions = LayoutOptions.Center;
        InputTransparent = true;
    }

    private sealed class DonutPainter(SplitDonutChart owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var size = Math.Min(dirtyRect.Width, dirtyRect.Height);
            if (size < 8)
                return;

            var stroke = size * 0.22f;
            var radius = (size - stroke) / 2f - 1f;
            var center = dirtyRect.Center;
            var primary = (float)Math.Clamp(owner.PrimaryFraction, 0, 1);
            var gap = primary is > 0.02f and < 0.98f ? 4f : 0f;
            var activeSweep = primary * 360f - gap;
            var idleSweep = (1f - primary) * 360f - gap;

            DonutGeometry.DrawSegment(canvas, center, radius, stroke, -90f, activeSweep, Color.FromArgb("#14B8A6"));
            DonutGeometry.DrawSegment(canvas, center, radius, stroke, -90f + activeSweep + gap, idleSweep, Color.FromArgb("#FDBA74"));
        }
    }
}

internal static class DonutGeometry
{
    public static void DrawSegment(
        ICanvas canvas,
        PointF center,
        float radius,
        float stroke,
        float startDeg,
        float sweepDeg,
        Color color)
    {
        if (sweepDeg <= 0.4f || radius <= 0)
            return;

        canvas.StrokeColor = color;
        canvas.StrokeSize = stroke;
        canvas.StrokeLineCap = LineCap.Butt;
        canvas.StrokeLineJoin = LineJoin.Round;

        var path = new PathF();
        var steps = Math.Max(16, (int)Math.Ceiling(Math.Abs(sweepDeg)));
        for (var i = 0; i <= steps; i++)
        {
            var deg = startDeg + sweepDeg * i / steps;
            var rad = deg * MathF.PI / 180f;
            var x = center.X + radius * MathF.Cos(rad);
            var y = center.Y + radius * MathF.Sin(rad);
            if (i == 0)
                path.MoveTo(x, y);
            else
                path.LineTo(x, y);
        }

        canvas.DrawPath(path);
    }
}
