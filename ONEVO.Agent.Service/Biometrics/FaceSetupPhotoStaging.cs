namespace ONEVO.Agent.Service.Biometrics;

using ONEVO.Agent.Shared.IPC;

/// <summary>
/// Holds the tray face setup photos that already passed their step check until the tray commits
/// all three (they do not fit in one IPC message). Only one setup attempt is kept: a new session
/// id discards the previous one, so a commit can never mix photos from two attempts. Nothing is
/// written to disk — a Service restart simply means "retake".
/// </summary>
public sealed class FaceSetupPhotoStaging
{
    public static TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(15);

    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _now;
    private Guid _sessionId;
    private DateTimeOffset _startedAt;
    private readonly Dictionary<string, byte[]> _photos = new(StringComparer.OrdinalIgnoreCase);

    public FaceSetupPhotoStaging(Func<DateTimeOffset>? now = null) =>
        _now = now ?? (() => DateTimeOffset.UtcNow);

    public void Stage(Guid sessionId, string pose, byte[] jpeg)
    {
        lock (_gate)
        {
            EnsureSession(sessionId);
            _photos[pose] = jpeg;
        }
    }

    /// <summary>A step failed its check: drop any earlier photo for that step.</summary>
    public void Discard(Guid sessionId, string pose)
    {
        lock (_gate)
        {
            if (sessionId == _sessionId)
                _photos.Remove(pose);
        }
    }

    /// <summary>The three photos of this session, or null when any is missing or the session expired.</summary>
    public (byte[] Front, byte[] Left, byte[] Right)? TryGetComplete(Guid sessionId)
    {
        lock (_gate)
        {
            if (sessionId != _sessionId || _now() - _startedAt > Lifetime)
                return null;

            return _photos.TryGetValue(FaceSetupPoses.Front, out var front)
                && _photos.TryGetValue(FaceSetupPoses.Left, out var left)
                && _photos.TryGetValue(FaceSetupPoses.Right, out var right)
                    ? (front, left, right)
                    : null;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _photos.Clear();
            _sessionId = Guid.Empty;
        }
    }

    private void EnsureSession(Guid sessionId)
    {
        if (sessionId == _sessionId && _now() - _startedAt <= Lifetime)
            return;

        _photos.Clear();
        _sessionId = sessionId;
        _startedAt = _now();
    }
}
