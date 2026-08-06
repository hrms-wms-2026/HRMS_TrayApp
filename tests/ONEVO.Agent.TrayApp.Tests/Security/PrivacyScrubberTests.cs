namespace ONEVO.Agent.TrayApp.Tests.Security;

public sealed class PrivacyScrubberTests
{
    [Theory]
    [InlineData("code",       "code.exe")]
    [InlineData("code.exe",   "code.exe")]
    [InlineData("Code.EXE",   "code.exe")]
    [InlineData("msedge.exe", "msedge.exe")]
    public void SanitizeProcessName_NormalizesToLowerExe(string input, string expected)
    {
        Assert.Equal(expected, PrivacyScrubber.SanitizeProcessName(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SanitizeProcessName_NullOrWhitespace_ReturnsNull(string? input)
    {
        Assert.Null(PrivacyScrubber.SanitizeProcessName(input));
    }

    [Theory]
    [InlineData("visual studio")]
    [InlineData("bad name!.exe")]
    [InlineData("$special.exe")]
    public void SanitizeProcessName_InvalidChars_ReturnsNull(string input)
    {
        Assert.Null(PrivacyScrubber.SanitizeProcessName(input));
    }

    [Theory]
    [InlineData("C:\\Windows\\System32\\cmd.exe")]
    [InlineData("/usr/bin/bash")]
    [InlineData("../evil.exe")]
    [InlineData("sub:stream.exe")]
    public void SanitizeProcessName_PathSeparatorsOrColon_ReturnsNull(string input)
    {
        Assert.Null(PrivacyScrubber.SanitizeProcessName(input));
    }

    [Fact]
    public void SanitizeProcessName_LongName_TruncatedTo100Chars()
    {
        var longBase = new string('a', 98) + ".exe";
        var result = PrivacyScrubber.SanitizeProcessName(longBase);
        Assert.NotNull(result);
        Assert.True(result!.Length <= 100);
        Assert.EndsWith(".exe", result);
    }

    [Fact]
    public void GetSecondsSinceLastInput_ReturnsNonNegative()
    {
        var result = PrivacyScrubber.GetSecondsSinceLastInput();
        Assert.True(result >= 0);
    }

    [Fact]
    public void GetForegroundProcessNameSafe_ReturnsNullOrValidSafeName()
    {
        var result = PrivacyScrubber.GetForegroundProcessNameSafe();
        if (result is null) return; // acceptable in headless CI

        Assert.EndsWith(".exe", result, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", result);
        Assert.DoesNotContain("/", result);
        Assert.DoesNotContain(":", result);
        Assert.True(result.Length <= 100);
        Assert.Matches(@"^[a-z0-9][a-z0-9._-]{0,98}\.exe$", result);
    }
}
