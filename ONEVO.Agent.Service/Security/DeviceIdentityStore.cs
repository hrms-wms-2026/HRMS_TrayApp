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
        Directory.CreateDirectory(Path.GetDirectoryName(IdentityPath)!);
        var json = JsonSerializer.Serialize(identity);
        var tempPath = IdentityPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, IdentityPath, overwrite: true);
        Directory.CreateDirectory(Path.GetDirectoryName(_identityPath)!);
        File.WriteAllText(_identityPath, JsonSerializer.Serialize(identity));
    }

    public DeviceIdentity? Load()
    {
        try
        {
            if (!File.Exists(IdentityPath)) return null;
            using var stream = new FileStream(
                IdentityPath,
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
        if (!File.Exists(_identityPath)) return null;
        try { return JsonSerializer.Deserialize<DeviceIdentity>(File.ReadAllText(_identityPath)); }
        catch (JsonException) { return null; }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(IdentityPath)) File.Delete(IdentityPath);
        }
        catch (IOException)
        {
            // Another process still has the file; the next Save overwrites.
        }
        if (File.Exists(_identityPath)) File.Delete(_identityPath);
    }
}
