namespace ONEVO.Agent.TrayApp.Tests.Fakes;

using ONEVO.Agent.TrayApp.Services;

public sealed class FakeLocationService : ILocationService
{
    private readonly LocationCaptureResult? _result;
    private readonly Exception? _throw;

    public FakeLocationService(LocationCaptureResult result)
    {
        _result = result;
    }

    private FakeLocationService(Exception toThrow)
    {
        _throw = toThrow;
    }

    /// <summary>A location service that throws instead of returning a failure result.</summary>
    public static FakeLocationService Throwing(Exception? toThrow = null) =>
        new(toThrow ?? new InvalidOperationException("location subsystem exploded"));

    public int CallCount { get; private set; }

    public Task<LocationCaptureResult> GetCurrentAsync(CancellationToken ct = default)
    {
        CallCount++;
        if (_throw is not null)
            throw _throw;
        return Task.FromResult(_result!);
    }
}
