namespace ONEVO.Agent.TrayApp.Controls;

public readonly record struct DonutSegment(string ColorHex, float Fraction);

public sealed class MultiSegmentDonutChart : GraphicsView
{
    public static readonly BindableProperty SegmentsProperty =
        BindableProperty.Create(
            nameof(Segments),
            typeof(IEnumerable<DonutSegment>),
            typeof(MultiSegmentDonutChart),
            default(IEnumerable<DonutSegment>),
            propertyChanged: static (bindable, _, _) => ((MultiSegmentDonutChart)bindable).Invalidate());

    public IEnumerable<DonutSegment>? Segments
    {
        get => (IEnumerable<DonutSegment>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public MultiSegmentDonutChart()
    {
        Drawable = new Painter(this);
        HeightRequest = 120;
        WidthRequest = 120;
        HorizontalOptions = LayoutOptions.Center;
        VerticalOptions = LayoutOptions.Center;
        InputTransparent = true;
    }

    private sealed class Painter(MultiSegmentDonutChart owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var size = Math.Min(dirtyRect.Width, dirtyRect.Height);
            if (size < 8)
                return;

            var stroke = size * 0.22f;
            var radius = (size - stroke) / 2f - 1f;
            var center = dirtyRect.Center;
            var items = owner.Segments?.Where(s => s.Fraction > 0.001f).ToArray() ?? [];
            if (items.Length == 0)
            {
                DonutGeometry.DrawSegment(canvas, center, radius, stroke, -90f, 360f, Color.FromArgb("#E5E7EB"));
                return;
            }

            var total = items.Sum(s => s.Fraction);
            if (total <= 0)
            {
                DonutGeometry.DrawSegment(canvas, center, radius, stroke, -90f, 360f, Color.FromArgb("#E5E7EB"));
                return;
            }

            var cursor = -90f;
            var gap = items.Length > 1 ? 3f : 0f;
            foreach (var item in items)
            {
                var sweep = (float)(item.Fraction / total * 360.0) - gap;
                var color = Color.FromArgb(string.IsNullOrWhiteSpace(item.ColorHex) ? "#6366F1" : item.ColorHex);
                DonutGeometry.DrawSegment(canvas, center, radius, stroke, cursor, sweep, color);
                cursor += sweep + gap;
            }
        }
    }
}