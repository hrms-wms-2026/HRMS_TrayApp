namespace ONEVO.Agent.TrayApp.Controls;

/// <summary>Simple polyline sparkline (with a soft area fill) over 0–1 normalized points.
/// No axes, labels, or interactivity — matches <see cref="FractionBar"/>'s minimal drawable pattern.</summary>
public sealed class SparklineChart : GraphicsView
{
    public static readonly BindableProperty PointsProperty =
        BindableProperty.Create(
            nameof(Points),
            typeof(IReadOnlyList<float>),
            typeof(SparklineChart),
            Array.Empty<float>(),
            propertyChanged: static (bindable, _, _) => ((SparklineChart)bindable).Invalidate());

    public static readonly BindableProperty LineColorProperty =
        BindableProperty.Create(
            nameof(LineColor),
            typeof(Color),
            typeof(SparklineChart),
            Color.FromArgb("#22C7F0"),
            propertyChanged: static (bindable, _, _) => ((SparklineChart)bindable).Invalidate());

    /// <summary>The 0–1 normalized values to plot. Assign a NEW list/array to update the chart —
    /// mutating an existing list in place will not trigger a redraw, since change detection is by
    /// property reassignment, not by observing the collection's contents.</summary>
    public IReadOnlyList<float> Points
    {
        get => (IReadOnlyList<float>)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public Color LineColor
    {
        get => (Color)GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
    }

    public SparklineChart()
    {
        Drawable = new Painter(this);
        InputTransparent = true;
    }

    private sealed class Painter(SparklineChart owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var points = owner.Points;
            if (points is null || points.Count < 2 || dirtyRect.Width < 2 || dirtyRect.Height < 2)
                return;

            var stepX = dirtyRect.Width / (points.Count - 1);
            var coords = new PointF[points.Count];
            for (var i = 0; i < points.Count; i++)
            {
                var fraction = float.IsFinite(points[i]) ? Math.Clamp(points[i], 0f, 1f) : 0f;
                var x = dirtyRect.X + stepX * i;
                var y = dirtyRect.Y + dirtyRect.Height * (1 - fraction);
                coords[i] = new PointF(x, y);
            }

            var fillPath = new PathF();
            fillPath.MoveTo(coords[0].X, dirtyRect.Bottom);
            foreach (var point in coords)
                fillPath.LineTo(point.X, point.Y);
            fillPath.LineTo(coords[^1].X, dirtyRect.Bottom);
            fillPath.Close();

            canvas.FillColor = owner.LineColor.WithAlpha(0.15f);
            canvas.FillPath(fillPath);

            var linePath = new PathF();
            linePath.MoveTo(coords[0].X, coords[0].Y);
            for (var i = 1; i < coords.Length; i++)
                linePath.LineTo(coords[i].X, coords[i].Y);

            canvas.StrokeColor = owner.LineColor;
            canvas.StrokeSize = 2f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;
            canvas.DrawPath(linePath);
        }
    }
}
