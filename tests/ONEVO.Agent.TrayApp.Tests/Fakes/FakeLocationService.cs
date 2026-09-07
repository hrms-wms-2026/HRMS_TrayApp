namespace ONEVO.Agent.TrayApp.Tests.Fakes;

using ONEVO.Agent.TrayApp.Services;

public sealed class FakeLocationService : ILocationService
{
    private readonly LocationCaptureResult _result;

    public FakeLocationService(LocationCaptureResult result)
    {
        _result = result;
    }

    public int CallCount { get; private set; }

    public Task<LocationCaptureResult> GetCurrentAsync(CancellationToken ct = default)
    {
        CallCount++;
        return Task.FromResult(_result);
    }
}
