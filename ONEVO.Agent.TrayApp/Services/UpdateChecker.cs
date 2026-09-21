namespace ONEVO.Agent.TrayApp.Services;

using ONEVO.Agent.Shared.IPC;

public sealed class UpdateChecker : IUpdateChecker
{
    private readonly INamedPipeClient _pipe;
    private readonly Func<string> _currentVersion;

    /// <summary>Production ctor: reads the packaged version via MAUI AppInfo (x.y.z.n on packaged Windows builds).</summary>
    public UpdateChecker(INamedPipeClient pipe) : this(pipe, () => AppInfo.Current.VersionString) { }

    public UpdateChecker(INamedPipeClient pipe, Func<string> currentVersion)
    {
        _pipe = pipe;
        _currentVersion = currentVersion;
    }

    public async Task<UpdateCheckResultPayload?> CheckAsync(CancellationToken ct)
    {
        var result = await _pipe.SendUpdateCheckAsync(ToThreePartVersion(_currentVersion()), ct);
        return result is { Success: true, UpdateAvailable: true } ? result : null;
    }

    /// <summary>The registry compares strict x.y.z; packaged builds report x.y.z.n, so drop the revision.</summary>
    internal static string ToThreePartVersion(string version)
    {
        var parts = version.Split('.');
        return parts.Length > 3 ? string.Join('.', parts.Take(3)) : version;
    }
}
