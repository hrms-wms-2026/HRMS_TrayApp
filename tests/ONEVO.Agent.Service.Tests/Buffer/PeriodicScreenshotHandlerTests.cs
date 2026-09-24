namespace ONEVO.Agent.Service.Tests.Buffer;

using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Service.Policy;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.Service.Tests.Sync;
using Xunit;

public sealed class PeriodicScreenshotHandlerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "onevo-shot-" + Guid.NewGuid().ToString("N"));
    private readonly ActivityRecordBuffer _buffer = ActivityRecordBuffer.CreateInMemory();
    private readonly EvidenceSpoolStore _spool;
    private readonly AgentStateMachine _state = new();
    private readonly PolicyCache _policy = new();
    private readonly PeriodicScreenshotHandler _handler;

    public PeriodicScreenshotHandlerTests()
    {
        Directory.CreateDirectory(_dir);
        _spool = new EvidenceSpoolStore(_dir);
        _state.TryTransition(MonitoringState.Stopped, out _);
        _state.TryTransition(MonitoringState.Active, out _);
        _policy.Set(new AgentPolicy
        {
            Version = "v1",
            ActivitySignalEnabled = true,
            AppUsageEnabled = false,
            ScreenshotEnabled = true,
            CameraVerificationEnabled = false,
            ValidUntil = DateTimeOffset.UtcNow.AddHours(1)
        });
        _handler = new PeriodicScreenshotHandler(
            _buffer,
            _spool,
            new PassthroughEvidenceProtector(),
            _state,
            _policy,
            new DeviceIdentityStore(Path.Combine(_dir, "identity.json")),
            NullLogger<PeriodicScreenshotHandler>.Instance);
    }

    [Fact]
    public void Complete_QueuesTheJpegForUpload()
    {
        var id = Guid.NewGuid();
        var capturedAt = DateTimeOffset.Parse("2026-09-22T04:15:00Z");
        var jpeg = new byte[] { 1, 2, 3, 4 };

        var start = _handler.HandleStart(
            new PeriodicScreenshotStartPayload(id, capturedAt, jpeg.Length, 1),
            DateTimeOffset.UtcNow);
        var chunk = _handler.HandleChunk(
            new PeriodicScreenshotChunkPayload(id, 0, Convert.ToBase64String(jpeg)),
            DateTimeOffset.UtcNow);
        var complete = _handler.HandleComplete(id, DateTimeOffset.UtcNow);

        Assert.True(start.Accepted);
        Assert.True(chunk.Accepted);
        Assert.True(complete.Accepted);
        Assert.Equal(1, _buffer.Count);
        Assert.NotNull(_buffer.GetEvidenceSpoolEntry(id.ToString("N"))?.EncryptedPath);
    }

    public void Dispose()
    {
        _buffer.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp */ }
    }
}
