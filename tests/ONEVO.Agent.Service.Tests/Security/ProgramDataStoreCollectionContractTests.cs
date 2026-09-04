namespace ONEVO.Agent.Service.Tests.Security;

using Xunit;

/// <summary>
/// Guards the race that failed CI on identity.json: any test class that constructs a real
/// DeviceIdentityStore or CredentialStore must sit in CredentialStoreFileCollection so
/// xUnit does not run it in parallel with other ProgramData writers.
/// </summary>
public sealed class ProgramDataStoreCollectionContractTests
{
    [Fact]
    public void TestClassesThatTouchProgramDataStores_AreInTheSharedCollection()
    {
        var testsRoot = FindTestsRoot();
        var offenders = new List<string>();

        foreach (var path in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            var source = File.ReadAllText(path);
            var touchesStore =
                source.Contains("new DeviceIdentityStore(", StringComparison.Ordinal)
                || source.Contains("new CredentialStore(", StringComparison.Ordinal);
            if (!touchesStore)
                continue;

            if (!source.Contains($"[Collection({nameof(CredentialStoreFileCollection)}.Name)]", StringComparison.Ordinal)
                && !source.Contains($"[Collection(CredentialStoreFileCollection.Name)]", StringComparison.Ordinal))
                offenders.Add(Path.GetRelativePath(testsRoot, path));
        }

        Assert.True(
            offenders.Count == 0,
            "These test files construct CredentialStore/DeviceIdentityStore but are not in "
            + "CredentialStoreFileCollection (parallel runs lock %ProgramData%\\ONEVO\\Agent\\identity.json):\n"
            + string.Join(Environment.NewLine, offenders));
    }

    private static string FindTestsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ONEVO.Agent.Service.Tests.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate ONEVO.Agent.Service.Tests.csproj.");
    }
}
