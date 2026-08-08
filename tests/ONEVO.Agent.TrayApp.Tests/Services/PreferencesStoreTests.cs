namespace ONEVO.Agent.TrayApp.Tests.Services;

using ONEVO.Agent.TrayApp.Services;

public sealed class PreferencesStoreTests
{
    [Fact]
    public void Get_NoPlatformContext_ReturnsDefaultWithoutThrowing()
    {
        var store = new PreferencesStore();
        var value = store.Get("any.key", "fallback");
        Assert.Equal("fallback", value);
    }

    [Fact]
    public void Set_NoPlatformContext_DoesNotThrow()
    {
        var store = new PreferencesStore();
        var exception = Record.Exception(() => store.Set("any.key", "value"));
        Assert.Null(exception);
    }
}
