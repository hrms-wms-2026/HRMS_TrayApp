namespace ONEVO.Agent.TrayApp.Tests.Collectors;

using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Collectors;
using ONEVO.Agent.TrayApp.Tests.Fakes;

public class MeetingDetectorTests
{
    [Fact]
    public async Task StartAsync_SamplesImmediatelyOnStart_SubmitsRecordOverPipe()
    {
        var pipe = new FakeNamedPipeClient();

        await using var sut = new MeetingDetector(NullLogger<MeetingDetector>.Instance, pipe);
        await sut.StartAsync(policy: null!, CancellationToken.None);
        await Task.Delay(50);

        var submitted = Assert.Single(pipe.Submitted);
        var record = Assert.Single(submitted);
        Assert.Equal(CollectionRecordTypes.MeetingSignal, record.RecordType);
        Assert.Equal(CollectionSchemaVersions.MeetingSignalV1, record.SchemaVersion);

        await sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void IsMeetingAppRunning_NoKnownProcessRunning_ReturnsFalse()
    {
        Assert.Equal(MeetingDetector.IsMeetingAppRunning(), MeetingDetector.IsMeetingAppRunning());
    }
}
