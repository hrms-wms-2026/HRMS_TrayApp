namespace ONEVO.Agent.Service.Tests.Api;

using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Shared.Diagnostics;
using Xunit;

public class CorrelationIdHandlerTests
{
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public List<string?> Ids { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Ids.Add(request.Headers.TryGetValues(AgentCorrelation.HeaderName, out var v) ? v.Single() : null);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    private static (HttpClient Client, CaptureHandler Capture) Create()
    {
        var capture = new CaptureHandler();
        return (new HttpClient(new CorrelationIdHandler { InnerHandler = capture }), capture);
    }

    [Fact]
    public async Task SendsAmbientCorrelationId()
    {
        var (client, capture) = Create();
        using (AgentCorrelation.Begin("tray-action-1"))
            (await client.GetAsync("http://localhost/x")).Dispose();

        Assert.Equal("tray-action-1", Assert.Single(capture.Ids));
    }

    [Fact]
    public async Task WithoutAmbientId_SendsFreshIdPerRequest()
    {
        var (client, capture) = Create();
        (await client.GetAsync("http://localhost/x")).Dispose();
        (await client.GetAsync("http://localhost/x")).Dispose();

        Assert.All(capture.Ids, id => Assert.True(AgentCorrelation.IsValid(id)));
        Assert.NotEqual(capture.Ids[0], capture.Ids[1]);
    }

    [Fact]
    public async Task KeepsExplicitHeader()
    {
        var (client, capture) = Create();
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/x");
        request.Headers.Add(AgentCorrelation.HeaderName, "explicit");
        using (AgentCorrelation.Begin("ambient"))
            (await client.SendAsync(request)).Dispose();

        Assert.Equal("explicit", Assert.Single(capture.Ids));
    }
}
