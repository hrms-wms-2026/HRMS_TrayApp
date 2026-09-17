namespace ONEVO.Agent.TrayApp.ViewModels;

using System.Text.Json;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class LegalConsentViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;
    private readonly IPreferencesStore _preferences;

    [ObservableProperty] private IReadOnlyList<PendingLegalDocumentPayload> _pendingDocuments = [];
    [ObservableProperty] private bool _completed;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isSubmitting;

    public LegalConsentViewModel(INamedPipeClient pipe, IPreferencesStore preferences)
    {
        Title = "Before You Continue";
        _pipe = pipe;
        _preferences = preferences;
        PendingDocuments = LoadPendingDocuments();
    }

    private IReadOnlyList<PendingLegalDocumentPayload> LoadPendingDocuments()
    {
        var json = _preferences.Get(SessionPreferenceKeys.PendingLegalDocumentsJson, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<PendingLegalDocumentPayload>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    [RelayCommand]
    private static void OpenDocument(PendingLegalDocumentPayload document)
    {
        if (string.IsNullOrWhiteSpace(document.ContentUrl))
            return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = document.ContentUrl,
                UseShellExecute = true
            });
        }
        catch { /* browser unavailable */ }
    }

    [RelayCommand]
    private async Task AcceptAsync(CancellationToken ct)
    {
        ErrorMessage = null;
        IsSubmitting = true;
        try
        {
            var acceptances = PendingDocuments
                .Select(d => new LegalAcceptanceItemPayload(d.DocumentType, d.Version))
                .ToArray();

            var result = await _pipe.SendLegalAcceptanceSubmitAsync(acceptances, ct);

            if (result is null)
            {
                ErrorMessage = "No response from OneXso Agent Service. Is the service running?";
                return;
            }

            if (!result.Success)
            {
                ErrorMessage = $"Couldn't submit your acceptance ({result.ErrorCode ?? "unknown error"}). Try again.";
                return;
            }

            _preferences.Remove(SessionPreferenceKeys.PendingLegalDocumentsJson);
            Completed = true;
            try { await Shell.Current.GoToAsync(SetupFlow.AfterActivation); }
            catch { /* unit tests */ }
        }
        finally
        {
            IsSubmitting = false;
        }
    }
}
