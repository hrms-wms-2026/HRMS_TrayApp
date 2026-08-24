namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.Controls;
using ONEVO.Agent.TrayApp.ViewModels;

public partial class ClockInPage : ContentPage
{
    // Base and hover-brightened gradient/stroke colors for the CLOCK IN pill.
    private static readonly Color GradientBase0 = Color.FromArgb("#22C7F0");
    private static readonly Color GradientBase1 = Color.FromArgb("#175CFF");
    private static readonly Color GradientBase2 = Color.FromArgb("#6D28D9");
    private static readonly Color GradientBright0 = Lighten(GradientBase0, 0.16f);
    private static readonly Color GradientBright1 = Lighten(GradientBase1, 0.16f);
    private static readonly Color GradientBright2 = Lighten(GradientBase2, 0.16f);
    private static readonly Color StrokeRest = Color.FromArgb("#00FFFFFF");
    private static readonly Color StrokeHover = Color.FromArgb("#80FFFFFF");

    private const float ShadowRadiusRest = 22f;
    private const float ShadowRadiusHover = 34f;
    private const float ShadowOpacityRest = 0.55f;
    private const float ShadowOpacityHover = 0.85f;

    private CancellationTokenSource? _statusPulse;
    private CancellationTokenSource? _clockInSparkleLoop;
    private bool _clockInHovering;

    public ClockInPage(ClockInViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        ResponsiveTwoPane.Attach(this, PaneGrid, LeftPane, RightPane, narrowLeftMaxHeight: 300);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ClockInViewModel vm)
            vm.OnAppearing();

        _ = PageAnimations.EntranceAsync(LeftPane, RightPane);
        _statusPulse = PageAnimations.StartPulse(StatusDot, scaleTo: 1.3, duration: 850);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        PageAnimations.StopPulse(_statusPulse, StatusDot);
        _clockInSparkleLoop?.Cancel();
        _clockInSparkleLoop = null;
    }

    // ── CLOCK IN hover / press micro-interaction ────────────────────────────
    // Native MAUI only: PointerGestureRecognizer for hover, Button.Pressed/Released
    // for press feedback, ScaleToAsync/FadeToAsync/TranslateToAsync for motion, and a
    // custom Animation callback to lerp the gradient/stroke/shadow colors themselves
    // (there's no built-in ColorTo for GradientStop/Shadow, so this is the native way).

    private void OnClockInPointerEntered(object? sender, PointerEventArgs e)
    {
        if (_clockInHovering) return;
        _clockInHovering = true;

        _ = ClockInGlow.ScaleToAsync(1.03, 220, Easing.CubicOut);
        AnimateHoverIntensity(toHover: true, duration: 220);
        _ = RunIconPulseAsync();
        _ = RunShimmerAsync();
        StartSparkles();
    }

    private void OnClockInPointerExited(object? sender, PointerEventArgs e)
    {
        if (!_clockInHovering) return;
        _clockInHovering = false;

        _ = ClockInGlow.ScaleToAsync(1.0, 220, Easing.CubicOut);
        AnimateHoverIntensity(toHover: false, duration: 220);
        StopSparkles();
    }

    private void OnClockInPressed(object? sender, EventArgs e)
    {
        _ = ClockInGlow.ScaleToAsync(0.97, 90, Easing.CubicOut);
    }

    private void OnClockInReleased(object? sender, EventArgs e)
    {
        _ = ClockInGlow.ScaleToAsync(_clockInHovering ? 1.03 : 1.0, 140, Easing.CubicOut);
    }

    /// <summary>Lerps gradient stops, pill stroke and shadow together in one animation tick.</summary>
    private void AnimateHoverIntensity(bool toHover, uint duration)
    {
        var fromStop0 = ClockInStop0.Color;
        var fromStop1 = ClockInStop1.Color;
        var fromStop2 = ClockInStop2.Color;
        var fromStroke = ClockInStrokeBrush.Color;
        var fromRadius = ClockInShadow.Radius;
        var fromOpacity = ClockInShadow.Opacity;

        var toStop0 = toHover ? GradientBright0 : GradientBase0;
        var toStop1 = toHover ? GradientBright1 : GradientBase1;
        var toStop2 = toHover ? GradientBright2 : GradientBase2;
        var toStroke = toHover ? StrokeHover : StrokeRest;
        var toRadius = toHover ? ShadowRadiusHover : ShadowRadiusRest;
        var toOpacity = toHover ? ShadowOpacityHover : ShadowOpacityRest;

        var animation = new Animation(t =>
        {
            ClockInStop0.Color = Lerp(fromStop0, toStop0, t);
            ClockInStop1.Color = Lerp(fromStop1, toStop1, t);
            ClockInStop2.Color = Lerp(fromStop2, toStop2, t);
            ClockInStrokeBrush.Color = Lerp(fromStroke, toStroke, t);
            ClockInShadow.Radius = (float)(fromRadius + (toRadius - fromRadius) * t);
            ClockInShadow.Opacity = (float)(fromOpacity + (toOpacity - fromOpacity) * t);
        });
        animation.Commit(this, "ClockInHoverIntensity", 16, duration, Easing.CubicOut);
    }

    private async Task RunIconPulseAsync()
    {
        try
        {
            await ClockInIconCircle.ScaleToAsync(1.08, 160, Easing.CubicOut);
            await ClockInIconCircle.ScaleToAsync(1.0, 180, Easing.CubicOut);
        }
        catch (ObjectDisposedException) { /* page navigated away mid-animation */ }
    }

    private async Task RunShimmerAsync()
    {
        try
        {
            ClockInShimmer.TranslationX = -60;
            ClockInShimmer.Opacity = 1;
            await ClockInShimmer.TranslateToAsync(280, 0, 650, Easing.CubicOut);
            await ClockInShimmer.FadeToAsync(0, 120);
        }
        catch (ObjectDisposedException) { }
    }

    private void StartSparkles()
    {
        _clockInSparkleLoop?.Cancel();
        _clockInSparkleLoop = new CancellationTokenSource();
        var token = _clockInSparkleLoop.Token;
        _ = SparkleLoopAsync(ClockInSparkle1, 0, token);
        _ = SparkleLoopAsync(ClockInSparkle2, 250, token);
        _ = SparkleLoopAsync(ClockInSparkle3, 500, token);
    }

    private void StopSparkles()
    {
        _clockInSparkleLoop?.Cancel();
        _clockInSparkleLoop = null;
        ClockInSparkle1.Opacity = 0;
        ClockInSparkle2.Opacity = 0;
        ClockInSparkle3.Opacity = 0;
    }

    private static async Task SparkleLoopAsync(VisualElement sparkle, int delayMs, CancellationToken token)
    {
        try
        {
            if (delayMs > 0)
                await Task.Delay(delayMs, token);

            while (!token.IsCancellationRequested)
            {
                await Task.WhenAll(
                    sparkle.FadeToAsync(0.55, 500, Easing.SinInOut),
                    sparkle.TranslateToAsync(0, -4, 500, Easing.SinInOut));
                if (token.IsCancellationRequested) break;
                await Task.WhenAll(
                    sparkle.FadeToAsync(0, 500, Easing.SinInOut),
                    sparkle.TranslateToAsync(0, 0, 500, Easing.SinInOut));
            }
        }
        catch (TaskCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private static Color Lighten(Color c, float amount) =>
        Color.FromRgba(
            c.Red + (1 - c.Red) * amount,
            c.Green + (1 - c.Green) * amount,
            c.Blue + (1 - c.Blue) * amount,
            c.Alpha);

    private static Color Lerp(Color from, Color to, double t) =>
        Color.FromRgba(
            from.Red + (to.Red - from.Red) * t,
            from.Green + (to.Green - from.Green) * t,
            from.Blue + (to.Blue - from.Blue) * t,
            from.Alpha + (to.Alpha - from.Alpha) * t);
}
