using ONEVO.Agent.TrayApp.Controls;

namespace ONEVO.Agent.TrayApp.Tests.Controls;

public sealed class CameraPreviewCoverTests
{
    [Fact]
    public void Cover_SquareSlot_FourByThree_KeepsFullHeightAndCropsSides()
    {
        var size = CameraPreviewCover.Cover(204, 204, 4d / 3d);

        Assert.Equal(204, size.Height);
        Assert.InRange(size.Width, (204 * 4d / 3d) - 0.01, (204 * 4d / 3d) + 0.01);
    }

    [Fact]
    public void Cover_SquareSlot_SixteenByNine_KeepsFullHeightAndCropsSides()
    {
        var size = CameraPreviewCover.Cover(204, 204, 16d / 9d);

        Assert.Equal(204, size.Height);
        Assert.InRange(size.Width, (204 * 16d / 9d) - 0.01, (204 * 16d / 9d) + 0.01);
    }

    [Fact]
    public void Cover_PortraitFrame_KeepsFullWidth()
    {
        var size = CameraPreviewCover.Cover(204, 204, 3d / 4d);

        Assert.Equal(204, size.Width);
        Assert.InRange(size.Height, (204 * 4d / 3d) - 0.01, (204 * 4d / 3d) + 0.01);
    }

    [Fact]
    public void CoverScale_FourByThreeInSquare_ZoomsUntilHeightCovers()
    {
        var scale = CameraPreviewCover.CoverScale(204, 204, 4d / 3d);

        Assert.InRange(scale, (4d / 3d) - 0.01, (4d / 3d) + 0.01);
    }

    [Fact]
    public void CoverScale_SixteenByNineInSquare_ZoomsUntilHeightCovers()
    {
        var scale = CameraPreviewCover.CoverScale(204, 204, 16d / 9d);

        Assert.InRange(scale, (16d / 9d) - 0.01, (16d / 9d) + 0.01);
    }

    [Fact]
    public void CoverScale_SquareVideo_StaysAtOne()
    {
        Assert.Equal(1, CameraPreviewCover.CoverScale(204, 204, 1));
    }

    [Fact]
    public void Cover_UnknownAspect_UsesTheSlot()
    {
        var size = CameraPreviewCover.Cover(204, 204, 0);

        Assert.Equal(204, size.Width);
        Assert.Equal(204, size.Height);
    }
}
