namespace ONEVO.Agent.TrayApp.Controls;

/// <summary>One coloured stretch of the day, as fractions (0–1) of the timeline's range.</summary>
public readonly record struct TimelineSegment(double Start, double End, string ColorHex);

/// <summary>A tick label under the bar at <see cref="Position"/> (0–1).</summary>
public readonly record struct TimelineTick(double Position, string Label);

/// <summary>
/// Day timeline for Daily Summary: a rounded grey track with active/break/idle stretches drawn
/// over it, and hour labels underneath.
/// </summary>
public sealed class WorkTimelineBar : GraphicsView
{
    private const float BarHeight = 18f;
    private const float LabelGap = 8f;
    private const float LabelSize = 11f;

    public static readonly BindableProperty SegmentsProperty =
        BindableProperty.Create(
            nameof(Segments),
            typeof(IEnumerable<TimelineSegment>),
            typeof(WorkTimelineBar),
            default(IEnumerable<TimelineSegment>),
            propertyChanged: static (bindable, _, _) => ((WorkTimelineBar)bindable).Invalidate());

    public static readonly BindableProperty TicksProperty =
        BindableProperty.Create(
            nameof(Ticks),
            typeof(IEnumerable<TimelineTick>),
            typeof(WorkTimelineBar),
            default(IEnumerable<TimelineTick>),
            propertyChanged: static (bindable, _, _) => ((WorkTimelineBar)bindable).Invalidate());

    public IEnumerable<TimelineSegment>? Segments
    {
        get => (IEnumerable<TimelineSegment>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public IEnumerable<TimelineTick>? Ticks
    {
        get => (IEnumerable<TimelineTick>?)GetValue(TicksProperty);
        set => SetValue(TicksProperty, value);
    }

    public WorkTimelineBar()
    {
        Drawable = new Painter(this);
        HeightRequest = BarHeight + LabelGap + LabelSize + 4;
        HorizontalOptions = LayoutOptions.Fill;
        InputTransparent = true;
    }

    private sealed class Painter(WorkTimelineBar owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            // Inset so the first/last tick label is not clipped at the edges.
            const float inset = 16f;
            var left = dirtyRect.Left + inset;
            var width = dirtyRect.Width - 2 * inset;
            if (width < 10)
                return;

            var track = new RectF(left, dirtyRect.Top, width, BarHeight);
            canvas.FillColor = Color.FromArgb("#E5E7EB");
            canvas.FillRoundedRectangle(track, BarHeight / 2);

            var segments = owner.Segments?.Where(s => s.End > s.Start).OrderBy(s => s.Start).ToArray() ?? [];
            if (segments.Length > 0)
            {
                canvas.SaveState();
                var clip = new PathF();
                clip.AppendRoundedRectangle(track, BarHeight / 2);
                canvas.ClipPath(clip);
                foreach (var segment in segments)
                {
                    var x0 = left + (float)(Math.Clamp(segment.Start, 0, 1) * width);
                    var x1 = left + (float)(Math.Clamp(segment.End, 0, 1) * width);
                    // Keep very short breaks visible as a sliver.
                    if (x1 - x0 < 2f)
                        x1 = x0 + 2f;
                    canvas.FillColor = Color.FromArgb(segment.ColorHex);
                    canvas.FillRectangle(x0, track.Top, x1 - x0, BarHeight);
                }
                canvas.RestoreState();
            }

            var ticks = owner.Ticks?.ToArray() ?? [];
            if (ticks.Length == 0)
                return;

            canvas.FontColor = Color.FromArgb("#64748B");
            canvas.FontSize = LabelSize;
            var labelTop = track.Bottom + LabelGap;
            foreach (var tick in ticks)
            {
                var x = left + (float)(Math.Clamp(tick.Position, 0, 1) * width);
                canvas.DrawString(tick.Label, x - 30, labelTop, 60, LabelSize + 4,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
            }
        }
    }
}
