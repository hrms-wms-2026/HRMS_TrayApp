namespace ONEVO.Agent.Service.Tests.Security;

using Xunit;

/// <summary>
/// CredentialStore reads/writes real DPAPI-protected files under a single shared
/// %ProgramData%\ONEVO\Agent\ location — not per-test isolated. xUnit runs different test
/// classes in parallel by default, so any two classes that both construct a real
/// CredentialStore() race on that shared file (one test's StoreDeviceJwt/ClearDeviceJwt can
/// flip state mid-way through another test's "no JWT on disk" assumption). Every test class
/// that touches a real CredentialStore must carry [Collection(Name)] so xUnit serializes them
/// against each other instead of running them concurrently.
/// </summary>
[CollectionDefinition(Name)]
public class CredentialStoreFileCollection
{
    public const string Name = "CredentialStoreFile";
}
