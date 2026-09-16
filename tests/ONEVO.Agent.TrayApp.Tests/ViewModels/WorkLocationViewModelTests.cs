using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class WorkLocationViewModelTests
{
    private static FakeNamedPipeClient LocationTrackingEnabledPipe() =>
        new() { LastKnownPolicy = new AgentPolicy { Version = "v1", LocationTrackingEnabled = true } };

    private static WorkLocationViewModel MakeVm(
        LocationCaptureResult? result = null,
        FakeWorkLocationStore? store = null,
        FakePreferencesStore? preferences = null,
        FakeNamedPipeClient? pipe = null)
    {
        result ??= LocationCaptureResult.Success(new GeoLocationFix(6.9271, 79.8612, 15, DateTimeOffset.UtcNow));
        return new WorkLocationViewModel(
            new FakeLocationService(result),
            store ?? new FakeWorkLocationStore(),
            preferences ?? new FakePreferencesStore(),
            // Existing tests assume the always-capture behavior that predates location-tracking
            // gating, so default the fake pipe's policy to "enabled" unless a test says otherwise.
            pipe ?? LocationTrackingEnabledPipe(),
            // The real evaluator is pure Haversine math - no need to fake it out.
            new GeofenceEvaluator());
    }

    [Fact]
    public void Options_AreExactlyTheApprovedThree()
    {
        var vm = MakeVm();
        Assert.Equal(["Office", "Work From Home", "Other Approved Location"],
            vm.Options.Select(x => x.DisplayName));
    }

    [Fact]
    public async Task DetectLocation_Success_SetsCurrentFix()
    {
        var fix = new GeoLocationFix(6.9271, 79.8612, 15, DateTimeOffset.UtcNow);
        var vm = MakeVm(LocationCaptureResult.Success(fix));

        await vm.DetectLocationCommand.ExecuteAsync(null);

        Assert.Equal(fix, vm.CurrentFix);
        Assert.Null(vm.ErrorMessage);
        Assert.True(vm.IsLocationVerified);
        Assert.Equal("Detected location", vm.DetectionTitle);
        Assert.DoesNotContain("9.66557", vm.DetectionDetail);
    }

    /// <summary>
    /// The bug this covers: DetectionDetail used to unconditionally claim "outside your registered
    /// office" regardless of the actual fix, because no comparison against the office point ever
    /// ran. These three tests pin the real behavior: no configured office, a fix far from a
    /// configured office, and a fix within range of one.
    /// </summary>
    [Fact]
    public async Task DetectLocation_NoOfficeConfigured_DoesNotClaimInsideOrOutside()
    {
        var pipe = new FakeNamedPipeClient
        {
            LastKnownPolicy = new AgentPolicy
            {
                Version = "v1", LocationTrackingEnabled = true, OfficeLatitude = null, OfficeLongitude = null
            }
        };
        var vm = MakeVm(pipe: pipe);

        await vm.DetectLocationCommand.ExecuteAsync(null);

        Assert.DoesNotContain("outside", vm.DetectionDetail);
        Assert.DoesNotContain("within", vm.DetectionDetail);
    }

    [Fact]
    public async Task DetectLocation_FarFromConfiguredOffice_SaysOutsideOffice()
    {
        // Colombo office, fix from ~150km away (Kandy) - well past any sane geofence radius.
        var pipe = new FakeNamedPipeClient
        {
            LastKnownPolicy = new AgentPolicy
            {
                Version = "v1", LocationTrackingEnabled = true, OfficeLatitude = 6.9271, OfficeLongitude = 79.8612
            }
        };
        var farFix = new GeoLocationFix(7.2906, 80.6337, 15, DateTimeOffset.UtcNow);
        var vm = MakeVm(LocationCaptureResult.Success(farFix), pipe: pipe);

        await vm.DetectLocationCommand.ExecuteAsync(null);

        Assert.Contains("outside your registered office", vm.DetectionDetail);
    }

    [Fact]
    public async Task DetectLocation_WithinConfiguredOfficeRadius_SaysInsideOffice()
    {
        var officeLat = 6.9271;
        var officeLng = 79.8612;
        var pipe = new FakeNamedPipeClient
        {
            LastKnownPolicy = new AgentPolicy
            {
                Version = "v1", LocationTrackingEnabled = true, OfficeLatitude = officeLat, OfficeLongitude = officeLng
            }
        };
        // A few meters off the exact office point - well within the 300m default office radius.
        var nearFix = new GeoLocationFix(officeLat + 0.0001, officeLng, 15, DateTimeOffset.UtcNow);
        var vm = MakeVm(LocationCaptureResult.Success(nearFix), pipe: pipe);

        await vm.DetectLocationCommand.ExecuteAsync(null);

        Assert.Contains("within your registered office", vm.DetectionDetail);
    }

    [Fact]
    public async Task DetectLocation_PermissionDenied_SetsErrorAndNoFix()
    {
        var vm = MakeVm(LocationCaptureResult.Failed(LocationCaptureFailure.PermissionDenied));

        await vm.DetectLocationCommand.ExecuteAsync(null);

        Assert.Null(vm.CurrentFix);
        Assert.Contains("Windows Settings", vm.ErrorMessage);
        Assert.False(vm.IsLocationVerified);
        Assert.True(vm.HasDetectionError);
        Assert.Equal("Location unavailable", vm.DetectionTitle);
    }

    [Fact]
    public async Task ConfirmLocation_DisabledUntilOptionSelectedAndFixCaptured()
    {
        var vm = MakeVm();
        Assert.False(vm.ConfirmLocationCommand.CanExecute(null));

        await vm.DetectLocationCommand.ExecuteAsync(null);
        Assert.False(vm.ConfirmLocationCommand.CanExecute(null));

        vm.SelectOptionCommand.Execute(vm.Options.Single(x => x.Code == "WFH"));
        Assert.True(vm.ConfirmLocationCommand.CanExecute(null));
    }

    [Fact]
    public async Task ConfirmLocation_FailedDetection_NeverEnablesConfirm()
    {
        var vm = MakeVm(LocationCaptureResult.Failed(LocationCaptureFailure.ServicesDisabled));
        await vm.DetectLocationCommand.ExecuteAsync(null);
        vm.SelectOptionCommand.Execute(vm.Options[0]);

        Assert.False(vm.ConfirmLocationCommand.CanExecute(null));
    }

    [Fact]
    public async Task ConfirmLocation_SavesReferenceWithSelectedOptionAndLiveFix()
    {
        var fix = new GeoLocationFix(6.9271, 79.8612, 18, DateTimeOffset.UtcNow);
        var store = new FakeWorkLocationStore();
        var vm = MakeVm(LocationCaptureResult.Success(fix), store);

        await vm.DetectLocationCommand.ExecuteAsync(null);
        vm.SelectOptionCommand.Execute(vm.Options.Single(x => x.Code == "WFH"));
        await vm.ConfirmLocationCommand.ExecuteAsync(null);

        Assert.NotNull(store.Value);
        Assert.Equal(WorkLocationKind.WorkFromHome, store.Value!.Kind);
        Assert.Equal(250, store.Value.RadiusMeters);
        Assert.Equal(fix.Latitude, store.Value.Latitude);
        Assert.Equal(fix.Longitude, store.Value.Longitude);
    }

    [Fact]
    public async Task ConfirmLocation_SetsLegacyPreferenceKeysForFacePhotoSubmission()
    {
        var fix = new GeoLocationFix(6.9271, 79.8612, 18, DateTimeOffset.UtcNow);
        var preferences = new FakePreferencesStore();
        var vm = MakeVm(LocationCaptureResult.Success(fix), preferences: preferences);

        await vm.DetectLocationCommand.ExecuteAsync(null);
        vm.SelectOptionCommand.Execute(vm.Options.Single(x => x.Code == "OFFICE"));
        await vm.ConfirmLocationCommand.ExecuteAsync(null);

        Assert.Equal("OFFICE", preferences.Get(SessionPreferenceKeys.WorkLocationCode, ""));
        Assert.Equal("Office", preferences.Get(SessionPreferenceKeys.WorkLocationDisplay, ""));
        Assert.Equal(fix.Latitude.ToString("G17"), preferences.Get(SessionPreferenceKeys.LiveLatitude, ""));
        Assert.Equal(fix.Longitude.ToString("G17"), preferences.Get(SessionPreferenceKeys.LiveLongitude, ""));
        Assert.True(WorkLocationFlow.IsConfirmedToday(preferences));
    }

    [Fact]
    public async Task ConfirmLocation_SetsIsConfirmedTrue()
    {
        var vm = MakeVm();
        await vm.DetectLocationCommand.ExecuteAsync(null);
        vm.SelectOptionCommand.Execute(vm.Options[0]);

        Assert.False(vm.IsConfirmed);
        await vm.ConfirmLocationCommand.ExecuteAsync(null);
        Assert.True(vm.IsConfirmed);
    }

    [Fact]
    public void NavigateBackCommand_Exists()
    {
        var vm = MakeVm();
        Assert.True(vm.NavigateBackCommand.CanExecute(null));
    }

    [Fact]
    public void Options_CarryOfficeAndHomeIcons()
    {
        var vm = MakeVm();
        Assert.Equal("icon_office_building.png", vm.Options[0].IconSource);
        Assert.Equal("icon_home_house.png", vm.Options[1].IconSource);
        Assert.Equal("icon_office_building.png", vm.Options[2].IconSource);
    }

    [Fact]
    public void SelectOption_MarksOnlyThatOptionSelected()
    {
        var vm = MakeVm();
        vm.SelectOptionCommand.Execute(vm.Options[1]);

        Assert.False(vm.Options[0].IsSelected);
        Assert.True(vm.Options[1].IsSelected);
        Assert.False(vm.Options[2].IsSelected);
    }

    [Fact]
    public async Task DetectLocation_TrackingDisabled_NeverCallsLocationServiceAndClearsFix()
    {
        var locationService = new FakeLocationService(
            LocationCaptureResult.Success(new GeoLocationFix(6.9271, 79.8612, 15, DateTimeOffset.UtcNow)));
        var pipe = new FakeNamedPipeClient { LastKnownPolicy = new AgentPolicy { Version = "v1", LocationTrackingEnabled = false } };
        var vm = new WorkLocationViewModel(
            locationService, new FakeWorkLocationStore(), new FakePreferencesStore(), pipe, new GeofenceEvaluator());

        await vm.DetectLocationCommand.ExecuteAsync(null);

        Assert.Equal(0, locationService.CallCount);
        Assert.Null(vm.CurrentFix);
        Assert.False(vm.IsLocationVerified);
        Assert.False(vm.HasDetectionError);
        Assert.Equal("Location tracking is off", vm.DetectionTitle);
    }

    [Fact]
    public async Task ConfirmLocation_TrackingDisabled_EnabledOnceOptionSelectedWithoutFix()
    {
        var pipe = new FakeNamedPipeClient { LastKnownPolicy = new AgentPolicy { Version = "v1", LocationTrackingEnabled = false } };
        var vm = new WorkLocationViewModel(
            new FakeLocationService(LocationCaptureResult.Success(new GeoLocationFix(6.9271, 79.8612, 15, DateTimeOffset.UtcNow))),
            new FakeWorkLocationStore(), new FakePreferencesStore(), pipe, new GeofenceEvaluator());

        await vm.DetectLocationCommand.ExecuteAsync(null);
        Assert.False(vm.ConfirmLocationCommand.CanExecute(null));

        vm.SelectOptionCommand.Execute(vm.Options.Single(x => x.Code == "OFFICE"));
        Assert.True(vm.ConfirmLocationCommand.CanExecute(null));
    }

    [Fact]
    public async Task ConfirmLocation_TrackingDisabled_SavesCodeAndDisplayButNoReferenceOrLiveCoordinates()
    {
        var store = new FakeWorkLocationStore();
        var preferences = new FakePreferencesStore();
        var pipe = new FakeNamedPipeClient { LastKnownPolicy = new AgentPolicy { Version = "v1", LocationTrackingEnabled = false } };
        var vm = new WorkLocationViewModel(
            new FakeLocationService(LocationCaptureResult.Success(new GeoLocationFix(6.9271, 79.8612, 15, DateTimeOffset.UtcNow))),
            store, preferences, pipe, new GeofenceEvaluator());

        await vm.DetectLocationCommand.ExecuteAsync(null);
        vm.SelectOptionCommand.Execute(vm.Options.Single(x => x.Code == "OFFICE"));
        await vm.ConfirmLocationCommand.ExecuteAsync(null);

        Assert.True(vm.IsConfirmed);
        Assert.Null(store.Value);
        Assert.Equal("OFFICE", preferences.Get(SessionPreferenceKeys.WorkLocationCode, ""));
        Assert.Equal("Office", preferences.Get(SessionPreferenceKeys.WorkLocationDisplay, ""));
        Assert.Equal("", preferences.Get(SessionPreferenceKeys.LiveLatitude, ""));
        Assert.Equal("", preferences.Get(SessionPreferenceKeys.LiveLongitude, ""));
        Assert.True(WorkLocationFlow.IsConfirmedToday(preferences));
    }

    [Fact]
    public async Task ConfirmLocation_WithFix_SendsWorkLocationConfirmOverPipe()
    {
        var pipe = LocationTrackingEnabledPipe();
        var fix = new GeoLocationFix(6.9271, 79.8612, 15, DateTimeOffset.UtcNow);
        var vm = MakeVm(LocationCaptureResult.Success(fix), pipe: pipe);

        await vm.DetectLocationCommand.ExecuteAsync(null);
        vm.SelectOptionCommand.Execute(vm.Options[1]); // "Work From Home"
        await vm.ConfirmLocationCommand.ExecuteAsync(null);

        Assert.Single(pipe.WorkLocationConfirmCalls);
        var call = pipe.WorkLocationConfirmCalls[0];
        Assert.Equal("home", call.LocationType);
        Assert.Equal(6.9271, call.Latitude);
        Assert.Equal(79.8612, call.Longitude);
    }

    [Fact]
    public async Task ConfirmLocation_BackendRejectsSend_DoesNotMarkDayConfirmed()
    {
        var preferences = new FakePreferencesStore();
        var pipe = LocationTrackingEnabledPipe();
        pipe.WorkLocationConfirmResult = new WorkLocationConfirmResultPayload(false, "UNAUTHORIZED");
        var vm = MakeVm(preferences: preferences, pipe: pipe);

        await vm.DetectLocationCommand.ExecuteAsync(null);
        vm.SelectOptionCommand.Execute(vm.Options.Single(x => x.Code == "WFH"));
        await vm.ConfirmLocationCommand.ExecuteAsync(null);

        Assert.Single(pipe.WorkLocationConfirmCalls);
        Assert.False(WorkLocationFlow.IsConfirmedToday(preferences));
        // Navigation still proceeds so the employee isn't stuck; the day just isn't locked in,
        // so the confirm screen re-prompts next time.
        Assert.True(vm.IsConfirmed);
    }

    [Fact]
    public async Task ConfirmLocation_PipeReturnsNull_DoesNotMarkDayConfirmed()
    {
        var preferences = new FakePreferencesStore();
        var pipe = LocationTrackingEnabledPipe();
        pipe.WorkLocationConfirmReturnsNull = true;
        var vm = MakeVm(preferences: preferences, pipe: pipe);

        await vm.DetectLocationCommand.ExecuteAsync(null);
        vm.SelectOptionCommand.Execute(vm.Options.Single(x => x.Code == "WFH"));
        await vm.ConfirmLocationCommand.ExecuteAsync(null);

        Assert.False(WorkLocationFlow.IsConfirmedToday(preferences));
    }

    [Fact]
    public void Options_WhenPolicyHasAllowedRadiusMeters_UsesPolicyRadiusForEveryOption()
    {
        var pipe = new FakeNamedPipeClient
        {
            LastKnownPolicy = new AgentPolicy { Version = "v1", LocationTrackingEnabled = true, AllowedRadiusMeters = 175 }
        };
        var vm = MakeVm(pipe: pipe);

        Assert.All(vm.Options, option => Assert.Equal(175, option.RadiusMeters));
    }

    [Fact]
    public void Options_WhenPolicyHasNoAllowedRadiusMeters_FallsBackToHardcodedDefaults()
    {
        var pipe = new FakeNamedPipeClient
        {
            LastKnownPolicy = new AgentPolicy { Version = "v1", LocationTrackingEnabled = true, AllowedRadiusMeters = null }
        };
        var vm = MakeVm(pipe: pipe);

        Assert.Equal(300, vm.Options.Single(x => x.Code == "OFFICE").RadiusMeters);
        Assert.Equal(250, vm.Options.Single(x => x.Code == "WFH").RadiusMeters);
        Assert.Equal(250, vm.Options.Single(x => x.Code == "OTHER").RadiusMeters);
    }

    [Fact]
    public async Task ConfirmLocation_TrackingDisabledNoFix_SendsWorkLocationConfirmWithNullCoordinates()
    {
        var pipe = new FakeNamedPipeClient { LastKnownPolicy = new AgentPolicy { Version = "v1", LocationTrackingEnabled = false } };
        var vm = MakeVm(store: new FakeWorkLocationStore(), pipe: pipe);

        vm.SelectOptionCommand.Execute(vm.Options[0]); // "Office"
        await vm.ConfirmLocationCommand.ExecuteAsync(null);

        Assert.Single(pipe.WorkLocationConfirmCalls);
        var call = pipe.WorkLocationConfirmCalls[0];
        Assert.Equal("office", call.LocationType);
        Assert.Null(call.Latitude);
        Assert.Null(call.Longitude);
    }
}
