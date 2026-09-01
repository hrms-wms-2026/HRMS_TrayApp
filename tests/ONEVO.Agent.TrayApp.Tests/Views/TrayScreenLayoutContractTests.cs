using ONEVO.Agent.TrayApp.Controls;

namespace ONEVO.Agent.TrayApp.Tests.Views;

public sealed class TrayScreenLayoutContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    public static IEnumerable<object[]> PrimaryScreenXamlFiles()
    {
        yield return ["ONEVO.Agent.TrayApp/Views/ConnectWorkspacePage.xaml"];
        yield return ["ONEVO.Agent.TrayApp/Views/PrepareWorkspacePage.xaml"];
        yield return ["ONEVO.Agent.TrayApp/Views/WorkLocationPage.xaml"];
        yield return ["ONEVO.Agent.TrayApp/Views/PhotoCaptureWindow.xaml"];
        yield return ["ONEVO.Agent.TrayApp/Views/BiometricEnrollmentPage.xaml"];
        yield return ["ONEVO.Agent.TrayApp/Views/ReviewSetupPage.xaml"];
        yield return ["ONEVO.Agent.TrayApp/Views/PrivacyConsentPage.xaml"];
        yield return ["ONEVO.Agent.TrayApp/Views/ClockInPage.xaml"];
        yield return ["ONEVO.Agent.TrayApp/Views/ActiveSessionPage.xaml"];
        yield return ["ONEVO.Agent.TrayApp/Views/EndSessionPage.xaml"];
    }

    [Fact]
    public void WindowMetrics_DefineSingleScreenDesktopCanvas()
    {
        Assert.Equal(1024, TrayLayoutMetrics.DefaultWindowWidth);
        Assert.Equal(720, TrayLayoutMetrics.DefaultWindowHeight);
        Assert.Equal(960, TrayLayoutMetrics.MinimumWindowWidth);
        Assert.Equal(700, TrayLayoutMetrics.MinimumWindowHeight);
        Assert.Equal(900, TrayLayoutMetrics.WideBreakpoint);
    }

    [Theory]
    [MemberData(nameof(PrimaryScreenXamlFiles))]
    public void PrimaryScreens_DoNotUsePageLevelScrollView(string relativePath)
    {
        var xaml = ReadSource(relativePath);
        Assert.DoesNotContain("<ScrollView", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ONEVO.Agent.TrayApp/Views/EndSessionPage.xaml")]
    public void FiniteTrayLists_DoNotUseCollectionView(string relativePath)
    {
        var xaml = ReadSource(relativePath);
        Assert.DoesNotContain("<CollectionView", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClockInHeroAction_UsesCompactHeightResource()
    {
        var xaml = ReadSource("ONEVO.Agent.TrayApp/Views/ClockInPage.xaml");
        Assert.DoesNotContain("HeightRequest=\"92\"", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TrayHeroActionHeight", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void PhotoCaptureFrame_UsesCompactSizeResource()
    {
        var xaml = ReadSource("ONEVO.Agent.TrayApp/Views/PhotoCaptureWindow.xaml");
        Assert.DoesNotContain("WidthRequest=\"260\"", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TrayFaceFrameOuterSize", xaml, StringComparison.Ordinal);
    }

    private static string ReadSource(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ONEVO.Agent.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate ONEVO.Agent.slnx above the test output directory.");
    }
}
