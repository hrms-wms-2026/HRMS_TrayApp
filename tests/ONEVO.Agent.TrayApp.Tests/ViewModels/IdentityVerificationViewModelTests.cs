using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;
using Xunit;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class IdentityVerificationViewModelTests
{
    [Fact]
    public void LoadCapturedPhoto_AppliesAwsValidationFlagsAndSimilarity()
    {
        var buffer = new CapturedPhotoBuffer
        {
            LastValidation = new FacePhotoValidateResultPayload(
                true, null, true, true, true, true, true, 93f, null)
        };
        var vm = new IdentityVerificationViewModel(buffer, new FakePreferencesStore());

        vm.LoadCapturedPhoto();

        Assert.True(vm.IsGoodLighting);
        Assert.True(vm.IsFaceVisible);
        Assert.True(vm.IsNoMaskOrGlasses);
        Assert.Equal(93, vm.MatchPercentage);
        Assert.Equal("Identity matched", vm.StatusText);
    }

    [Fact]
    public void LoadCapturedPhoto_WithoutValidation_LeavesChecksOff()
    {
        var vm = new IdentityVerificationViewModel(new CapturedPhotoBuffer(), new FakePreferencesStore());

        vm.LoadCapturedPhoto();

        Assert.False(vm.IsGoodLighting);
        Assert.False(vm.IsFaceVisible);
        Assert.False(vm.IsNoMaskOrGlasses);
        Assert.Equal(0, vm.MatchPercentage);
    }
}
