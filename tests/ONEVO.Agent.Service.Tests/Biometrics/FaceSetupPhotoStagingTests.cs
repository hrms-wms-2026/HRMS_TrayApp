namespace ONEVO.Agent.Service.Tests.Biometrics;

using ONEVO.Agent.Service.Biometrics;
using ONEVO.Agent.Shared.IPC;
using Xunit;

public sealed class FaceSetupPhotoStagingTests
{
    private static readonly byte[] Front = [1], Left = [2], Right = [3];

    [Fact]
    public void AllThreeStaged_ReturnsThem()
    {
        var staging = new FaceSetupPhotoStaging();
        var session = Guid.NewGuid();
        staging.Stage(session, FaceSetupPoses.Front, Front);
        staging.Stage(session, FaceSetupPoses.Left, Left);
        staging.Stage(session, FaceSetupPoses.Right, Right);

        var photos = staging.TryGetComplete(session);

        Assert.NotNull(photos);
        Assert.Equal(Front, photos.Value.Front);
        Assert.Equal(Left, photos.Value.Left);
        Assert.Equal(Right, photos.Value.Right);
    }

    [Fact]
    public void MissingPhoto_ReturnsNull()
    {
        var staging = new FaceSetupPhotoStaging();
        var session = Guid.NewGuid();
        staging.Stage(session, FaceSetupPoses.Front, Front);
        staging.Stage(session, FaceSetupPoses.Left, Left);

        Assert.Null(staging.TryGetComplete(session));
    }

    [Fact]
    public void NewSession_DiscardsPreviousAttemptsPhotos()
    {
        var staging = new FaceSetupPhotoStaging();
        var first = Guid.NewGuid();
        staging.Stage(first, FaceSetupPoses.Front, Front);
        staging.Stage(first, FaceSetupPoses.Left, Left);

        var second = Guid.NewGuid();
        staging.Stage(second, FaceSetupPoses.Right, Right);

        Assert.Null(staging.TryGetComplete(first));
        Assert.Null(staging.TryGetComplete(second));
    }

    [Fact]
    public void DiscardedPose_MustBeRetaken()
    {
        var staging = new FaceSetupPhotoStaging();
        var session = Guid.NewGuid();
        staging.Stage(session, FaceSetupPoses.Front, Front);
        staging.Stage(session, FaceSetupPoses.Left, Left);
        staging.Stage(session, FaceSetupPoses.Right, Right);

        staging.Discard(session, FaceSetupPoses.Left);

        Assert.Null(staging.TryGetComplete(session));
    }

    [Fact]
    public void ExpiredSession_ReturnsNull()
    {
        var now = DateTimeOffset.UtcNow;
        var staging = new FaceSetupPhotoStaging(() => now);
        var session = Guid.NewGuid();
        staging.Stage(session, FaceSetupPoses.Front, Front);
        staging.Stage(session, FaceSetupPoses.Left, Left);
        staging.Stage(session, FaceSetupPoses.Right, Right);

        now += FaceSetupPhotoStaging.Lifetime + TimeSpan.FromSeconds(1);

        Assert.Null(staging.TryGetComplete(session));
    }
}
