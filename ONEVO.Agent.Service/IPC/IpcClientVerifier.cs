namespace ONEVO.Agent.Service.IPC;

using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Options;
using ONEVO.Agent.Service.Configuration;

/// <summary>The process on the other end of a pipe connection, as far as Windows reports it.</summary>
public sealed record IpcClientProcess(int ProcessId, string? ExecutablePath, int? SessionId);

/// <summary>Looks up the process behind a connected pipe. Swappable so the checks are unit-testable.</summary>
public interface IClientProcessInspector
{
    IpcClientProcess? Inspect(NamedPipeServerStream pipe);

    /// <summary>Session of the user at the physical console; null when no session is attached.</summary>
    int? ActiveConsoleSessionId();
}

/// <summary>Decides whether a freshly connected pipe client is the installed Tray App.</summary>
public interface IIpcClientVerifier
{
    bool IsTrusted(NamedPipeServerStream pipe);
}

/// <summary>
/// The pipe ACL lets any signed-in user connect, and the nonce exchange proves nothing (the
/// client just echoes it), so this is the real check. A client is trusted only when it is
/// <see cref="TrayExecutableName"/> running from an install directory a standard user cannot
/// write to (the MSIX WindowsApps folder or Program Files\ONEVO), inside the active console
/// session. That stops another user on a shared PC, RDP server or fast-user-switched session —
/// and any script — from clocking in, submitting records, or reading biometric credentials.
/// </summary>
public sealed class IpcClientVerifier : IIpcClientVerifier
{
    public const string TrayExecutableName = "ONEVO.Agent.TrayApp.exe";

    private readonly ILogger<IpcClientVerifier> _logger;
    private readonly IClientProcessInspector _inspector;
    private readonly bool _allowAnyPath;
    private readonly IReadOnlyList<string> _trustedRoots;

    public IpcClientVerifier(
        ILogger<IpcClientVerifier> logger,
        IClientProcessInspector inspector,
        IOptions<AgentOptions> options)
    {
        _logger = logger;
        _inspector = inspector;
        _allowAnyPath = options.Value.AllowIpcClientFromAnyPath;
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        _trustedRoots =
        [
            Path.Combine(programFiles, "WindowsApps"),
            Path.Combine(programFiles, "ONEVO")
        ];
        if (_allowAnyPath)
            _logger.LogWarning("AllowIpcClientFromAnyPath=true — Tray App install path not checked (development)");
    }

    public bool IsTrusted(NamedPipeServerStream pipe)
    {
        IpcClientProcess? client;
        int? consoleSessionId;
        try
        {
            client = _inspector.Inspect(pipe);
            consoleSessionId = _inspector.ActiveConsoleSessionId();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IPC client rejected: could not inspect the client process");
            return false;
        }

        var rejection = Check(client, consoleSessionId, _trustedRoots, _allowAnyPath);
        if (rejection is null)
            return true;

        _logger.LogWarning(
            "IPC client rejected ({Reason}): pid={Pid} path={Path} session={Session} consoleSession={ConsoleSession}",
            rejection, client?.ProcessId, client?.ExecutablePath, client?.SessionId, consoleSessionId);
        return false;
    }

    /// <summary>Returns null when the client is trusted, otherwise the reason it is not.</summary>
    internal static string? Check(
        IpcClientProcess? client, int? consoleSessionId, IReadOnlyList<string> trustedRoots, bool allowAnyPath)
    {
        if (client is null)
            return "client process unknown";
        if (string.IsNullOrWhiteSpace(client.ExecutablePath))
            return "client executable unknown";

        string fullPath;
        try { fullPath = Path.GetFullPath(client.ExecutablePath); }
        catch (Exception) { return "client executable path invalid"; }

        if (!string.Equals(Path.GetFileName(fullPath), TrayExecutableName, StringComparison.OrdinalIgnoreCase))
            return "not the Tray App executable";

        if (!allowAnyPath && !trustedRoots.Any(root => IsUnder(fullPath, root)))
            return "Tray App not running from an install directory";

        if (consoleSessionId is null)
            return "no active console session";
        if (client.SessionId != consoleSessionId)
            return "client is not in the active console session";

        return null;
    }

    private static bool IsUnder(string fullPath, string root)
    {
        // Trailing separator so "Program Files\ONEVOEvil" does not match "Program Files\ONEVO".
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class WindowsClientProcessInspector : IClientProcessInspector
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint NoConsoleSession = 0xFFFFFFFF;

    public IpcClientProcess? Inspect(NamedPipeServerStream pipe)
    {
        if (!NativeMethods.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetNamedPipeClientProcessId failed");

        int? sessionId = NativeMethods.ProcessIdToSessionId(pid, out var session) ? (int)session : null;
        return new IpcClientProcess((int)pid, QueryImagePath(pid), sessionId);
    }

    public int? ActiveConsoleSessionId()
    {
        var session = NativeMethods.WTSGetActiveConsoleSessionId();
        return session == NoConsoleSession ? null : (int)session;
    }

    private static string? QueryImagePath(uint pid)
    {
        var process = NativeMethods.OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process == IntPtr.Zero)
            return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref size)
                ? buffer.ToString(0, size)
                : null;
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetNamedPipeClientProcessId(SafeHandle pipe, out uint clientProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

        [DllImport("kernel32.dll")]
        public static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder exeName, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
