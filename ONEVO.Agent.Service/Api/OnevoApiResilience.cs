namespace ONEVO.Agent.Service.Api;

using System.Net;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Timeout;

/// <summary>
/// The resilience pipeline of the "OnevoApi" HttpClient (packages-guide §9, Polly v8): retry with
/// jittered exponential backoff, a circuit breaker, and a per-attempt timeout (innermost).
/// </summary>
public static class OnevoApiResilience
{
    public static TimeSpan AttemptTimeout(int httpTimeoutSeconds) =>
        TimeSpan.FromSeconds(Math.Clamp(httpTimeoutSeconds, 5, 120));

    public static void Configure(ResiliencePipelineBuilder<HttpResponseMessage> builder, TimeSpan attemptTimeout)
    {
        builder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 4,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(30),
            UseJitter = true,
            // Replaces the strategy's default predicate, so every transient case is listed here.
            // TimeoutRejectedException is the per-attempt timeout below; leaving it out (AUD-023)
            // failed the whole call on the first slow attempt instead of retrying.
            ShouldHandle = args =>
            {
                if (args.Outcome.Exception is HttpRequestException or TaskCanceledException or TimeoutRejectedException)
                    return PredicateResult.True();
                var code = args.Outcome.Result?.StatusCode;
                if (code is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
                    return PredicateResult.True();
                if (code is >= HttpStatusCode.InternalServerError)
                    return PredicateResult.True();
                return PredicateResult.False();
            },
            // Honor Retry-After on 429 when present; else exponential default.
            DelayGenerator = args =>
            {
                if (args.Outcome.Result?.StatusCode == HttpStatusCode.TooManyRequests
                    && args.Outcome.Result.Headers.RetryAfter?.Delta is { } ra
                    && ra > TimeSpan.Zero)
                    return new ValueTask<TimeSpan?>(ra);
                return new ValueTask<TimeSpan?>((TimeSpan?)null);
            }
        });

        builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            SamplingDuration = TimeSpan.FromMinutes(2),
            MinimumThroughput = 5,
            FailureRatio = 0.5,
            BreakDuration = TimeSpan.FromMinutes(1)
        });

        builder.AddTimeout(attemptTimeout);
    }
}
