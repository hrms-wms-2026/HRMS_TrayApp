namespace ONEVO.Agent.Service.Security;

using System.Text.Json;
using ONEVO.Agent.Shared.Models;

public sealed class DeviceIdentityStore
{
    private static readonly string IdentityPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ONEVO", "Agent", "identity.json");

    public void Save(DeviceIdentity identity)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(IdentityPath)!);
        var json = JsonSerializer.Serialize(identity);
        var tempPath = IdentityPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, IdentityPath, overwrite: true);
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
    }
}
