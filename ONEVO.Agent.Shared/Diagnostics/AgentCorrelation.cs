namespace ONEVO.Agent.Shared.Diagnostics;

/// <summary>
/// One correlation id per user action, carried end to end: the Tray stamps it on the IPC
/// envelope (<c>cid</c>), the Service makes it the ambient id while it handles that envelope,
/// and every backend call made in that flow sends it as <see cref="HeaderName"/>. The backend's
/// CorrelationIdMiddleware already accepts and echoes that header (AUD-018).
/// </summary>
public static class AgentCorrelation
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ScopeKey = "CorrelationId";
    private const int MaxLength = 64;

    private static readonly AsyncLocal<string?> CurrentId = new();

    /// <summary>The id of the flow running on this async context, or null outside one.</summary>
    public static string? Current => CurrentId.Value;

    /// <summary>
    /// Makes <paramref name="correlationId"/> the ambient id until the result is disposed. An id
    /// that is not safe to put in an HTTP header or a log line is ignored, and the current one is kept.
    /// </summary>
    public static IDisposable Begin(string? correlationId)
    {
        var previous = CurrentId.Value;
        if (IsValid(correlationId))
            CurrentId.Value = correlationId;
        return new Restore(previous);
    }

    /// <summary>Log-scope state that puts <paramref name="correlationId"/> on every line in the scope.</summary>
    public static IReadOnlyList<KeyValuePair<string, object?>> Scope(string correlationId) =>
        [new(ScopeKey, correlationId)];

    public static string NewId() => Guid.NewGuid().ToString("N");

    public static bool IsValid(string? correlationId)
    {
        if (string.IsNullOrEmpty(correlationId) || correlationId.Length > MaxLength)
            return false;
        foreach (var c in correlationId)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                return false;
        }
        return true;
    }

    private sealed class Restore(string? previous) : IDisposable
    {
        public void Dispose() => CurrentId.Value = previous;
    }
}
