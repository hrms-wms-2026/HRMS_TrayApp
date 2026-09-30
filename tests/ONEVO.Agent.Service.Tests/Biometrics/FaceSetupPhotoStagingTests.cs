namespace ONEVO.Agent.Service.Tests.Biometrics;

using ONEVO.Agent.Service.Biometrics;
using ONEVO.Agent.Shared.IPC;
using Xunit;

public sealed class FaceSetupPhotoStagingTests
{
    private static readonly byte[] Front = [1], Left = [2];

    [Fact]
    public void FrontStaged_ReturnsIt()
    {
        var staging = new FaceSetupPhotoStaging();
        var session = Guid.NewGuid();
        staging.Stage(session, FaceSetupPoses.Front, Front);

        Assert.Equal(Front, staging.TryGetComplete(session));
    }

    [Fact]
    public void MissingFront_ReturnsNull()
    {
        var staging = new FaceSetupPhotoStaging();
        var session = Guid.NewGuid();
        staging.Stage(session, FaceSetupPoses.Left, Left);

        Assert.Null(staging.TryGetComplete(session));
    }

    [Fact]
    public void NewSession_DiscardsPreviousAttemptsPhotos()
    {
        var staging = new FaceSetupPhotoStaging();
        var first = Guid.NewGuid();
        staging.Stage(first, FaceSetupPoses.Front, Front);

        var second = Guid.NewGuid();
        staging.Stage(second, FaceSetupPoses.Left, Left);

        Assert.Null(staging.TryGetComplete(first));
        Assert.Null(staging.TryGetComplete(second));
    }

    [Fact]
    public void DiscardedFront_MustBeRetaken()
    {
        var staging = new FaceSetupPhotoStaging();
        var session = Guid.NewGuid();
        staging.Stage(session, FaceSetupPoses.Front, Front);

        staging.Discard(session, FaceSetupPoses.Front);

        Assert.Null(staging.TryGetComplete(session));
    }

    [Fact]
    public void ExpiredSession_ReturnsNull()
    {
        var now = DateTimeOffset.UtcNow;
        var staging = new FaceSetupPhotoStaging(() => now);
        var session = Guid.NewGuid();
        staging.Stage(session, FaceSetupPoses.Front, Front);

        now += FaceSetupPhotoStaging.Lifetime + TimeSpan.FromSeconds(1);

        Assert.Null(staging.TryGetComplete(session));
    }
}
