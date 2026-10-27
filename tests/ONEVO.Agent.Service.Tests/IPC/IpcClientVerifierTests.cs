using ONEVO.Agent.Service.IPC;
using Xunit;

namespace ONEVO.Agent.Service.Tests.IPC;

public class IpcClientVerifierTests
{
    private const int ConsoleSession = 1;
    private const string InstalledTray =
        @"C:\Program Files\WindowsApps\ONEVO.Agent_1.4.0.0_x64__abc123\ONEVO.Agent.TrayApp.exe";

    private static readonly string[] Roots = [@"C:\Program Files\WindowsApps", @"C:\Program Files\ONEVO"];

    private static string? Check(string? path, int? session = ConsoleSession, int? consoleSession = ConsoleSession,
        bool allowAnyPath = false) =>
        IpcClientVerifier.Check(new IpcClientProcess(4242, path, session), consoleSession, Roots, allowAnyPath);

    [Theory]
    [InlineData(InstalledTray)]
    [InlineData(@"C:\Program Files\ONEVO\TrayApp\ONEVO.Agent.TrayApp.exe")]
    [InlineData(@"c:\program files\onevo\trayapp\onevo.agent.trayapp.exe")]
    public void InstalledTray_InConsoleSession_IsTrusted(string path) =>
        Assert.Null(Check(path));

    [Theory]
    [InlineData(@"C:\Users\bob\AppData\Local\Temp\ONEVO.Agent.TrayApp.exe")]
    [InlineData(@"C:\Program Files\ONEVOEvil\ONEVO.Agent.TrayApp.exe")]
    [InlineData(@"C:\Program Files\ONEVO\..\..\Users\bob\ONEVO.Agent.TrayApp.exe")]
    [InlineData(@"C:\src\HRMS_TrayApp\ONEVO.Agent.TrayApp\bin\Debug\ONEVO.Agent.TrayApp.exe")]
    public void TrayOutsideInstallDirectory_IsRejected(string path) =>
        Assert.Equal("Tray App not running from an install directory", Check(path));

    [Theory]
    [InlineData(@"C:\Program Files\PowerShell\7\pwsh.exe")]
    [InlineData(@"C:\Program Files\ONEVO\TrayApp\pipeprobe.exe")]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe")]
    public void OtherExecutable_IsRejected_EvenFromAnInstallDirectory(string path) =>
        Assert.Equal("not the Tray App executable", Check(path));

    [Fact]
    public void TrayInAnotherUsersSession_IsRejected() =>
        Assert.Equal("client is not in the active console session", Check(InstalledTray, session: 3));

    [Fact]
    public void NoActiveConsoleSession_IsRejected() =>
        Assert.Equal("no active console session", Check(InstalledTray, consoleSession: null));

    [Fact]
    public void UnknownSession_IsRejected() =>
        Assert.Equal("client is not in the active console session", Check(InstalledTray, session: null));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void UnknownExecutable_IsRejected(string? path) =>
        Assert.Equal("client executable unknown", Check(path));

    [Fact]
    public void UnknownProcess_IsRejected() =>
        Assert.Equal("client process unknown", IpcClientVerifier.Check(null, ConsoleSession, Roots, false));

    [Fact]
    public void AllowAnyPath_AcceptsDevBuild_ButStillChecksNameAndSession()
    {
        const string devBuild = @"C:\src\HRMS_TrayApp\ONEVO.Agent.TrayApp\bin\Debug\ONEVO.Agent.TrayApp.exe";

        Assert.Null(Check(devBuild, allowAnyPath: true));
        Assert.Equal("not the Tray App executable", Check(@"C:\src\pipeprobe.exe", allowAnyPath: true));
        Assert.Equal("client is not in the active console session", Check(devBuild, session: 3, allowAnyPath: true));
    }
}
