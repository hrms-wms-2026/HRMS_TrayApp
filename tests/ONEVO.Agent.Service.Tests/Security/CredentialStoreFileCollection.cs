namespace ONEVO.Agent.Service.Tests.Security;

using Xunit;

/// <summary>
/// CredentialStore and DeviceIdentityStore read/write real files under a single shared
/// %ProgramData%\ONEVO\Agent\ location — not per-test isolated. xUnit runs different test
/// classes in parallel by default, so any two classes that both construct a real
/// CredentialStore() or DeviceIdentityStore() race on that shared folder (one test's
/// StoreDeviceJwt/ClearDeviceJwt/Save can lock identity.json or credential.dat mid-way
/// through another test's Load). Every test class that touches those stores must carry
/// [Collection(Name)] so xUnit serializes them against each other instead of running
/// them concurrently.
/// </summary>
[CollectionDefinition(Name)]
public class CredentialStoreFileCollection
{
    public const string Name = "CredentialStoreFile";
}
