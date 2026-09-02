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
        yield return ["ONEVO.Agent.TrayApp/Views/PrivacyTransparencyPage.xaml"];
        yield return ["ONEVO.Agent.TrayApp/Views/ConfirmDevicePage.xaml"];
        yield return ["ONEVO.Agent.TrayApp/Views/ClockInPage.xaml"];
        yield return ["ONEVO.Agent.TrayApp/Views/ActiveSessionPage.xaml"];
        yield return ["ONEVO.Agent.TrayApp/Views/EndSessionPage.xaml"];
        yield return ["ONEVO.Agent.TrayApp/Views/DailySummaryPage.xaml"];
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
    public void ConnectWorkspacePage_MatchesActivationMock()
    {
        var xaml = ReadSource("ONEVO.Agent.TrayApp/Views/ConnectWorkspacePage.xaml");
        Assert.Contains("OneXso Workspace", xaml, StringComparison.Ordinal);
        Assert.Contains("Open Activation Website", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Connect\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Shell.NavBarIsVisible=\"False\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Connect &amp; Login", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ONEVO Workspace", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AppHeaderBar", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void PrepareWorkspacePage_MatchesSettingUpAndFinalSetupMocks()
    {
        var xaml = ReadSource("ONEVO.Agent.TrayApp/Views/PrepareWorkspacePage.xaml");
        Assert.Contains("Setting up ", xaml, StringComparison.Ordinal);
        Assert.Contains("your workspace", xaml, StringComparison.Ordinal);
        Assert.Contains("We are securely preparing your account and registering this device.", xaml, StringComparison.Ordinal);
        Assert.Contains("Verifying activation", xaml, StringComparison.Ordinal);
        Assert.Contains("Fetching employee details", xaml, StringComparison.Ordinal);
        Assert.Contains("Registering this device", xaml, StringComparison.Ordinal);
        Assert.Contains("Preparing your workspace", xaml, StringComparison.Ordinal);
        Assert.Contains("Please wait while we finish your setup.", xaml, StringComparison.Ordinal);
        Assert.Contains("Final ", xaml, StringComparison.Ordinal);
        Assert.Contains("Workspace Setup", xaml, StringComparison.Ordinal);
        Assert.Contains("Applying your organisation policies and synchronising your workspace.", xaml, StringComparison.Ordinal);
        Assert.Contains("Applying organisation policies", xaml, StringComparison.Ordinal);
        Assert.Contains("Initialising monitoring agent", xaml, StringComparison.Ordinal);
        Assert.Contains("Syncing configuration", xaml, StringComparison.Ordinal);
        Assert.Contains("Validating device", xaml, StringComparison.Ordinal);
        Assert.Contains("Checking connectivity", xaml, StringComparison.Ordinal);
        Assert.Contains("Everything is ready.", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Continue\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SetupProgressRing", xaml, StringComparison.Ordinal);
        Assert.Contains("SetupStepRow", xaml, StringComparison.Ordinal);
        Assert.Contains("workspace_connect.png", xaml, StringComparison.Ordinal);
        Assert.Contains("onexso_x_mark.png", xaml, StringComparison.Ordinal);
        Assert.Contains("FooterStatusBar", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivityIndicator", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ClockInHeroAction_UsesCompactHeightResource()
    {
        var xaml = ReadSource("ONEVO.Agent.TrayApp/Views/ClockInPage.xaml");
        Assert.DoesNotContain("HeightRequest=\"92\"", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TrayHeroActionHeight", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ClockInPage_MatchesReadyToStartWorkMock()
    {
        var xaml = ReadSource("ONEVO.Agent.TrayApp/Views/ClockInPage.xaml");
        Assert.Contains("clockin_hero.png", xaml, StringComparison.Ordinal);
        Assert.Contains("Workspace Status", xaml, StringComparison.Ordinal);
        Assert.Contains("Policies", xaml, StringComparison.Ordinal);
        Assert.Contains("Profile", xaml, StringComparison.Ordinal);
        Assert.Contains("Devices", xaml, StringComparison.Ordinal);
        Assert.Contains("CLOCK IN", xaml, StringComparison.Ordinal);
        Assert.Contains("Not started", xaml, StringComparison.Ordinal);
        Assert.Contains("IconStopwatch", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveSessionPage_MatchesClockedInMock()
    {
        var xaml = ReadSource("ONEVO.Agent.TrayApp/Views/ActiveSessionPage.xaml");
        Assert.Contains("workspace_active.png", xaml, StringComparison.Ordinal);
        Assert.Contains("Open Dashboard", xaml, StringComparison.Ordinal);
        Assert.Contains("Start Break Later", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IconStopwatch", xaml, StringComparison.Ordinal);
        Assert.Contains("IconGlobe", xaml, StringComparison.Ordinal);
        Assert.Contains("HeaderAccent", xaml, StringComparison.Ordinal);
        Assert.Contains("IconInfo", xaml, StringComparison.Ordinal);
        Assert.Contains("End Break", xaml, StringComparison.Ordinal);
        Assert.Contains("WorkStartedCaption", xaml, StringComparison.Ordinal);
        Assert.Contains("BreakTotalCaption", xaml, StringComparison.Ordinal);
        Assert.Contains("ProductiveShareCaption", xaml, StringComparison.Ordinal);
        Assert.Contains("break_hero.png", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void EndSessionPage_MatchesWorkdayCompletedMock()
    {
        var xaml = ReadSource("ONEVO.Agent.TrayApp/Views/EndSessionPage.xaml");
        Assert.Contains("end_session_hero.png", xaml, StringComparison.Ordinal);
        Assert.Contains("View Dashboard", xaml, StringComparison.Ordinal);
        Assert.Contains("Download Summary", xaml, StringComparison.Ordinal);
        Assert.Contains("Close App", xaml, StringComparison.Ordinal);
        Assert.Contains("Synced to OneXso Cloud", xaml, StringComparison.Ordinal);
        Assert.Contains("IconCloud", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"2\"", xaml, StringComparison.Ordinal);
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
