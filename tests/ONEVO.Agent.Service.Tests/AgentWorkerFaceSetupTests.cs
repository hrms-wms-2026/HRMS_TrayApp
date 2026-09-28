namespace ONEVO.Agent.Service.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Biometrics;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.Lifecycle;
using ONEVO.Agent.Service.Policy;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Service.Tests.Security;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using Xunit;

// See CredentialStoreFileCollection's doc comment: CredentialStore/DeviceIdentityStore read/write
// real shared files, so every test class touching them must serialize against the others.
[Collection(CredentialStoreFileCollection.Name)]
public class AgentWorkerFaceSetupTests : IDisposable
{
    private readonly List<string> _enrollBodies = [];
    private readonly List<string> _previewBodies = [];
    private bool _previewPasses = true;
    private object _referenceStatusJson = new { enrolled = false, reference_photo_count = 0 };

    public void Dispose() => new CredentialStore().ClearDeviceJwt();

    private AgentWorker BuildWorker(FaceSetupPhotoStaging staging)
    {
        var credentials = new CredentialStore();
        credentials.StoreDeviceJwt("test-device-jwt");

        var stateMachine = new AgentStateMachine();
        stateMachine.TryTransition(MonitoringState.Stopped, out _);

        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == AgentApiRoutes.FaceReference)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(_referenceStatusJson) };
            }

            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (request.RequestUri!.AbsolutePath == AgentApiRoutes.FaceEnroll)
            {
                _enrollBodies.Add(body);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { enrolled = true, failed_photo = (string?)null, failure_reason = (string?)null })
                };
            }

            _previewBodies.Add(body);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    lighting_ok = true, face_visible = true, no_sunglasses_or_mask = true,
                    is_match = false, can_proceed = _previewPasses, similarity_score = (float?)null,
                    failure_reason = _previewPasses ? null : "wrong_pose"
                })
            };
        });

        var worker = new AgentWorker(
            NullLogger<AgentWorker>.Instance,
            null!, // NamedPipeServer — not touched by these handlers
            stateMachine,
            new PolicyCache(),
            ActivityRecordBuffer.CreateInMemory(),
            new PresenceSession(),
            new LifecycleGate(),
            Options.Create(new AgentOptions()),
            new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance),
            credentials,
            new DeviceIdentityStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            null!, // EnrollmentCoordinator — not touched
            null!, // InactivityEvidenceHandler — not touched
            null!, // EvidenceSpoolStore — not touched
            new PendingLegalChallengeStore(),
            faceSetupStaging: staging);
        worker.ApplyEnrollmentGates();
        return worker;
    }

    private static IpcEnvelope Validate(Guid session, string pose, byte marker) => new()
    {
        Type = IpcMessageTypes.FacePhotoValidate,
        CorrelationId = Guid.NewGuid().ToString("N"),
        Payload = JsonSerializer.SerializeToElement(new FacePhotoValidatePayload(
            "jpeg", Convert.ToBase64String([0xFF, 0xD8, marker]),
            FacePhotoValidatePurposes.Enrollment, pose, session))
    };

    private static IpcEnvelope Commit(Guid session) => new()
    {
        Type = IpcMessageTypes.FaceEnrollCommit,
        CorrelationId = "commit-1",
        Payload = JsonSerializer.SerializeToElement(new FaceEnrollCommitPayload(session))
    };

    private static async Task<FaceEnrollCommitResultPayload?> CommitAsync(AgentWorker worker, Guid session)
    {
        FaceEnrollCommitResultPayload? result = null;
        await worker.HandleFaceEnrollCommitAsync(Commit(session), reply =>
        {
            result = reply.Payload!.Value.Deserialize<FaceEnrollCommitResultPayload>();
            return Task.CompletedTask;
        });
        return result;
    }

    private static Task ValidateAsync(AgentWorker worker, Guid session, string pose, byte marker) =>
        worker.HandleFacePhotoValidateAsync(Validate(session, pose, marker), _ => Task.CompletedTask);

    [Fact]
    public async Task ThreePassingSteps_ThenCommit_SendsAllThreePhotosAndEnrolls()
    {
        var staging = new FaceSetupPhotoStaging();
        var worker = BuildWorker(staging);
        var session = Guid.NewGuid();

        await ValidateAsync(worker, session, FaceSetupPoses.Front, 1);
        await ValidateAsync(worker, session, FaceSetupPoses.Left, 2);
        await ValidateAsync(worker, session, FaceSetupPoses.Right, 3);
        var result = await CommitAsync(worker, session);

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.True(result.Enrolled);
        var body = Assert.Single(_enrollBodies);
        Assert.Contains("name=front", body);
        Assert.Contains("name=left", body);
        Assert.Contains("name=right", body);
        Assert.All(_previewBodies, b => Assert.Contains("name=pose", b));
        // Committed photos are not reused by a later commit.
        Assert.Null(staging.TryGetComplete(session));
    }

    [Fact]
    public async Task Commit_WithMissingPhoto_DoesNotCallBackend()
    {
        var worker = BuildWorker(new FaceSetupPhotoStaging());
        var session = Guid.NewGuid();
        await ValidateAsync(worker, session, FaceSetupPoses.Front, 1);
        await ValidateAsync(worker, session, FaceSetupPoses.Left, 2);

        var result = await CommitAsync(worker, session);

        Assert.False(result!.Success);
        Assert.Equal("PHOTOS_MISSING", result.ErrorCode);
        Assert.Empty(_enrollBodies);
    }

    [Fact]
    public async Task FailedStep_IsNotStaged()
    {
        var worker = BuildWorker(new FaceSetupPhotoStaging());
        var session = Guid.NewGuid();
        await ValidateAsync(worker, session, FaceSetupPoses.Front, 1);
        await ValidateAsync(worker, session, FaceSetupPoses.Left, 2);
        _previewPasses = false;
        await ValidateAsync(worker, session, FaceSetupPoses.Right, 3);

        var result = await CommitAsync(worker, session);

        Assert.Equal("PHOTOS_MISSING", result!.ErrorCode);
        Assert.Empty(_enrollBodies);
    }

    [Fact]
    public async Task FaceReferenceStatus_RelaysBackendAnswer()
    {
        var worker = BuildWorker(new FaceSetupPhotoStaging());
        _referenceStatusJson = new { enrolled = true, reference_photo_count = 3 };

        FaceReferenceStatusResultPayload? result = null;
        await worker.HandleFaceReferenceStatusAsync(
            new IpcEnvelope { Type = IpcMessageTypes.FaceReferenceStatus, CorrelationId = "r" },
            reply =>
            {
                result = reply.Payload!.Value.Deserialize<FaceReferenceStatusResultPayload>();
                return Task.CompletedTask;
            });

        Assert.True(result!.Success);
        Assert.True(result.Enrolled);
        Assert.Equal(3, result.ReferencePhotoCount);
    }

    [Fact]
    public async Task ClockInValidate_NeverStagesAnything()
    {
        var staging = new FaceSetupPhotoStaging();
        var worker = BuildWorker(staging);
        var session = Guid.NewGuid();
        var envelope = new IpcEnvelope
        {
            Type = IpcMessageTypes.FacePhotoValidate,
            CorrelationId = "c",
            Payload = JsonSerializer.SerializeToElement(new FacePhotoValidatePayload(
                "jpeg", Convert.ToBase64String([0xFF, 0xD8, 0x01]),
                FacePhotoValidatePurposes.ClockIn, FaceSetupPoses.Front, session))
        };

        await worker.HandleFacePhotoValidateAsync(envelope, _ => Task.CompletedTask);

        Assert.Null(staging.TryGetComplete(session));
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler) { BaseAddress = new Uri("https://api.example.com/") };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
