namespace ONEVO.Agent.Service.IPC;

using ONEVO.Agent.Shared.IPC;

/// <summary>
/// Decides which inbound IPC messages <see cref="NamedPipeServer"/> may handle off its read loop
/// (AUD-008, UAT-17).
///
/// Each connection's messages used to be handled strictly one at a time, so a request waiting on a
/// slow backend call (daily tasks, location change, consent, update check, ...) held back every
/// message behind it, including a clock action whose reply the tray only waits
/// <c>Constants.LifecycleReplyTimeout</c> for.
///
/// A message type listed here is a request/reply (or ack) whose handler only reads the device
/// credential, calls the backend and replies by correlation id. It changes no Service-local state,
/// so its order relative to other messages does not matter. Everything else (lifecycle commands,
/// collection submits, evidence and screenshot chunk streams, activation, pairing, logout, face
/// setup staging, legal acceptance) stays on the read loop, in arrival order.
/// </summary>
internal static class IpcDispatch
{
    /// <summary>
    /// Independent requests one connection may have in flight at once. When all slots are busy the
    /// read loop waits for one to free up, so a misbehaving client cannot start unbounded work.
    /// </summary>
    internal const int MaxConcurrentRequestsPerConnection = 4;

    private static readonly HashSet<string> IndependentRequests = new(StringComparer.Ordinal)
    {
        IpcMessageTypes.DailyTasksRequest,
        IpcMessageTypes.LocationChangeSubmit,
        IpcMessageTypes.LocationChangePendingCheck,
        IpcMessageTypes.LocationChangeRespond,
        IpcMessageTypes.WorkLocationConfirm,
        IpcMessageTypes.FaceReferenceStatus,
        IpcMessageTypes.PermissionConsentStatus,
        IpcMessageTypes.PermissionConsentSubmit,
        IpcMessageTypes.GenericNotificationAccepted,
        IpcMessageTypes.UpdateCheckRequest,
    };

    /// <summary>
    /// How long the Service waits on the backend for a read-only request (UAT-17 part 2): the tray's
    /// own wait for that reply (<c>NamedPipeClient</c>), after which nobody reads the answer. Without
    /// it a hung call held a dispatch slot through every retry (~2.5–3 min).
    ///
    /// Only read-only calls get a budget. Cancelling a call that writes (activation, consent submit,
    /// face enrolment, legal acceptance, ...) can leave the backend committed while the Service
    /// throws the result away, so those keep waiting until a decision on how to recover them.
    /// </summary>
    private static readonly Dictionary<string, TimeSpan> ReadOnlyBudgets = new(StringComparer.Ordinal)
    {
        [IpcMessageTypes.DailyTasksRequest] = TimeSpan.FromSeconds(10),
        [IpcMessageTypes.LocationChangePendingCheck] = TimeSpan.FromSeconds(10),
        [IpcMessageTypes.FaceReferenceStatus] = TimeSpan.FromSeconds(15),
        [IpcMessageTypes.PermissionConsentStatus] = TimeSpan.FromSeconds(8),
        [IpcMessageTypes.UpdateCheckRequest] = TimeSpan.FromSeconds(20),
        // No reply is awaited; an ack that is not confirmed is re-offered on the next poll.
        [IpcMessageTypes.GenericNotificationAccepted] = TimeSpan.FromSeconds(30),
    };

    internal static bool RunsConcurrently(string? messageType) =>
        messageType is not null && IndependentRequests.Contains(messageType);

    /// <summary>The budget for <paramref name="messageType"/>; infinite for a type without one.</summary>
    internal static TimeSpan ReadOnlyBudget(string messageType) =>
        ReadOnlyBudgets.TryGetValue(messageType, out var budget) ? budget : Timeout.InfiniteTimeSpan;
}
