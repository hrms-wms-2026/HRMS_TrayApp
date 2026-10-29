namespace ONEVO.Agent.Service.Security;

public interface ICredentialStore
{
    void StoreDeviceJwt(string jwt);
    string? ReadDeviceJwt();
    void ClearDeviceJwt();
}
