using System.Text.Json;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class LegalConsentViewModelTests
{
    private static readonly PendingLegalDocumentPayload DocOne = new(
        "privacy_policy", "2.0", "Privacy Policy", null, "/api/v1/legal/documents/privacy_policy/2.0");
    private static readonly PendingLegalDocumentPayload DocTwo = new(
        "terms_of_service", "1.3", "Terms of Service", "https://portal.example.com/legal/terms", "/api/v1/legal/documents/terms_of_service/1.3");

    private static FakePreferencesStore PreferencesWithPendingDocs(params PendingLegalDocumentPayload[] docs)
    {
        var prefs = new FakePreferencesStore();
        prefs.Set(SessionPreferenceKeys.PendingLegalDocumentsJson, JsonSerializer.Serialize<IReadOnlyList<PendingLegalDocumentPayload>>(docs));
        return prefs;
    }

    [Fact]
    public void Constructor_LoadsPendingDocumentsFromPreferences()
    {
        var prefs = PreferencesWithPendingDocs(DocOne, DocTwo);
        var vm = new LegalConsentViewModel(new FakeNamedPipeClient(), prefs);

        Assert.Equal(2, vm.PendingDocuments.Count);
        Assert.Equal("privacy_policy", vm.PendingDocuments[0].DocumentType);
    }

    [Fact]
    public async Task AcceptCommand_PostsAllPendingDocumentIdentifiersAndCompletes()
    {
        var prefs = PreferencesWithPendingDocs(DocOne, DocTwo);
        var pipe = new FakeNamedPipeClient();
        var vm = new LegalConsentViewModel(pipe, prefs);

        await vm.AcceptCommand.ExecuteAsync(null);

        Assert.Single(pipe.LegalAcceptanceSubmitCalls);
        Assert.Equal(2, pipe.LegalAcceptanceSubmitCalls[0].Count);
        Assert.Contains(pipe.LegalAcceptanceSubmitCalls[0], a => a.DocumentType == "privacy_policy" && a.Version == "2.0");
        Assert.Contains(pipe.LegalAcceptanceSubmitCalls[0], a => a.DocumentType == "terms_of_service" && a.Version == "1.3");
        Assert.True(vm.Completed);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task AcceptCommand_OnSuccess_ClearsPendingDocumentsPreference()
    {
        var prefs = PreferencesWithPendingDocs(DocOne);
        var vm = new LegalConsentViewModel(new FakeNamedPipeClient(), prefs);

        await vm.AcceptCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, prefs.Get(SessionPreferenceKeys.PendingLegalDocumentsJson, string.Empty));
    }

    [Fact]
    public async Task AcceptCommand_WhenBackendCallFails_ShowsRetryableErrorAndDoesNotComplete()
    {
        var prefs = PreferencesWithPendingDocs(DocOne);
        var pipe = new FakeNamedPipeClient
        {
            NextLegalAcceptanceResult = new LegalAcceptanceResultPayload(false, "SERVICE_UNAVAILABLE")
        };
        var vm = new LegalConsentViewModel(pipe, prefs);

        await vm.AcceptCommand.ExecuteAsync(null);

        Assert.False(vm.Completed);
        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public async Task AcceptCommand_WhenNoResponseFromService_ShowsRetryableErrorAndDoesNotComplete()
    {
        var prefs = PreferencesWithPendingDocs(DocOne);
        var pipe = new FakeNamedPipeClient { LegalAcceptanceSubmitReturnsNull = true };
        var vm = new LegalConsentViewModel(pipe, prefs);

        await vm.AcceptCommand.ExecuteAsync(null);

        Assert.False(vm.Completed);
        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public async Task AcceptCommand_SetsIsSubmittingDuringTheCall()
    {
        var prefs = PreferencesWithPendingDocs(DocOne);
        var vm = new LegalConsentViewModel(new FakeNamedPipeClient(), prefs);

        var task = vm.AcceptCommand.ExecuteAsync(null);
        await task;

        Assert.False(vm.IsSubmitting);
    }
}
