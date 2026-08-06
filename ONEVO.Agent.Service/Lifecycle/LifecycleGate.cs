namespace ONEVO.Agent.Service.Lifecycle;

/// <summary>
/// All 9 conditions must be true before collection may enter Active (§6).
/// Missing or uncertain state → fails closed (collection remains stopped).
/// </summary>
public sealed class LifecycleGate
{
    private readonly Lock _lock = new();

    private bool _deviceEnrolled;
    private bool _credentialValid;
    private bool _deviceApproved;
    private bool _employeeSessionActive;
    private bool _consentValid;
    private bool _policyAllowsCollection;
    private bool _presenceSessionActive;
    private bool _notOnBreak;
    private bool _notOnApprovedTimeOff;

    public bool CanActivate
    {
        get
        {
            lock (_lock)
            {
                return _deviceEnrolled
                    && _credentialValid
                    && _deviceApproved
                    && _employeeSessionActive
                    && _consentValid
                    && _policyAllowsCollection
                    && _presenceSessionActive
                    && _notOnBreak
                    && _notOnApprovedTimeOff;
            }
        }
    }

    public void SetDeviceEnrolled(bool value)         { lock (_lock) _deviceEnrolled = value; }
    public void SetCredentialValid(bool value)        { lock (_lock) _credentialValid = value; }
    public void SetDeviceApproved(bool value)         { lock (_lock) _deviceApproved = value; }
    public void SetEmployeeSessionActive(bool value)  { lock (_lock) _employeeSessionActive = value; }
    public void SetConsentValid(bool value)           { lock (_lock) _consentValid = value; }
    public void SetPolicyAllowsCollection(bool value) { lock (_lock) _policyAllowsCollection = value; }
    public void SetPresenceSessionActive(bool value)  { lock (_lock) _presenceSessionActive = value; }
    public void SetNotOnBreak(bool value)             { lock (_lock) _notOnBreak = value; }
    public void SetNotOnApprovedTimeOff(bool value)   { lock (_lock) _notOnApprovedTimeOff = value; }

    public GateSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new GateSnapshot(
                _deviceEnrolled, _credentialValid, _deviceApproved,
                _employeeSessionActive, _consentValid, _policyAllowsCollection,
                _presenceSessionActive, _notOnBreak, _notOnApprovedTimeOff);
        }
    }
}

public sealed record GateSnapshot(
    bool DeviceEnrolled,
    bool CredentialValid,
    bool DeviceApproved,
    bool EmployeeSessionActive,
    bool ConsentValid,
    bool PolicyAllowsCollection,
    bool PresenceSessionActive,
    bool NotOnBreak,
    bool NotOnApprovedTimeOff)
{
    public bool CanActivate =>
        DeviceEnrolled && CredentialValid && DeviceApproved &&
        EmployeeSessionActive && ConsentValid && PolicyAllowsCollection &&
        PresenceSessionActive && NotOnBreak && NotOnApprovedTimeOff;
}
