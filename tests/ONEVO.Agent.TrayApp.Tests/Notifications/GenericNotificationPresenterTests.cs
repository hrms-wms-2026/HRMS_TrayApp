namespace ONEVO.Agent.TrayApp.Tests.Notifications;

using System.Text.Json;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using Xunit;

public sealed class GenericNotificationPresenterTests : IDisposable
{
    private readonly string _logPath = Path.Combine(Path.GetTempPath(), $"shown-notifications-{Guid.NewGuid():N}.json");
    private readonly FakeNamedPipeClient _pipe = new();
    private readonly RecordingToast _toast = new();
    private readonly DateTimeOffset _now = new(2026, 10, 18, 16, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (File.Exists(_logPath))
            File.Delete(_logPath);
    }

    private GenericNotificationPresenter Build(int capacity = ShownNotificationLog.DefaultCapacity)
        => new(_pipe, _toast, new ShownNotificationLog(_logPath, capacity), () => _now);

    private GenericNotificationPushPayload Payload(Guid? id = null, DateTimeOffset? expiresAt = null) => new()
    {
        DeliveryId = id ?? Guid.NewGuid(), EventKey = "work_approval_requested", Title = "Approval needed",
        Message = "Bala asked", ActionKey = "open_related", RelatedEntityType = "work_approval_request",
        RelatedEntityId = Guid.NewGuid(), ExpiresAt = expiresAt ?? _now.AddHours(1)
    };

    private IEnumerable<Guid> AcceptedIds() => _pipe.SentEnvelopes
        .Where(e => e.Type == IpcMessageTypes.GenericNotificationAccepted)
        .Select(e => e.Payload!.Value.Deserialize<GenericNotificationAcceptedPayload>()!.DeliveryId);

    [Fact]
    public async Task NewDelivery_IsShownOnce_ThenConfirmedToTheService()
    {
        var payload = Payload();

        await Build().HandleAsync(payload, CancellationToken.None);

        var shown = Assert.Single(_toast.Shown);
        Assert.Equal("Approval needed", shown.Payload.Title);
        Assert.Equal(GenericNotificationActivation.BuildArguments(payload), shown.Arguments);
        Assert.Equal([payload.DeliveryId], AcceptedIds());
    }

    [Fact]
    public async Task RepeatedDelivery_IsNotShownAgain_ButIsStillConfirmed()
    {
        var payload = Payload();
        await Build().HandleAsync(payload, CancellationToken.None);

        // A new presenter (e.g. after a tray restart) reads the same on-disk log.
        await Build().HandleAsync(payload, CancellationToken.None);

        Assert.Single(_toast.Shown);
        Assert.Equal([payload.DeliveryId, payload.DeliveryId], AcceptedIds());
    }

    [Fact]
    public async Task ExpiredDelivery_IsNeitherShownNorConfirmed()
    {
        await Build().HandleAsync(Payload(expiresAt: _now.AddMinutes(-1)), CancellationToken.None);

        Assert.Empty(_toast.Shown);
        Assert.Empty(AcceptedIds());
    }

    [Fact]
    public async Task ToastFailure_IsNotConfirmed_SoTheServiceResendsIt()
    {
        _toast.Fail = true;

        await Build().HandleAsync(Payload(), CancellationToken.None);

        Assert.Empty(AcceptedIds());
    }

    [Fact]
    public async Task ToastFailure_ThenRetry_IsShownOnTheNextOffer()
    {
        var payload = Payload();
        _toast.Fail = true;
        await Build().HandleAsync(payload, CancellationToken.None);

        _toast.Fail = false;
        await Build().HandleAsync(payload, CancellationToken.None);

        Assert.Single(_toast.Shown);
        Assert.Equal([payload.DeliveryId], AcceptedIds());
    }

    [Fact]
    public async Task ConcurrentOffersOfTheSameDelivery_ShowItOnce()
    {
        var presenter = Build();
        var payload = Payload();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => presenter.HandleAsync(payload, CancellationToken.None))));

        Assert.Single(_toast.Shown);
    }

    [Fact]
    public void ShownLog_IsBounded_KeepingTheNewestIds()
    {
        var log = new ShownNotificationLog(_logPath, capacity: 3);
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        ids.ForEach(log.Add);

        var reloaded = new ShownNotificationLog(_logPath, capacity: 3);

        Assert.False(reloaded.Contains(ids[0]));
        Assert.False(reloaded.Contains(ids[1]));
        Assert.True(reloaded.Contains(ids[4]));
    }

    [Fact]
    public void ShownLog_CorruptFile_StartsEmptyInsteadOfThrowing()
    {
        File.WriteAllText(_logPath, "{ not json");

        var log = new ShownNotificationLog(_logPath);

        Assert.False(log.Contains(Guid.NewGuid()));
    }

    private sealed class RecordingToast : IGenericNotificationToast
    {
        public List<(GenericNotificationPushPayload Payload, string Arguments)> Shown { get; } = [];
        public bool Fail { get; set; }

        public bool Show(GenericNotificationPushPayload payload, string activationArguments)
        {
            if (Fail)
                return false;
            lock (Shown)
                Shown.Add((payload, activationArguments));
            return true;
        }
    }
}
