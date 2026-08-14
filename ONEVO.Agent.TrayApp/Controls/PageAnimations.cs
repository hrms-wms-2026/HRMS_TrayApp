namespace ONEVO.Agent.TrayApp.Controls;

/// <summary>
/// Small, reusable entrance/attention animations shared by the attendance-flow pages.
/// Loops (pulse) are cooperative — call <see cref="StopPulse"/> from OnDisappearing so
/// they don't keep animating (and holding a strong ref to the element) after navigation.
/// </summary>
public static class PageAnimations
{
    public static async Task EntranceAsync(View? left, View? right)
    {
        if (left is not null)
        {
            left.Opacity = 0;
            left.TranslationY = 16;
        }
        if (right is not null)
        {
            right.Opacity = 0;
            right.TranslationY = 16;
        }

        var tasks = new List<Task>();
        if (left is not null)
            tasks.Add(Task.WhenAll(
                left.FadeToAsync(1, 320, Easing.CubicOut),
                left.TranslateToAsync(0, 0, 320, Easing.CubicOut)));
        if (right is not null)
        {
            await Task.Delay(60);
            tasks.Add(Task.WhenAll(
                right.FadeToAsync(1, 320, Easing.CubicOut),
                right.TranslateToAsync(0, 0, 320, Easing.CubicOut)));
        }
        await Task.WhenAll(tasks);
    }

    /// <summary>Gentle infinite breathing pulse — for a live status dot or the CLOCK IN glow.</summary>
    public static CancellationTokenSource StartPulse(VisualElement element, double scaleTo = 1.18, uint duration = 900)
    {
        var cts = new CancellationTokenSource();
        _ = PulseLoopAsync(element, scaleTo, duration, cts.Token);
        return cts;
    }

    public static void StopPulse(CancellationTokenSource? cts, VisualElement? element)
    {
        cts?.Cancel();
        if (element is not null)
        {
            element.AbortAnimation("PulseScale");
            element.AbortAnimation("PulseOpacity");
            element.Scale = 1;
            element.Opacity = 1;
        }
    }

    private static async Task PulseLoopAsync(VisualElement element, double scaleTo, uint duration, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.WhenAll(
                    element.ScaleToAsync(scaleTo, duration, Easing.SinInOut),
                    element.FadeToAsync(0.55, duration, Easing.SinInOut));
                if (token.IsCancellationRequested) break;
                await Task.WhenAll(
                    element.ScaleToAsync(1, duration, Easing.SinInOut),
                    element.FadeToAsync(1, duration, Easing.SinInOut));
            }
        }
        catch (ObjectDisposedException)
        {
            // Page navigated away mid-animation — nothing to clean up.
        }
    }

    /// <summary>One-shot celebratory pop — for the Workday Completed badge.</summary>
    public static async Task PopAsync(VisualElement element)
    {
        element.Scale = 0.5;
        element.Opacity = 0;
        await Task.WhenAll(
            element.ScaleToAsync(1.12, 260, Easing.CubicOut),
            element.FadeToAsync(1, 200, Easing.CubicOut));
        await element.ScaleToAsync(1, 140, Easing.SpringOut);
    }
}
