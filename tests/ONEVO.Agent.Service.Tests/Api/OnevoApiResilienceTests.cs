using System.Net;
using ONEVO.Agent.Service.Api;
using Polly;
using Polly.Timeout;
using Xunit;

namespace ONEVO.Agent.Service.Tests.Api;

/// <summary>
/// AUD-023: the custom retry predicate replaced the resilience defaults and dropped
/// <see cref="TimeoutRejectedException"/>, so an attempt that hit the 30 s attempt timeout failed the
/// call instead of being retried, and 408 Request Timeout was treated as final. These tests run the
/// real pipeline from <see cref="OnevoApiResilience.Configure"/> with a short attempt timeout.
/// </summary>
public sealed class OnevoApiResilienceTests
{
    private static ResiliencePipeline<HttpResponseMessage> Pipeline() =>
        new ResiliencePipelineBuilder<HttpResponseMessage>()
            .Apply(builder => OnevoApiResilience.Configure(builder, TimeSpan.FromMilliseconds(200)))
            .Build();

    private static async Task<(HttpResponseMessage Response, int Calls)> RunAsync(
        Func<int, CancellationToken, Task<HttpResponseMessage>> attempt, CancellationToken ct = default)
    {
        var calls = 0;
        var response = await Pipeline().ExecuteAsync(async token => await attempt(++calls, token), ct);
        return (response, calls);
    }

    [Fact]
    public async Task AttemptThatTimesOut_IsRetried()
    {
        var (response, calls) = await RunAsync(async (call, token) =>
        {
            if (call == 1)
                await Task.Delay(TimeSpan.FromSeconds(10), token); // exceeds the 200 ms attempt timeout
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task TransientStatus_IsRetried(HttpStatusCode transient)
    {
        var (response, calls) = await RunAsync((call, _) =>
            Task.FromResult(new HttpResponseMessage(call == 1 ? transient : HttpStatusCode.OK)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task ClientError_IsNotRetried(HttpStatusCode final)
    {
        var (response, calls) = await RunAsync((_, _) => Task.FromResult(new HttpResponseMessage(final)));

        Assert.Equal(final, response.StatusCode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task NetworkFailure_IsRetried()
    {
        var (response, calls) = await RunAsync((call, _) => call == 1
            ? throw new HttpRequestException("connection reset")
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task CallerCancellation_IsNotRetried()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Pipeline().ExecuteAsync<HttpResponseMessage>(
            async token =>
            {
                calls++;
                await cts.CancelAsync();
                token.ThrowIfCancellationRequested();
                return new HttpResponseMessage(HttpStatusCode.OK);
            },
            cts.Token).AsTask());

        Assert.Equal(1, calls);
    }
}

internal static class ResiliencePipelineBuilderExtensions
{
    public static ResiliencePipelineBuilder<T> Apply<T>(this ResiliencePipelineBuilder<T> builder, Action<ResiliencePipelineBuilder<T>> configure)
    {
        configure(builder);
        return builder;
    }
}
