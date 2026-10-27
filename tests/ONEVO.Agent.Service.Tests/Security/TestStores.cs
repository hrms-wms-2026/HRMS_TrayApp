namespace ONEVO.Agent.Service.Tests.Security;

using ONEVO.Agent.Service.Security;

/// <summary>
/// Credential stores for tests (AUD-017). Every test shares one per-run temp directory instead of
/// the real %ProgramData%\ONEVO\Agent\ files, so the existing "store in one instance, read or clear
/// in another" test patterns keep working while a run can no longer overwrite or delete a real
/// enrolled agent's credential.dat/refresh.dat. Test classes using it stay in
/// <see cref="CredentialStoreFileCollection"/> because they still share this one directory.
/// </summary>
internal static class TestStores
{
    public static readonly string SharedCredentialDirectory = CreateRunDirectory();

    public static CredentialStore NewCredentialStore() => new(SharedCredentialDirectory);

    private static string CreateRunDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"onevo-agent-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
        return directory;
    }
}
