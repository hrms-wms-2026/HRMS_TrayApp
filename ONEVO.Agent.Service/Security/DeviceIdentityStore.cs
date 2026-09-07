namespace ONEVO.Agent.Service.Security;

using System.Text.Json;
using ONEVO.Agent.Shared.Models;

public sealed class DeviceIdentityStore
{
    private static readonly string DefaultIdentityDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ONEVO", "Agent");

    private readonly string _identityPath;

    /// <summary>Production callers use the parameterless constructor (real ProgramData path).
    /// Tests pass <paramref name="identityDirectoryOverride"/> (e.g. a per-test temp directory)
    /// so parallel test runs don't share and race on the same real file.</summary>
    public DeviceIdentityStore(string? identityDirectoryOverride = null)
    {
        _identityPath = Path.Combine(identityDirectoryOverride ?? DefaultIdentityDirectory, "identity.json");
    }

    public void Save(DeviceIdentity identity)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_identityPath)!);
        var json = JsonSerializer.Serialize(identity);
        var tempPath = _identityPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _identityPath, overwrite: true);
    }

    public DeviceIdentity? Load()
    {
        try
        {
            if (!File.Exists(_identityPath)) return null;
            using var stream = new FileStream(
                _identityPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<DeviceIdentity>(stream);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_identityPath)) File.Delete(_identityPath);
        }
        catch (IOException)
        {
            // Another process still has the file; the next Save overwrites.
        }
    }
}
