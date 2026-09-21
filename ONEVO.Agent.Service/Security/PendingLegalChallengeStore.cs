namespace ONEVO.Agent.Service.Security;

/// <summary>
/// Holds the short-lived legal-acceptance challenge/CSRF pair minted by the backend's most recent
/// enroll/refresh response, in memory only — the challenge is a bearer-equivalent credential for
/// accepting legal documents as this user for its ~10 minute lifetime, so it never touches disk or
/// crosses the named pipe to the tray UI. Set by whichever of AgentWorker (enroll, silent resume)
/// or TokenRefreshService (periodic background refresh) most recently obtained one; cleared once
/// the employee successfully accepts.
/// </summary>
public sealed class PendingLegalChallengeStore
{
    private readonly object _lock = new();
    private (string Challenge, string CsrfToken)? _current;

    public void Set(string? challenge, string? csrfToken)
    {
        lock (_lock)
        {
            _current = challenge is not null && csrfToken is not null
                ? (challenge, csrfToken)
                : null;
        }
    }

    public (string Challenge, string CsrfToken)? Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }
}
