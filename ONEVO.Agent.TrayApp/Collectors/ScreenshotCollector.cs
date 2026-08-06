namespace ONEVO.Agent.TrayApp.Collectors;

using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Screenshot collection — off unless effective policy enables it (§7.5).
/// Never runs during break, stopped, or uncertain lifecycle state.
/// </summary>
public sealed class ScreenshotCollector : IAgentCollector, IAsyncDisposable
{
    public string Name => "Screenshot";

    private readonly ILogger<ScreenshotCollector> _logger;
    private readonly INamedPipeClient _pipe;
    private readonly string _deviceId;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _running;

    public ScreenshotCollector(ILogger<ScreenshotCollector> logger, INamedPipeClient pipe)
    {
        _logger   = logger;
        _pipe     = pipe;
        _deviceId = Environment.MachineName;
    }

    public Task StartAsync(AgentPolicy policy, CancellationToken ct)
    {
        if (!policy.ScreenshotEnabled)
        {
            _logger.LogDebug("{Name}: policy disabled — not starting", Name);
            return Task.CompletedTask;
        }

        if (_running) return Task.CompletedTask;

        _cts     = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop    = CaptureLoopAsync(_cts.Token);
        _running = true;
        _logger.LogInformation("{Name}: started (policy-enabled)", Name);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (!_running) return;
        _running = false;
        if (_cts is not null) { await _cts.CancelAsync(); _cts.Dispose(); _cts = null; }
        if (_loop is not null) { try { await _loop.WaitAsync(TimeSpan.FromSeconds(3), ct); } catch { } _loop = null; }
        _logger.LogInformation("{Name}: stopped", Name);
    }

    private async Task CaptureLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(300));
            while (await timer.WaitForNextTickAsync(ct))
                await CaptureScreenAsync(ct);
        }
        catch (OperationCanceledException) { }
    }

    private async Task CaptureScreenAsync(CancellationToken ct)
    {
        try
        {
            var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
                ?? new Rectangle(0, 0, 1920, 1080);

            using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using var g   = Graphics.FromImage(bmp);
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);

            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Jpeg);
            var dataBase64 = Convert.ToBase64String(ms.ToArray());

            var payload = new { format = "jpeg", data = dataBase64 };
            var record  = new ONEVO.Agent.Shared.Models.CollectionRecord
            {
                EventId         = Guid.NewGuid().ToString("N"),
                RecordType      = CollectionRecordTypes.Screenshot,
                SchemaVersion   = CollectionSchemaVersions.ScreenshotV1,
                CaptureTimestamp = DateTimeOffset.UtcNow,
                DeviceId        = _deviceId,
                Payload         = JsonSerializer.SerializeToElement(payload)
            };

            await _pipe.SubmitCollectionRecordsAsync([record], ct);
            _logger.LogDebug("{Name}: captured and submitted ({Bytes} bytes)", Name, ms.Length);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Name}: capture failed", Name);
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);
}
