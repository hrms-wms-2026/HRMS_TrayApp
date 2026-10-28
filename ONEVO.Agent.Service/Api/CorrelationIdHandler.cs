namespace ONEVO.Agent.Service.Api;

using ONEVO.Agent.Shared.Diagnostics;

/// <summary>
/// Sends <see cref="AgentCorrelation.HeaderName"/> on every OnevoApi request: the id of the Tray
/// action being handled, or a fresh one for background work (sync, heartbeat), so a backend log
/// line can always be matched to the agent's. Sits outside the resilience pipeline so all
/// retries of one call share the same id.
/// </summary>
public sealed class CorrelationIdHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(AgentCorrelation.HeaderName))
            request.Headers.TryAddWithoutValidation(
                AgentCorrelation.HeaderName, AgentCorrelation.Current ?? AgentCorrelation.NewId());
        return base.SendAsync(request, cancellationToken);
    }
}
