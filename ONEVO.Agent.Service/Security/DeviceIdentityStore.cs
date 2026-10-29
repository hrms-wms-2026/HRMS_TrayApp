namespace ONEVO.Agent.Service.Security;

using System.Text.Json;
using ONEVO.Agent.Shared.Models;

public sealed class DeviceIdentityStore
{
    private readonly string _directory;
    private readonly string _identityPath;

    public DeviceIdentityStore() : this(SecureAgentStorage.DefaultDirectory)
    {
    }

    internal DeviceIdentityStore(string directory)
    {
        _directory = directory;
        _identityPath = Path.Combine(directory, "identity.json");
    }

    public void Save(DeviceIdentity identity)
    {
        SecureAgentStorage.EnsureDirectory(_directory);
        SecureAgentStorage.WriteAllBytesAtomic(_identityPath, JsonSerializer.SerializeToUtf8Bytes(identity));
    }

    public DeviceIdentity? Load()
    {
        if (!File.Exists(_identityPath)) return null;
        try { return JsonSerializer.Deserialize<DeviceIdentity>(File.ReadAllText(_identityPath)); }
        catch (JsonException) { return null; }
    }

    public void Clear()
    {
        if (File.Exists(_identityPath)) File.Delete(_identityPath);
    }
}
