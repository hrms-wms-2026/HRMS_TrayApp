namespace ONEVO.Agent.TrayApp.Tests;

public class MauiProgramTests
{
    [Fact]
    public void DisableMauiAspireIntegration_TurnsOffTheAspireEnvVarConfigPath()
    {
        MauiProgram.DisableMauiAspireIntegration();

        // With this switch off, MauiApp.CreateBuilder()'s internal ConfigureEnvironmentVariables()
        // returns immediately instead of reading ASPNETCORE_ENVIRONMENT/DOTNET_ENVIRONMENT — the
        // path that collapses both to the same "ENVIRONMENT" config key and throws when a launcher
        // (e.g. scripts/run-all.ps1) sets both before starting TrayApp. Microsoft.Maui.RuntimeFeature
        // itself is internal to the MAUI assembly, so assert via the same AppContext switch it reads.
        var found = AppContext.TryGetSwitch("Microsoft.Maui.RuntimeFeature.EnableMauiAspire", out var enabled);
        Assert.True(found);
        Assert.False(enabled);
    }
}
