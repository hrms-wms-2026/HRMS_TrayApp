using ONEVO.Agent.TrayApp.Controls;

namespace ONEVO.Agent.TrayApp.Tests.Controls;

public sealed class SetupProgressDrawableTests
{
    [Fact]
    public void Lerp3_StartsAtFirstColor()
    {
        var a = Color.FromRgb(255, 0, 0);
        var b = Color.FromRgb(0, 255, 0);
        var c = Color.FromRgb(0, 0, 255);
        var result = SetupProgressDrawable.Lerp3(a, b, c, 0f);
        Assert.Equal(a.Red, result.Red, 3);
        Assert.Equal(a.Green, result.Green, 3);
        Assert.Equal(a.Blue, result.Blue, 3);
    }

    [Fact]
    public void Lerp3_EndsAtLastColor()
    {
        var a = Color.FromRgb(255, 0, 0);
        var b = Color.FromRgb(0, 255, 0);
        var c = Color.FromRgb(0, 0, 255);
        var result = SetupProgressDrawable.Lerp3(a, b, c, 1f);
        Assert.Equal(c.Red, result.Red, 3);
        Assert.Equal(c.Green, result.Green, 3);
        Assert.Equal(c.Blue, result.Blue, 3);
    }

    [Fact]
    public void Lerp3_MidpointIsSecondColor()
    {
        var a = Color.FromRgb(255, 0, 0);
        var b = Color.FromRgb(0, 255, 0);
        var c = Color.FromRgb(0, 0, 255);
        var result = SetupProgressDrawable.Lerp3(a, b, c, 0.5f);
        Assert.Equal(b.Red, result.Red, 3);
        Assert.Equal(b.Green, result.Green, 3);
        Assert.Equal(b.Blue, result.Blue, 3);
    }
}
