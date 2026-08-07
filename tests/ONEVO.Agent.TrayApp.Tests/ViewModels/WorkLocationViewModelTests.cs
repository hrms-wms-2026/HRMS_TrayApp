using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class WorkLocationViewModelTests
{
    [Fact]
    public void ApprovedLocations_HasFourEntries()
    {
        var vm = new WorkLocationViewModel();
        Assert.Equal(4, vm.ApprovedLocations.Count);
    }

    [Fact]
    public void ApprovedLocations_ContainsChennaiOffice()
    {
        var vm = new WorkLocationViewModel();
        Assert.Contains(vm.ApprovedLocations,
            l => l.DisplayName == "Chennai Office" && l.Code == "CHENNAI");
    }

    [Fact]
    public void ApprovedLocations_ContainsBangaloreOffice()
    {
        var vm = new WorkLocationViewModel();
        Assert.Contains(vm.ApprovedLocations,
            l => l.DisplayName == "Bangalore Office" && l.Code == "BANGALORE");
    }

    [Fact]
    public void ApprovedLocations_ContainsHyderabadOffice()
    {
        var vm = new WorkLocationViewModel();
        Assert.Contains(vm.ApprovedLocations,
            l => l.DisplayName == "Hyderabad Office" && l.Code == "HYDERABAD");
    }

    [Fact]
    public void ApprovedLocations_ContainsWorkFromHome()
    {
        var vm = new WorkLocationViewModel();
        Assert.Contains(vm.ApprovedLocations,
            l => l.DisplayName == "Work From Home" && l.Code == "WFH");
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
    public void FilteredLocations_FiltersOnSearchText()
    {
        var vm = new WorkLocationViewModel();
        vm.SearchText = "Chennai";
        Assert.Single(vm.FilteredLocations);
        Assert.Equal("Chennai Office", vm.FilteredLocations.First().DisplayName);
    }

    [Fact]
    public void WorkLocationOption_HasSubTitle()
    {
        var option = new WorkLocationOption("Chennai Office", "CHENNAI", "Tamil Nadu, India");
        Assert.Equal("Tamil Nadu, India", option.SubTitle);
    }
}
