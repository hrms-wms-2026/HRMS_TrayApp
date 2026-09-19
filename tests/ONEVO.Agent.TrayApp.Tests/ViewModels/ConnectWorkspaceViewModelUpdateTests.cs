namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;
using Xunit;

public sealed class ConnectWorkspaceViewModelUpdateTests
{
    private sealed class FakeUpdateChecker : IUpdateChecker
    {
        public UpdateCheckResultPayload? Result { get; set; }
        public bool Throws { get; set; }

        public Task<UpdateCheckResultPayload?> CheckAsync(CancellationToken ct) =>
            Throws ? throw new InvalidOperationException("boom") : Task.FromResult(Result);
    }

    private static UpdateCheckResultPayload Update(bool mandatory) => new(
        true, true, mandatory, "1.3.0", "https://dl.example.com/ONEVO-1.3.0.msix",
        new string('a', 64), 10, null, null);

    private static ConnectWorkspaceViewModel Build(FakeUpdateChecker checker) =>
        new(new FakeNamedPipeClient(), new FakePreferencesStore(), checker);

    [Fact]
    public async Task MandatoryUpdate_SetsBanner_AndEnablesDownload()
    {
        var vm = Build(new FakeUpdateChecker { Result = Update(mandatory: true) });

        await vm.CheckForUpdateCommand.ExecuteAsync(null);

        Assert.True(vm.HasUpdateBanner);
        Assert.Contains("required update (v1.3.0)", vm.UpdateBannerText);
        Assert.True(vm.DownloadUpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task OptionalUpdate_SetsSoftBanner()
    {
        var vm = Build(new FakeUpdateChecker { Result = Update(mandatory: false) });

        await vm.CheckForUpdateCommand.ExecuteAsync(null);

        Assert.True(vm.HasUpdateBanner);
        Assert.DoesNotContain("required", vm.UpdateBannerText);
        Assert.Contains("v1.3.0", vm.UpdateBannerText);
    }

    [Fact]
    public async Task NoUpdate_LeavesBannerEmpty_AndDownloadDisabled()
    {
        var vm = Build(new FakeUpdateChecker { Result = null });

        await vm.CheckForUpdateCommand.ExecuteAsync(null);

        Assert.False(vm.HasUpdateBanner);
        Assert.True(string.IsNullOrEmpty(vm.UpdateBannerText));
        Assert.False(vm.DownloadUpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task CheckerFailure_IsSwallowed()
    {
        var vm = Build(new FakeUpdateChecker { Throws = true });

        await vm.CheckForUpdateCommand.ExecuteAsync(null);

        Assert.False(vm.HasUpdateBanner);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task WithoutAChecker_CheckIsANoOp()
    {
        var vm = new ConnectWorkspaceViewModel(new FakeNamedPipeClient(), new FakePreferencesStore());

        await vm.CheckForUpdateCommand.ExecuteAsync(null);

        Assert.False(vm.HasUpdateBanner);
    }
}
