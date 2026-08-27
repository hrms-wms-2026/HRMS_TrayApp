namespace ONEVO.Agent.Service.Buffer;

using System.Security.AccessControl;
using System.Security.Principal;

/// <summary>
/// Stores DPAPI-protected evidence files under %ProgramData%\ONEVO\Agent\EvidenceSpool.
/// Quota: 256 MB. Retention: 72 hours.
/// </summary>
public sealed class EvidenceSpoolStore
{
    public const long MaxQuotaBytes = 256L * 1024 * 1024;
    public static readonly TimeSpan Retention = TimeSpan.FromHours(72);

    private readonly string _directory;
    private readonly object _gate = new();

    public EvidenceSpoolStore(string? directory = null)
    {
        _directory = directory ?? GetDefaultDirectory();
        Directory.CreateDirectory(_directory);
        TryApplyRestrictedAcl(_directory);
    }

    public static string GetDefaultDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ONEVO", "Agent", "EvidenceSpool");

    public string DirectoryPath => _directory;

    public long TotalBytes
    {
        get
        {
            lock (_gate)
            {
                if (!Directory.Exists(_directory)) return 0;
                return Directory.EnumerateFiles(_directory, "*.evidence")
                    .Select(f => new FileInfo(f).Length)
                    .Sum();
            }
        }
    }

    public bool HasCapacityFor(int byteCount) => TotalBytes + byteCount <= MaxQuotaBytes;

    public string Write(Guid attemptId, ReadOnlyMemory<byte> protectedBytes)
    {
        lock (_gate)
        {
            if (!HasCapacityFor(protectedBytes.Length))
                throw new InvalidOperationException("evidence_spool_quota_exceeded");

            var fileName = $"{attemptId:N}_{Guid.NewGuid():N}.evidence";
            var path = Path.Combine(_directory, fileName);
            File.WriteAllBytes(path, protectedBytes.ToArray());
            TryApplyRestrictedAcl(path);
            return path;
        }
    }

    public byte[] Read(string path, Guid attemptId)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Evidence file missing.", path);

        return File.ReadAllBytes(path);
    }

    public void Delete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup after backend ack.
        }
    }

    public void PurgeExpired(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!Directory.Exists(_directory)) return;
            var cutoff = now - Retention;
            foreach (var file in Directory.EnumerateFiles(_directory, "*.evidence"))
            {
                try
                {
                    var created = File.GetCreationTimeUtc(file);
                    if (created < cutoff.UtcDateTime)
                        File.Delete(file);
                }
                catch
                {
                    // Best-effort purge.
                }
            }
        }
    }

    private static void TryApplyRestrictedAcl(string path)
    {
        try
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!path.StartsWith(programData, StringComparison.OrdinalIgnoreCase))
                return;

            var isDir = Directory.Exists(path);
            FileSystemSecurity security = isDir
                ? new DirectoryInfo(path).GetAccessControl()
                : new FileInfo(path).GetAccessControl();

            var inheritance = isDir ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None;

            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                inheritance,
                PropagationFlags.None,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                inheritance,
                PropagationFlags.None,
                AccessControlType.Allow));

            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser is not null
                && currentUser.Value != new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value)
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    currentUser,
                    FileSystemRights.FullControl,
                    inheritance,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            if (isDir)
                new DirectoryInfo(path).SetAccessControl((DirectorySecurity)security);
            else
                new FileInfo(path).SetAccessControl((FileSecurity)security);
        }
        catch
        {
            // ACL hardening is best-effort; spool still works without it.
        }
    }
}
