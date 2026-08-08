namespace ONEVO.Agent.Service.Security;

using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

/// <summary>
/// Deterministic per-machine fingerprint sent on exchange and compared on every
/// refresh (backend revokes all tokens for the device on mismatch). Must resolve
/// to the same value every time — derive it once from a stable machine identifier,
/// never from anything that can change between calls (session, hostname, IP).
/// </summary>
public static class DeviceFingerprint
{
    public static string Compute()
    {
        var machineGuid = ReadMachineGuid() ?? Environment.MachineName;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"onevo-agent:{machineGuid}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? ReadMachineGuid()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }
        catch
        {
            return null;
        }
    }
}
