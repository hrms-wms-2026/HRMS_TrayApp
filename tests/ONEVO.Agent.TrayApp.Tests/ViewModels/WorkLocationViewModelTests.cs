using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class WorkLocationViewModelTests
{
    private sealed class FixedLocationService : ILocationService
    {
        private readonly GeoPoint? _point;
        public FixedLocationService(GeoPoint? point) => _point = point;
        public Task<GeoPoint?> GetCurrentAsync(CancellationToken ct = default) =>
            Task.FromResult(_point);
    }

    [Fact]
    public void ApprovedLocations_HasTwoEntries()
    {
        var vm = new WorkLocationViewModel();
        Assert.Equal(2, vm.ApprovedLocations.Count);
    }

    [Fact]
    public void ApprovedLocations_ContainsOffice()
    {
        var vm = new WorkLocationViewModel();
        Assert.Contains(vm.ApprovedLocations,
            l => l.DisplayName == "Office" && l.Code == "OFFICE");
    }

    [Fact]
    public void ApprovedLocations_ContainsWorkFromHome()
    {
        var vm = new WorkLocationViewModel();
        Assert.Contains(vm.ApprovedLocations,
            l => l.DisplayName == "Work From Home" && l.Code == "WFH");
    }

    [Fact]
    public void FindNearestOffice_NearChennai_SelectsOffice()
    {
        var vm = new WorkLocationViewModel();
        // ~T.Nagar, Chennai
        var match = vm.FindNearestOffice(13.0418, 80.2341);
        Assert.NotNull(match);
        Assert.Equal("OFFICE", match!.Option.Code);
        Assert.Equal("Chennai", match.NearestCity);
        Assert.False(match.IsRemoteFallback);
        Assert.True(match.DistanceKm < 20);
    }

    [Fact]
    public void FindNearestOffice_NearBangalore_SelectsOffice()
    {
        var vm = new WorkLocationViewModel();
        var match = vm.FindNearestOffice(12.9716, 77.5946);
        Assert.NotNull(match);
        Assert.Equal("OFFICE", match!.Option.Code);
        Assert.Equal("Bangalore", match.NearestCity);
        Assert.False(match.IsRemoteFallback);
    }

    [Fact]
    public void FindNearestOffice_FarFromOffices_SuggestsWfh()
    {
        var vm = new WorkLocationViewModel();
        // London
        var match = vm.FindNearestOffice(51.5074, -0.1278);
        Assert.NotNull(match);
        Assert.Equal("WFH", match!.Option.Code);
        Assert.True(match.IsRemoteFallback);
    }

    [Fact]
    public async Task DetectLiveLocation_NearOffice_AutoSelectsOffice()
    {
        // Approximate Bangalore CBD
        var loc = new FixedLocationService(new GeoPoint(12.9716, 77.5946));
        var vm = new WorkLocationViewModel(loc);
        await vm.DetectLiveLocationCommand.ExecuteAsync(null);
        Assert.NotNull(vm.SelectedLocation);
        Assert.Equal("OFFICE", vm.SelectedLocation!.Code);
        Assert.Contains("Office selected", vm.LiveLocationStatus, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsDetectingLocation);
    }

    [Fact]
    public async Task DetectLiveLocation_FarFromOffice_AutoSelectsWfh()
    {
        // London
        var loc = new FixedLocationService(new GeoPoint(51.5074, -0.1278));
        var vm = new WorkLocationViewModel(loc);
        await vm.DetectLiveLocationCommand.ExecuteAsync(null);
        Assert.NotNull(vm.SelectedLocation);
        Assert.Equal("WFH", vm.SelectedLocation!.Code);
        Assert.Contains("Work From Home selected", vm.LiveLocationStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectLiveLocation_WhenUnavailable_DoesNotForceSelection()
    {
        var vm = new WorkLocationViewModel(new FixedLocationService(null));
        await vm.DetectLiveLocationCommand.ExecuteAsync(null);
        Assert.Null(vm.SelectedLocation);
        Assert.Contains("Could not get live location", vm.LiveLocationStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveAndContinueCommand_DisabledWhenNoSelection()
    {
        var vm = new WorkLocationViewModel();
        Assert.False(vm.SaveAndContinueCommand.CanExecute(null));
    }

    [Fact]
    public void SaveAndContinueCommand_EnabledAfterSelection()
    {
        var vm = new WorkLocationViewModel();
        vm.SelectedLocation = vm.ApprovedLocations[0];
        Assert.True(vm.SaveAndContinueCommand.CanExecute(null));
    }

    [Fact]
    public void WorkLocationOption_HasSubTitle()
    {
        var option = new WorkLocationOption("Office", "OFFICE", "Your registered office");
        Assert.Equal("Your registered office", option.SubTitle);
    }

    [Fact]
    public async Task SaveAndContinue_WithLiveFix_WritesGpsStringsToPrefs()
    {
        var prefs = new FakePreferencesStore();
        var loc   = new FixedLocationService(new GeoPoint(13.0827, 80.2707));
        var vm    = new WorkLocationViewModel(loc, prefs);

        await vm.DetectLiveLocationCommand.ExecuteAsync(null);
        vm.SelectedLocation = vm.ApprovedLocations[0];
        await vm.SaveAndContinueCommand.ExecuteAsync(null);

        Assert.Equal((13.0827).ToString("G17"), prefs.Get("onevo.live_latitude",  ""));
        Assert.Equal((80.2707).ToString("G17"), prefs.Get("onevo.live_longitude", ""));
    }

    [Fact]
    public async Task SaveAndContinue_SavesGenericOfficeCode()
    {
        var prefs = new FakePreferencesStore();
        var vm    = new WorkLocationViewModel(new FixedLocationService(null), prefs);
        vm.SelectedLocation = vm.ApprovedLocations[0]; // Office

        await vm.SaveAndContinueCommand.ExecuteAsync(null);

        Assert.Equal("OFFICE", prefs.Get("onevo.work_location_code", ""));
        Assert.Equal("Office", prefs.Get("onevo.work_location_display", ""));
    }

    [Fact]
    public async Task SaveAndContinue_SavesWfhCode()
    {
        var prefs = new FakePreferencesStore();
        var vm    = new WorkLocationViewModel(new FixedLocationService(null), prefs);
        vm.SelectedLocation = vm.ApprovedLocations[1]; // Work From Home

        await vm.SaveAndContinueCommand.ExecuteAsync(null);

        Assert.Equal("WFH", prefs.Get("onevo.work_location_code", ""));
        Assert.Equal("Work From Home", prefs.Get("onevo.work_location_display", ""));
    }
}
