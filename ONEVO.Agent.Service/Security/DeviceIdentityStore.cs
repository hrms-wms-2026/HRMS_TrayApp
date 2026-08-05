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
        File.WriteAllText(IdentityPath, JsonSerializer.Serialize(identity));
    }

    public DeviceIdentity? Load()
    {
        if (!File.Exists(IdentityPath)) return null;
        try { return JsonSerializer.Deserialize<DeviceIdentity>(File.ReadAllText(IdentityPath)); }
        catch (JsonException) { return null; }
    }

    public void Clear()
    {
        if (File.Exists(IdentityPath)) File.Delete(IdentityPath);
    }
}
