# WorkPulse Agent — Complete Package Guide
**Module Coverage:** Agent Gateway + Activity Monitoring  
**Reference Files:** `__Agent_Registration_-_End-to-End_L.txt` · `__Application_Tracking___End-to-End.txt` · `analysis_report.md`  
**Stack:** .NET 9 · ASP.NET Core · MAUI · PostgreSQL · MSIX

---

## How to Read This Document

Every package section follows this structure:
1. **என்னன்னா (What is it)** — ஒரு line-ல
2. **உன் system-ல எங்க use ஆகுது** — your exact files-ல mention ஆன places
3. **இதை use பண்ணலாமா? Better option இருக்கா?** — honest verdict
4. **Install பண்றது எப்படி** — exact command
5. **உன் code-ல எப்படி போடுவாங்க** — real implementation snippet
6. **Download / distribute பண்றது எப்படி** — where applicable

---

## Part 1 — Backend Packages (Agent Gateway + Activity Monitoring)

---

### 1. SignalR (`Microsoft.AspNetCore.SignalR`)

**என்னன்னா:** Server → Agent-க்கு real-time commands push பண்ண use ஆகுது. REST API-மாதிரி client request பண்ண வேண்டாம் — server directly agent-க்கு command அனுப்பலாம்.

**உன் system-ல எங்க:**
- `agent-gateway/remote-commands` module — `StartMonitoring`, `StopMonitoring`, `PauseMonitoring`, `ResumeMonitoring`, `RefreshPolicy`, `ExecuteCommand` commands push பண்ண
- Agent registers பண்ணிட்டு SignalR connection maintain பண்ணும்
- Heartbeat fallback: agent SignalR கண்ணெக்ட் ஆகல்ன்னா `GET /api/v1/agent/commands` polling use பண்ணும்

**இதை use பண்ணலாமா?**

✅ **Yes — இதுவே best choice.** Microsoft-ஓட first-party package, ASP.NET Core-ல built-in, WebSocket + SSE + Long Polling எல்லாத்தையும் auto-fallback பண்ணும். `agent_commands` table-ல pending commands store பண்றதால் SignalR down ஆனாலும் REST fallback work ஆகும் — உன் design perfect.

**Alternative:** gRPC streaming — better performance ஆனா Windows agent-ல client complexity அதிகம். SignalR தான் சரி.

**Install:**

```bash
# Backend (ASP.NET Core) — built-in, no separate install needed
# Add to Program.cs only

# Desktop Agent (client)
dotnet add package Microsoft.AspNetCore.SignalR.Client
```

**Backend Hub (உன் remote-commands module):**

```csharp
// Hubs/AgentCommandHub.cs
public class AgentCommandHub : Hub
{
    // Agent connects with device JWT → join tenant group
    public override async Task OnConnectedAsync()
    {
        var deviceId = Context.User!.FindFirst("device_id")!.Value;
        var tenantId = Context.User!.FindFirst("tenant_id")!.Value;
        
        // Each agent joins its own group by device_id
        await Groups.AddToGroupAsync(Context.ConnectionId, $"agent:{deviceId}");
        await base.OnConnectedAsync();
    }
}

// Program.cs
builder.Services.AddSignalR();
app.MapHub<AgentCommandHub>("/hubs/agent-commands");

// Sending a command FROM server → agent
public class MonitoringLifecycleService
{
    private readonly IHubContext<AgentCommandHub> _hub;

    // Called when employee clocks in (PresenceSessionStarted event)
    public async Task SendStartMonitoringAsync(Guid deviceId, Guid sessionId)
    {
        await _hub.Clients
            .Group($"agent:{deviceId}")
            .SendAsync("StartMonitoring", new { SessionId = sessionId });
    }
    
    public async Task SendPauseMonitoringAsync(Guid deviceId, string reason)
    {
        await _hub.Clients
            .Group($"agent:{deviceId}")
            .SendAsync("PauseMonitoring", new { Reason = reason });
    }
}
```

**Desktop Agent (Windows Service — client side):**

```csharp
// SignalR client in ONEVO.Agent.Service
public class AgentCommandListener : BackgroundService
{
    private HubConnection _connection = null!;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl("https://agent.onevo.app/hubs/agent-commands", opts =>
            {
                // Attach device JWT from DPAPI store
                opts.AccessTokenProvider = () => Task.FromResult(
                    _tokenStore.GetDeviceToken())!;
            })
            .WithAutomaticReconnect()   // Auto-reconnect on network drop
            .Build();

        // Listen for server commands
        _connection.On<StartMonitoringPayload>("StartMonitoring", payload =>
        {
            _collectors.StartAll(payload.SessionId);
        });

        _connection.On<PauseMonitoringPayload>("PauseMonitoring", payload =>
        {
            _collectors.PauseAll(); // GDPR: zero collection during break
        });

        await _connection.StartAsync(ct);
    }
}
```

---

### 2. Hangfire (`Hangfire` + `Hangfire.AspNetCore` + `Hangfire.PostgreSql`)

**என்னன்னா:** Background jobs schedule பண்ண use ஆகுது — cron-style recurring jobs, retry, dashboard.

**உன் system-ல எங்க:**
- `daily-aggregation` module: `AggregateDailySummaryJob` — every 30 min
- `raw-data-processing` module: `ProcessRawBufferJob` — every 2 min
- `heartbeat-monitoring` module: `DetectOfflineAgentsJob` — every 5 min
- `agent-registration` module: `CleanupRevokedAgentsJob` — daily 3 AM

**இதை use பண்ணலாமா?**

✅ **Yes — உன் use-case-க்கு perfect.** Dashboard built-in, retry automatic, PostgreSQL storage support, .NET 9 compatible. Alternatives like Quartz.NET are more complex. For your scale (monitoring jobs, not millions of queue messages), Hangfire is ideal.

**⚠️ Important:** Hangfire-ஓட free version-ல one server மட்டும். Multiple servers-ல deploy பண்ணணும்னா `Hangfire.Pro` (paid) தேவை. உன் current phase-க்கு free போதும்.

**Install:**

```bash
dotnet add package Hangfire.AspNetCore
dotnet add package Hangfire.PostgreSql
```

**Setup (Program.cs):**

```csharp
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(connectionString, new PostgreSqlStorageOptions
    {
        QueuePollInterval = TimeSpan.FromSeconds(15),
        InvisibilityTimeout = TimeSpan.FromMinutes(30),
        DistributedLockTimeout = TimeSpan.FromMinutes(10),
    }));

builder.Services.AddHangfireServer(opts =>
{
    // Separate queues by priority — critical jobs don't wait behind slow jobs
    opts.Queues = new[] { "critical", "default", "low" };
    opts.WorkerCount = 4;
});

// Dashboard (internal only — never expose publicly)
app.MapHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAdminAuthFilter() }
});
```

**Registering Jobs (உன் exact jobs):**

```csharp
// In a IHostedService or startup class
public class JobScheduler : IHostedService
{
    private readonly IRecurringJobManager _jobs;

    public Task StartAsync(CancellationToken ct)
    {
        // ProcessRawBufferJob — every 2 min (உன் critical path)
        _jobs.AddOrUpdate<ProcessRawBufferJob>(
            "process-raw-buffer",
            job => job.ExecuteAsync(CancellationToken.None),
            "*/2 * * * *",        // every 2 minutes
            new RecurringJobOptions { QueueName = "critical" });

        // DetectOfflineAgentsJob — every 5 min
        _jobs.AddOrUpdate<DetectOfflineAgentsJob>(
            "detect-offline-agents",
            job => job.ExecuteAsync(CancellationToken.None),
            "*/5 * * * *",
            new RecurringJobOptions { QueueName = "critical" });

        // AggregateDailySummaryJob — every 30 min
        _jobs.AddOrUpdate<AggregateDailySummaryJob>(
            "aggregate-daily-summary",
            job => job.ExecuteAsync(CancellationToken.None),
            "*/30 * * * *",
            new RecurringJobOptions { QueueName = "default" });

        // CleanupRevokedAgentsJob — every day at 3 AM
        _jobs.AddOrUpdate<CleanupRevokedAgentsJob>(
            "cleanup-revoked-agents",
            job => job.ExecuteAsync(CancellationToken.None),
            "0 3 * * *",
            new RecurringJobOptions { QueueName = "low" });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

**ProcessRawBufferJob — உன் core job:**

```csharp
public class ProcessRawBufferJob
{
    public async Task ExecuteAsync(CancellationToken ct)
    {
        // 1. Pull unprocessed rows from activity_raw_buffer
        var batch = await _rawBuffer.GetUnprocessedBatchAsync(limit: 500, ct);
        if (!batch.Any()) return;

        foreach (var row in batch)
        {
            // 2. Validate payload schema
            // 3. Check device has active agent_sessions row
            // 4. Check monitoring toggle is enabled for this employee
            // 5. Route by data_type
            switch (row.DataType)
            {
                case "snapshot":
                    await _snapshotService.UpsertAsync(row, ct);
                    break;
                case "app_usage":
                    await _appUsageService.UpsertAsync(row, ct);
                    break;
                case "meeting":
                    await _meetingService.UpsertAsync(row, ct);
                    break;
            }
        }

        // 6. Mark batch as processed
        await _rawBuffer.MarkProcessedAsync(batch.Select(r => r.Id), ct);
    }
}
```

---

### 3. MediatR (`MediatR`)

**என்னன்னா:** Module-க்குள்ளே commands/queries/events route பண்ண use ஆகுது. Controller → Service directly call பண்றதை avoid பண்ணி, clean architecture maintain பண்ண.

**உன் system-ல எங்க:**
- `AgentController.StartEnrollment(StartAgentEnrollmentCommand)` — உன் registration file-ல exact mention
- `AgentController.CompleteEnrollment(CompleteAgentEnrollmentCommand)`
- `AgentRegistered`, `AgentSessionStarted`, `AgentHeartbeatLost` events publish பண்ண
- Cross-module notifications: registration → policy push, lifecycle → monitoring start

**இதை use பண்ணலாமா?**

✅ **Yes — உன் Clean Architecture-க்கு essential.** Controller thin ஆ இருக்கும், handler-ல business logic இருக்கும், events loose-coupled ஆ இருக்கும். Alternative (direct service calls) lead to tightly coupled modules — உன் multi-module system-க்கு bad.

**Install:**

```bash
dotnet add package MediatR
```

**Setup:**

```csharp
// Program.cs
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));
```

**உன் exact enrollment flow:**

```csharp
// Command definition
public record StartAgentEnrollmentCommand(
    Guid DeviceId,
    string DeviceName,
    string OsVersion,
    string AgentVersion
) : IRequest<Result<EnrollmentStartDto>>;

// Controller — thin, no business logic
[ApiController]
[Route("api/v1/agent")]
public class AgentController : ControllerBase
{
    private readonly ISender _sender;

    [HttpPost("enroll/start")]
    public async Task<IActionResult> StartEnrollment(
        [FromBody] StartEnrollmentRequest req,
        CancellationToken ct)
    {
        // Validation: device_id, device_name, os_version, agent_version required
        // (as per your Agent Registration file)
        var command = new StartAgentEnrollmentCommand(
            req.DeviceId, req.DeviceName, req.OsVersion, req.AgentVersion);

        var result = await _sender.Send(command, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("enroll/complete")]
    public async Task<IActionResult> CompleteEnrollment(
        [FromBody] CompleteEnrollmentRequest req,
        CancellationToken ct)
    {
        var command = new CompleteAgentEnrollmentCommand(
            req.EnrollmentId, req.DeviceId, req.AuthorizationCode);
        var result = await _sender.Send(command, ct);
        // Returns AgentEnrollmentDto { DeviceCredential, AgentId, Employee, Policy }
        return result.IsSuccess ? Ok(result.Value) : Unauthorized(result.Error);
    }
}

// Handler — all business logic here
public class CompleteAgentEnrollmentHandler
    : IRequestHandler<CompleteAgentEnrollmentCommand, Result<AgentEnrollmentDto>>
{
    public async Task<Result<AgentEnrollmentDto>> Handle(
        CompleteAgentEnrollmentCommand cmd, CancellationToken ct)
    {
        // 1. Resolve tenant_id and employee_id from authenticated user
        // 2. Check registered_agents by tenant_id + device_id
        var existing = await _repo.GetByDeviceIdAsync(cmd.DeviceId, ct);
        if (existing?.Status == AgentStatus.Revoked)
            return Result.Failure<AgentEnrollmentDto>("Device was revoked");

        // 3. Upsert registered_agents
        // 4. End previous active agent_sessions, INSERT new with is_active=true
        // 5. Build initial effective agent_policies
        // 6. Generate device credential — claims: device_id, tenant_id, type="agent"
        var deviceToken = await _tokenService.GenerateDeviceTokenAsync(
            cmd.DeviceId, tenantId, ct);

        // 7. Publish domain events
        await _publisher.Publish(new AgentRegisteredEvent(agentId, tenantId), ct);
        await _publisher.Publish(new AgentSessionStartedEvent(deviceId, employeeId), ct);

        return Result.Success(new AgentEnrollmentDto
        {
            DeviceCredential = deviceToken,
            AgentId = agentId,
            Employee = employeeDto,
            Policy = effectivePolicy
        });
    }
}
```

**Domain Event Handler (cross-module):**

```csharp
// When AgentRegistered fires → Configuration module pushes initial policy
public class PushInitialPolicyOnAgentRegistered
    : INotificationHandler<AgentRegisteredEvent>
{
    public async Task Handle(AgentRegisteredEvent e, CancellationToken ct)
    {
        await _policyService.PushInitialPolicyAsync(e.AgentId, e.TenantId, ct);
    }
}
```

---

### 4. JWT — Device Token (`Microsoft.AspNetCore.Authentication.JwtBearer`)

**என்னன்னா:** Agent-ஓட device credential validate பண்ண use ஆகுது. User JWT இல்ல — device-specific JWT.

**உன் system-ல எங்க:**
- `enroll/complete` → `ITokenService.GenerateDeviceTokenAsync()` — device JWT issue
- Claims: `device_id`, `tenant_id`, `type = "agent"` மட்டும்
- எல்லா Agent Gateway endpoints-லயும் `[Authorize(Policy = "AgentDevice")]`
- `ingest` endpoint: payload-ல employee validate against active `agent_sessions`

**இதை use பண்ணலாமா?**

✅ **Yes — correct approach.** Key design decision: இது user JWT இல்ல. HR data access கிடையாது. `type: "agent"` claim check mandatory — otherwise a user JWT could accidentally authenticate as agent.

**Install:**

```bash
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
```

**Setup:**

```csharp
// Program.cs
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer("AgentScheme", opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "onevo-agent-gateway",
            ValidateAudience = true,
            ValidAudience = "onevo-agent",
            ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(config["AgentJwt:Secret"]!))
        };
    });

// CRITICAL: Must check type = "agent" claim
builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("AgentDevice", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("type", "agent")   // Must not be a user JWT
        .RequireClaim("device_id")
        .RequireClaim("tenant_id"));
});

// Token generation (ITokenService implementation)
public async Task<string> GenerateDeviceTokenAsync(
    Guid deviceId, Guid tenantId, CancellationToken ct)
{
    var claims = new[]
    {
        new Claim("device_id", deviceId.ToString()),
        new Claim("tenant_id", tenantId.ToString()),
        new Claim("type", "agent"),   // NOT "user"
        // No roles, no HR permissions, no employee_id
    };

    var token = new JwtSecurityToken(
        issuer: "onevo-agent-gateway",
        audience: "onevo-agent",
        claims: claims,
        expires: DateTime.UtcNow.AddDays(90),
        signingCredentials: _credentials);

    return new JwtSecurityTokenHandler().WriteToken(token);
}
```

**Ingest validation (agent_sessions check):**

```csharp
// In ProcessRawBufferJob or IngestService
// Payload employee_id must match active agent_sessions for this device_id
var activeSession = await _db.AgentSessions
    .Where(s => s.DeviceId == deviceId && s.IsActive)
    .SingleOrDefaultAsync(ct);

if (activeSession?.EmployeeId != payload.EmployeeId)
    return Result.Failure("Employee mismatch — data rejected");
```

---

### 5. PostgreSQL + pg_partman

**என்னன்னா:** Primary database. pg_partman-ல time-series tables-ஐ monthly/daily partitions-ஆ split பண்ணுது — query speed அதிகமாகும்.

**உன் system-ல எங்க:**
- `activity_raw_buffer` — daily partitions, 48h TTL
- `activity_snapshots` — monthly partitions, 90-day retention
- `activity_daily_summary` — 2-year retention (allows UPDATE unlike snapshots)
- `application_usage` — per employee per day per app
- `registered_agents`, `agent_sessions`, `agent_health_logs`

**Install:**

```bash
# NuGet
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL

# pg_partman — install in PostgreSQL (run as superuser)
CREATE EXTENSION IF NOT EXISTS pg_partman;
```

**Table creation with partitioning:**

```sql
-- activity_raw_buffer: daily partitions, auto-purge after 48h
CREATE TABLE activity_raw_buffer (
    id          UUID        NOT NULL DEFAULT gen_random_uuid(),
    agent_id    UUID        NOT NULL,
    tenant_id   UUID        NOT NULL,
    employee_id UUID,
    data_type   VARCHAR(50) NOT NULL,   -- 'snapshot', 'app_usage', 'meeting'
    payload     JSONB       NOT NULL,
    captured_at TIMESTAMPTZ NOT NULL,
    processed   BOOLEAN     DEFAULT FALSE,
    PRIMARY KEY (id, captured_at)
) PARTITION BY RANGE (captured_at);

-- pg_partman manages partition creation automatically
SELECT partman.create_parent(
    p_parent_table => 'public.activity_raw_buffer',
    p_control => 'captured_at',
    p_interval => '1 day',
    p_premake => 2     -- pre-create 2 days ahead
);

-- activity_snapshots: monthly partitions
CREATE TABLE activity_snapshots (
    id              UUID        NOT NULL DEFAULT gen_random_uuid(),
    agent_id        UUID        NOT NULL,
    employee_id     UUID        NOT NULL,
    tenant_id       UUID        NOT NULL,
    snapshot_time   TIMESTAMPTZ NOT NULL,
    keyboard_events INT         NOT NULL DEFAULT 0,
    mouse_events    INT         NOT NULL DEFAULT 0,
    active_seconds  INT         NOT NULL DEFAULT 0,
    idle_seconds    INT         NOT NULL DEFAULT 0,
    intensity_score SMALLINT    NOT NULL DEFAULT 0,  -- 0-100
    PRIMARY KEY (id, snapshot_time)
) PARTITION BY RANGE (snapshot_time);

SELECT partman.create_parent(
    p_parent_table => 'public.activity_snapshots',
    p_control => 'snapshot_time',
    p_interval => '1 month'
);

-- application_usage (உன் Application Tracking file-ல exact schema)
CREATE TABLE application_usage (
    id                   UUID         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    tenant_id            UUID         NOT NULL,
    employee_id          UUID         NOT NULL,
    date                 DATE         NOT NULL,
    application_name     VARCHAR(255) NOT NULL,
    application_category VARCHAR(100),
    window_title_hash    VARCHAR(64)  NOT NULL,  -- SHA-256, never raw title
    total_seconds        INT          NOT NULL DEFAULT 0,
    is_productive        BOOLEAN,
    UNIQUE (tenant_id, employee_id, date, application_name)
);
```

**EF Core registration:**

```csharp
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(connectionString, npgsql =>
    {
        npgsql.EnableRetryOnFailure(maxRetryCount: 3);
        npgsql.CommandTimeout(30);
    }));
```

---

### 6. SHA-256 Hashing (Built-in `System.Security.Cryptography`)

**என்னன்னா:** Window title-ஐ hash பண்ண — raw title never stored. உன் Application Tracking file: `window_title_hash VARCHAR(64)`.

**இதை use பண்ணலாமா?**

✅ **Yes — correct and built-in.** No external package needed. SHA-256 is one-way — admin can verify a specific title but cannot reverse all titles.

**Implementation (Agent — AppTracker.cs):**

```csharp
// In ONEVO.Agent.Service/Collectors/AppTracker.cs
// Hash window title IMMEDIATELY — never leave the collection thread unprotected

private static string HashWindowTitle(string rawTitle)
{
    var bytes = Encoding.UTF8.GetBytes(rawTitle.ToLowerInvariant().Trim());
    var hash  = SHA256.HashData(bytes);
    return Convert.ToHexString(hash).ToLowerInvariant();  // 64-char hex
}

// Usage
var title = new StringBuilder(512);
GetWindowText(hwnd, title, 512);
var titleHash = HashWindowTitle(title.ToString());
// rawTitle is NEVER stored, never logged, never sent
```

---

## Part 2 — Desktop Agent Packages (Windows Service + MAUI TrayApp)

---

### 7. Microsoft.Extensions.Hosting.WindowsServices

**என்னன்னா:** .NET background service-ஐ Windows Service ஆக run பண்ண. `UseWindowsService()` ஒரே extension method.

**உன் system-ல எங்க:**
- `ONEVO.Agent.Service` — always-on background service
- Even when no user is logged in — runs at Windows startup

**Install:**

```bash
dotnet add package Microsoft.Extensions.Hosting.WindowsServices
```

**Setup (Program.cs):**

```csharp
var builder = Host.CreateApplicationBuilder(args);

// This one line makes it a Windows Service
builder.Services.AddWindowsService(opts =>
{
    opts.ServiceName = "ONEVOAgentService";
});

// Register all collectors as hosted services
builder.Services.AddHostedService<ActivityCollector>();
builder.Services.AddHostedService<HeartbeatService>();
builder.Services.AddHostedService<DataSyncService>();
builder.Services.AddHostedService<PolicySyncService>();
builder.Services.AddHostedService<AgentCommandListener>();   // SignalR
builder.Services.AddHostedService<NamedPipeServer>();        // IPC with TrayApp

var host = builder.Build();
await host.RunAsync();
```

**Service auto-recovery (Windows auto-restart on crash):**

```csharp
// In Package.appxmanifest or set via sc.exe after install
// Restart after 5s, 10s, 30s — resets after 24h
// sc failure "ONEVOAgentService" reset=86400 actions=restart/5000/restart/10000/restart/30000
```

---

### 8. Microsoft.Data.Sqlite (SQLite Local Buffer)

**என்னன்னா:** Agent device-ல local buffer — offline-safe. Network down ஆனாலும் data collect ஆகும், sync ஆகும்.

**உன் system-ல எங்க:**
- `ONEVO.Agent.Service/Buffer/SqliteBuffer.cs`
- Buffer path: `%LOCALAPPDATA%\ONEVO\Agent\agent_buffer.db`
- DPAPI encryption for the file
- 100MB max size, oldest unsent records drop when exceeded

**Install:**

```bash
dotnet add package Microsoft.Data.Sqlite
```

**Full buffer implementation:**

```csharp
public class SqliteBuffer
{
    private readonly string _dbPath;

    public SqliteBuffer(IConfiguration config)
    {
        _dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ONEVO", "Agent", "agent_buffer.db");
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute(@"
            CREATE TABLE IF NOT EXISTS activity_buffer (
                id           TEXT PRIMARY KEY,   -- UUID v7
                data_type    TEXT NOT NULL,       -- 'snapshot', 'app_usage', 'meeting'
                employee_id  TEXT,
                payload      TEXT NOT NULL,       -- JSON
                captured_at  TEXT NOT NULL,       -- ISO 8601 UTC
                sent         INTEGER DEFAULT 0,
                retry_count  INTEGER DEFAULT 0,
                last_error   TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_unsent
                ON activity_buffer(sent, captured_at);
        ");
    }

    public async Task WriteAsync(string dataType, string employeeId, object payload)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO activity_buffer (id, data_type, employee_id, payload, captured_at)
            VALUES (@Id, @DataType, @EmployeeId, @Payload, @CapturedAt)",
            new {
                Id = Guid.CreateVersion7().ToString(),
                DataType = dataType,
                EmployeeId = employeeId,
                Payload = JsonSerializer.Serialize(payload),
                CapturedAt = DateTime.UtcNow.ToString("O")
            });

        await EnforceSizeLimitAsync(conn);
    }

    public async Task<List<BufferRow>> GetUnsentBatchAsync(int maxBatchSize = 50)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        return (await conn.QueryAsync<BufferRow>(@"
            SELECT id, data_type, employee_id, payload, captured_at
            FROM activity_buffer
            WHERE sent = 0 AND retry_count < 10
            ORDER BY captured_at ASC
            LIMIT @Limit",
            new { Limit = maxBatchSize })).ToList();
    }

    public async Task MarkAsSentAsync(IEnumerable<string> ids)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE activity_buffer SET sent = 1 WHERE id IN @Ids",
            new { Ids = ids });
    }

    private async Task EnforceSizeLimitAsync(SqliteConnection conn)
    {
        // Drop oldest unsent records if DB > 100MB
        var sizeBytes = new FileInfo(_dbPath).Length;
        if (sizeBytes > 100 * 1024 * 1024)
        {
            await conn.ExecuteAsync(@"
                DELETE FROM activity_buffer
                WHERE id IN (
                    SELECT id FROM activity_buffer
                    WHERE sent = 0
                    ORDER BY captured_at ASC
                    LIMIT 1000
                );
                VACUUM;");
        }
    }
}
```

---

### 9. Polly (Resilience + Retry)

**என்னன்னா:** HTTP calls fail ஆனா automatically retry பண்ண, circuit breaker open பண்ண, timeout handle பண்ண.

**உன் system-ல எங்க:**
- `DataSyncService` — `POST /api/v1/agent/ingest` retry
- `HeartbeatService` — `POST /api/v1/agent/heartbeat` retry
- `PolicySyncService` — `GET /api/v1/agent/policy` retry
- Server 429 → `Retry-After` header honor பண்ண
- Server 500 → exponential backoff: 1s, 2s, 4s, 8s, max 30s

**Install:**

```bash
dotnet add package Microsoft.Extensions.Http.Resilience
# (Polly v8 — built into .NET Resilience extensions)
```

**Setup:**

```csharp
// In ONEVO.Agent.Service — typed HttpClient with resilience
builder.Services.AddHttpClient<IGatewayClient, GatewayClient>(client =>
{
    client.BaseAddress = new Uri(config["AgentGateway:BaseUrl"]!);
})
.AddResilienceHandler("agent-gateway-pipeline", builder =>
{
    // Retry: 1s → 2s → 4s → 8s (max 30s)
    builder.AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 4,
        BackoffType = DelayBackoffType.Exponential,
        Delay = TimeSpan.FromSeconds(1),
        MaxDelay = TimeSpan.FromSeconds(30),
        UseJitter = true,
        ShouldHandle = args => args.Outcome switch
        {
            { Exception: HttpRequestException } => PredicateResult.True(),
            { Result.StatusCode: HttpStatusCode.TooManyRequests } => PredicateResult.True(),
            { Result.StatusCode: >= HttpStatusCode.InternalServerError } => PredicateResult.True(),
            _ => PredicateResult.False()
        },
        // Honor Retry-After header on 429
        OnRetry = args =>
        {
            if (args.Outcome.Result?.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = args.Outcome.Result.Headers.RetryAfter?.Delta;
                if (retryAfter.HasValue)
                    args.RetryDelay = retryAfter.Value;
            }
            return ValueTask.CompletedTask;
        }
    });

    // Circuit breaker — stop hammering server if repeatedly failing
    builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        SamplingDuration = TimeSpan.FromMinutes(2),
        MinimumThroughput = 5,
        FailureRatio = 0.5,
        BreakDuration = TimeSpan.FromMinutes(1),
    });

    // Timeout per attempt
    builder.AddTimeout(TimeSpan.FromSeconds(30));
});
```

---

### 10. CommunityToolkit.Maui (TrayApp)

**என்னன்னா:** System tray icon, toast notifications, MAUI helpers — `ONEVO.Agent.TrayApp` UI-க்கு.

**உன் system-ல எங்க:**
- Tray icon: green (monitoring active), yellow (idle), red (stopped/error)
- Toast notifications: photo verification result, policy change alerts
- Login window, Status popup, Photo capture window

**Install:**

```bash
dotnet add package CommunityToolkit.Maui
```

**Setup (MauiProgram.cs):**

```csharp
public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();
    builder
        .UseMauiApp<App>()
        .UseMauiCommunityToolkit();

    // Register IPC client (connects to Windows Service via Named Pipe)
    builder.Services.AddSingleton<INamedPipeClient, NamedPipeClient>();
    builder.Services.AddSingleton<ITrayIconService, TrayIconService>();

    return builder.Build();
}
```

**Tray icon state management:**

```csharp
public class TrayIconService : ITrayIconService
{
    // Called when Service sends status_update via IPC
    public void UpdateStatus(AgentStatus status)
    {
        var (iconPath, tooltip) = status switch
        {
            AgentStatus.Monitoring => ("Icons/active.ico", "WorkPulse — Monitoring active"),
            AgentStatus.Idle       => ("Icons/idle.ico",   "WorkPulse — Idle"),
            AgentStatus.Paused     => ("Icons/paused.ico", "WorkPulse — On break"),
            AgentStatus.Stopped    => ("Icons/stopped.ico","WorkPulse — Not monitoring"),
            _                      => ("Icons/error.ico",  "WorkPulse — Error")
        };
        // Update tray icon
    }
}
```

---

### 11. Named Pipes (Built-in `System.IO.Pipes`)

**என்னன்னா:** Windows Service ↔ MAUI TrayApp IPC — இரண்டும் separate processes, pipe-ல communicate பண்ணும்.

**உன் system-ல எங்க:**
- Pipe name: `onevo-agent-ipc`
- TrayApp → Service: `employee_login`, `employee_logout`, `photo_captured`, `get_status`
- Service → TrayApp: `capture_photo`, `status_update`, `policy_updated`, `verification_result`

**No install needed — built into .NET.**

**Service side (NamedPipeServer.cs):**

```csharp
public class NamedPipeServer : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var server = new NamedPipeServerStream(
                "onevo-agent-ipc",
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Message);

            await server.WaitForConnectionAsync(ct);
            _ = HandleClientAsync(server, ct);
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using var reader = new StreamReader(pipe);
        using var writer = new StreamWriter(pipe) { AutoFlush = true };

        while (pipe.IsConnected && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;

            var msg = JsonSerializer.Deserialize<IpcMessage>(line)!;
            var response = msg.Type switch
            {
                "employee_login"   => await HandleLoginAsync(msg, ct),
                "employee_logout"  => await HandleLogoutAsync(msg, ct),
                "photo_captured"   => await HandlePhotoAsync(msg, ct),
                "get_status"       => GetCurrentStatus(),
                _                  => new IpcResponse { Success = false }
            };

            await writer.WriteLineAsync(JsonSerializer.Serialize(response));
        }
    }
}
```

---

### 12. Win32 P/Invoke (Built-in — No Package)

**என்னன்னா:** Low-level Windows API call பண்ண — foreground app detect பண்ண, keyboard/mouse hooks register பண்ண, idle time get பண்ண.

**No install — use `DllImport` or `LibraryImport` directly.**

**App tracking (AppTracker.cs):**

```csharp
public partial class AppTracker : BackgroundService
{
    // Win32 API declarations
    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);

            var hwnd = GetForegroundWindow();
            GetWindowThreadProcessId(hwnd, out var pid);
            var process = Process.GetProcessById((int)pid);

            // Hash title immediately — never store raw
            var titleBuilder = new System.Text.StringBuilder(512);
            GetWindowText(hwnd, titleBuilder, 512);
            var titleHash = HashWindowTitle(titleBuilder.ToString());

            await _buffer.WriteAsync("app_usage", _employeeId, new
            {
                ApplicationName = process.ProcessName,
                WindowTitleHash = titleHash,   // SHA-256 — as per Application Tracking schema
                CapturedAt = DateTime.UtcNow
            });
        }
    }
}
```

**Keyboard/Mouse hook (ActivityCollector.cs):**

```csharp
// WH_KEYBOARD_LL hook — count only, never record key content
[LibraryImport("user32.dll")]
private static partial IntPtr SetWindowsHookEx(
    int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
{
    if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        Interlocked.Increment(ref _keyCount);  // Count only
    return CallNextHookEx(_hookId, nCode, wParam, lParam);
}
```

---

## Part 3 — MSIX Packaging & Distribution

---

### 13. MSIX Bundle (.msixbundle)

**என்னன்னா:** Windows-ல modern app packaging — install, update, uninstall clean.

**Build பண்றது (developer):**

```bash
# Step 1: Restore + Build
dotnet restore
dotnet build --configuration Release

# Step 2: Publish both projects
dotnet publish ONEVO.Agent.Service \
    --runtime win-x64 \
    --configuration Release \
    --self-contained false

dotnet publish ONEVO.Agent.TrayApp \
    --runtime win-x64 \
    --configuration Release \
    --self-contained false

# Step 3: MSIX packaging (Visual Studio)
# Right-click ONEVO.Agent.Installer → Publish → Create App Packages
# Select: Sideloading (for direct distribution) or Store
# Choose EV code signing certificate
# Output: ONEVOAgent_1.0.0_x64.msixbundle
```

**Website download — user-க்கு:**

```html
<!-- Download button on install.onevo.app -->
<a href="https://cdn.onevo.app/agent/latest/ONEVOAgent.msixbundle"
   download="ONEVOAgent.msixbundle">
  Download WorkPulse Agent
</a>
```

```
User downloads .msixbundle
→ Double-click
→ Windows App Installer opens
→ Shows publisher name (your EV cert)
→ Click Install
→ Service + TrayApp installed
→ TrayApp opens automatically (startup task)
→ Employee sees Sign In screen
```

**PowerShell install — IT admin-க்கு:**

```powershell
# Method 1: Already downloaded
Add-AppxPackage -Path "C:\Downloads\ONEVOAgent.msixbundle"

# Method 2: Download then install in one script
$url = "https://cdn.onevo.app/agent/latest/ONEVOAgent.msixbundle"
$out = "$env:TEMP\ONEVOAgent.msixbundle"
Invoke-WebRequest -Uri $url -OutFile $out
Add-AppxPackage -Path $out

# Verify
Get-AppxPackage -Name "com.onevo.workpulse"

# Method 3: Remote deploy to multiple machines
$machines = @("PC001", "PC002", "PC003")
foreach ($m in $machines) {
    Invoke-Command -ComputerName $m -ScriptBlock {
        param($url)
        $out = "$env:TEMP\ONEVOAgent.msixbundle"
        Invoke-WebRequest -Uri $url -OutFile $out
        Add-AppxPackage -Path $out
    } -ArgumentList $url
}
```

**Auto-update via .appinstaller:**

```xml
<!-- ONEVOAgent.appinstaller — hosted on CDN -->
<?xml version="1.0" encoding="utf-8"?>
<AppInstaller Uri="https://cdn.onevo.app/ONEVOAgent.appinstaller"
  Version="1.0.0.0"
  xmlns="http://schemas.microsoft.com/appx/appinstaller/2017/2">
  <MainBundle
    Name="com.onevo.workpulse"
    Publisher="CN=ONEVO Pvt Ltd"
    Version="1.0.0.0"
    Uri="https://cdn.onevo.app/agent/ONEVOAgent_1.0.0_x64.msixbundle" />
  <UpdateSettings>
    <!-- Check every 12 hours, silent — no user prompt -->
    <OnLaunch HoursBetweenUpdateChecks="12" ShowPrompt="false"/>
  </UpdateSettings>
</AppInstaller>
```

---

## Part 4 — Phase 2 Packages (Not Yet — Plan Only)

---

### Microsoft Teams Graph API

**என்னன்னா:** Phase 1-ல Teams process name மட்டும் detect பண்றோம். Phase 2-ல actual meeting data (participant count, duration) Graph API-லயும் pull பண்ணலாம்.

**When:** Phase 2 planning-ல மட்டும். Phase 1-க்கு process name detection போதும்.

```bash
dotnet add package Microsoft.Graph  # Phase 2 only
```

---

### Azure AD App Registration

**என்னன்னா:** Graph API access-க்கு Azure AD-ல app register பண்ணணும். SSO enrollment-க்கு already use ஆகுது.

**Phase 2 additional scope:** `OnlineMeetings.Read.All` permission.

---

### macOS `.pkg` Installer

**என்னன்னா:** Phase 2-ல macOS agent-க்கு `.pkg` format — MSIX macOS-ல work ஆகாது.

**When:** Phase 2 macOS implementation-ல.

```bash
# macOS pkg creation
pkgbuild --root ./publish --identifier com.onevo.agent --version 1.0.0 ONEVOAgent.pkg
productbuild --distribution Distribution.xml --package-path . ONEVOAgent_installer.pkg
```

---

## Part 5 — Package Comparison — Better Alternatives?

| Package | Currently Using | Better Alternative? | Verdict |
|---|---|---|---|
| **SignalR** | `Microsoft.AspNetCore.SignalR` | gRPC Streaming | ✅ SignalR is correct — simpler client, auto-fallback |
| **Hangfire** | `Hangfire.PostgreSql` | Quartz.NET | ✅ Hangfire is correct — built-in dashboard, simpler |
| **MediatR** | `MediatR` | Direct service calls | ✅ MediatR is correct — Clean Architecture needs it |
| **SQLite** | `Microsoft.Data.Sqlite` | LiteDB, SQLCipher | ✅ Microsoft.Data.Sqlite + DPAPI encryption சரி |
| **Polly** | `Microsoft.Extensions.Http.Resilience` | Custom retry | ✅ Polly correct — standard .NET resilience |
| **JWT** | `Microsoft.AspNetCore.Authentication.JwtBearer` | API Key | ✅ JWT correct — device-specific claims needed |
| **PostgreSQL** | `Npgsql.EFCore.PostgreSQL` | MySQL, SQL Server | ✅ PostgreSQL + pg_partman is the right choice |
| **MSIX** | `.msixbundle` | MSI, EXE | ✅ MSIX is more modern — correct choice |
| **Named Pipes** | `System.IO.Pipes` | gRPC, TCP socket | ✅ Named Pipes correct for same-machine IPC |
| **Win32 P/Invoke** | Built-in | Managed alternatives | ✅ No alternative — must use Win32 for hooks |

**Summary: எல்லா package choice-உம் correct-ஆ இருக்கு.** Better alternatives இருந்தாலும் உன் use-case-க்கு current choices more suitable.

---

## Part 6 — Step-by-Step: எங்கிருந்து start பண்றது?

### Week 1 — Backend Foundation

```bash
# 1. Solution create
dotnet new sln -n ONEVO.Backend

# 2. Projects create
dotnet new webapi -n ONEVO.Api
dotnet new classlib -n ONEVO.Domain
dotnet new classlib -n ONEVO.Application
dotnet new classlib -n ONEVO.Infrastructure

# 3. Add all backend packages
cd ONEVO.Api
dotnet add package MediatR
dotnet add package Hangfire.AspNetCore
dotnet add package Hangfire.PostgreSql
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
```

### Week 2 — Agent Gateway Module

```bash
# Agent registration, heartbeat, policy endpoints
# Enroll/start → Enroll/complete → Device JWT
# registered_agents + agent_sessions tables
# AgentRegistered + AgentSessionStarted events via MediatR
```

### Week 3 — Windows Service

```bash
cd ONEVO.Agent.Service
dotnet add package Microsoft.Extensions.Hosting.WindowsServices
dotnet add package Microsoft.Data.Sqlite
dotnet add package Microsoft.AspNetCore.SignalR.Client
dotnet add package Microsoft.Extensions.Http.Resilience
dotnet add package Serilog
dotnet add package Serilog.Sinks.File
```

### Week 4 — MAUI TrayApp

```bash
dotnet workload install maui
cd ONEVO.Agent.TrayApp
dotnet add package CommunityToolkit.Maui
```

### Week 5 — MSIX Package

```
Visual Studio → ONEVO.Agent.Installer
Package.appxmanifest configure
EV Certificate acquire
Create App Packages → .msixbundle
Upload to CDN
```

---

## Quick Reference — All Install Commands

```bash
# ── BACKEND ───────────────────────────────────────────────
dotnet add package MediatR
dotnet add package Hangfire.AspNetCore
dotnet add package Hangfire.PostgreSql
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL

# ── WINDOWS SERVICE ───────────────────────────────────────
dotnet add package Microsoft.Extensions.Hosting.WindowsServices
dotnet add package Microsoft.Data.Sqlite
dotnet add package Microsoft.AspNetCore.SignalR.Client
dotnet add package Microsoft.Extensions.Http.Resilience
dotnet add package Serilog
dotnet add package Serilog.Sinks.File

# ── MAUI TRAY APP ─────────────────────────────────────────
dotnet workload install maui
dotnet add package CommunityToolkit.Maui

# ── PostgreSQL extension (run in psql) ────────────────────
# CREATE EXTENSION IF NOT EXISTS pg_partman;

# ── PHASE 2 ONLY ──────────────────────────────────────────
# dotnet add package Microsoft.Graph
```

---

*Document generated from: `__Agent_Registration_-_End-to-End_L.txt` · `__Application_Tracking___End-to-End.txt` · `analysis_report.md`*  
*Stack: .NET 9 · ASP.NET Core · MAUI · PostgreSQL · MSIX · Windows*
