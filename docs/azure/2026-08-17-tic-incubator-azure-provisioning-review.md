# TIC-Incubator Azure Environment — Technical Validation

**Date:** 2026-08-17
**Reviewer:** Mayooran
**Status:** Conditionally approved for internal non-production
**Scope:** Unicorn Connected Apps tenant · `TIC-Incubator` resource group · Onexso + OneVerz + WorkPulse tray-agent backend
**Audience:** Product / TIC (to finalize) and Pream / Cloud Team (to provision)

This document validates the proposed Azure setup and includes a ready-to-send provisioning request.

---

## Verdict

The proposed shape is **technically suitable for internal development, integration, testing, and demonstrations**.

| Question | Answer |
|---|---|
| Is a single Linux **Basic B2** App Service Plan enough for four App Services? | **Yes, for a small internal/non-prod load**, if Angular sites are static and both products are not load-tested at the same time. |
| Is sharing one plan between Onexso and OneVerz acceptable? | **Yes for incubator / non-prod.** Not acceptable later for production or isolated compliance. |
| Is resource group `TIC-Incubator` appropriate? | **Yes.** Add region, tags, and a naming convention. |
| Should the database / supporting services change? | **Yes.** Specify **Azure Database for PostgreSQL Flexible Server**, not a generic “database”. Add the supporting services listed below. |
| Anything to include before sending to Pream? | **Yes.** Region, SKUs, names, Entra apps, identity, networking, upload limits, WebSockets, and RBAC. See the provisioning request at the end. |

Do **not** treat this environment as production, customer-data hosting, or a performance/SLA target.

---

## Proposed setup (as received)

```
Unicorn Connected Apps tenant
└── Azure subscription (name to confirm)
    └── Resource group: TIC-Incubator
        ├── asp-tic-incubator-dev          Linux Basic B2 · 2 vCPU · 3.5 GB · 1 instance
        │   ├── Onexso API
        │   ├── Onexso Angular web
        │   ├── OneVerz API
        │   └── OneVerz Angular web
        ├── Onexso database
        └── OneVerz database
```

Cost claim of **USD 25–30 / month for the B2 plan only** is correct. Current public Linux Basic B2 pay-as-you-go is about **USD 25.55 / month**, excluding database, storage, backup, bandwidth, certificates, and monitoring.

---

## 1. Single B2 plan for four App Services

**Approved for internal non-prod**, with the constraints below.

An App Service Plan is a shared VM. All four apps compete for the same **2 vCPU / 3.5 GB RAM / 1 instance**.

| Workload | Typical idle | Notes |
|---|---|---|
| Onexso API (.NET) | 250–450 MB | EF Core + PostgreSQL + file upload paths |
| OneVerz API (.NET) | 250–450 MB | Same class of API |
| Onexso Angular | 60–150 MB | Only if served as **static files**, not `ng serve` / SSR |
| OneVerz Angular | 60–150 MB | Same |
| Platform / Linux overhead | 300–500 MB | Always present |
| **Idle total** | **~1.0–1.8 GB** | Fits B2 |
| Under demo + face-scan / screenshot upload | **2.0–3.2 GB** | Fits if only one product is busy |

### Must-follow rules for B2 to stay viable

1. Deploy Angular as **static content** (nginx / App Service static site). Do not run a Node build server or SSR on this plan.
2. Do not run concurrent load tests of Onexso and OneVerz on this plan.
3. Accept **no Always On** on Basic. Idle APIs unload and the first request (including WorkPulse tray-agent heartbeat / clock-in) can cold-start for 10–30 seconds.
4. Accept **no deployment slots**, **no autoscale**, **no VNet integration** on Basic.
5. Enable **WebSockets** on both APIs. The WorkPulse agent already has a SignalR client (`AgentCommandListener`) and will need a hub later.
6. Raise the HTTP request body limit on the APIs for face-scan and screenshot uploads. Default App Service limits will fail clock-in photo upload.

### When B2 is no longer enough

Move the two APIs to **Standard S1** (Always On, slots, VNet) or split Onexso and OneVerz onto two plans if any of these happen:

- Frequent OOM recycles
- Both products demoed at the same time with uploads
- Need private database networking
- Need staging slots
- Tray-agent heartbeats cannot tolerate cold starts

Do **not** jump to Premium for this incubator environment.

---

## 2. Sharing one plan between Onexso and OneVerz

**Acceptable for `TIC-Incubator`.**

Reasons it is OK now:

- Same tenant, same team, same non-prod purpose
- Cost is one B2 instead of two
- Apps remain separate App Services, so config, logs, and deploy pipelines stay independent
- We can move either product to its own plan later without rewriting the apps

Reasons it is **not** OK for production:

- One noisy app (memory leak, large upload, bad deploy) recycles **all four**
- No network isolation between products
- Shared restart / patch window
- Basic has no SLA suitable for customer use

**Rule:** share compute in incubator; keep **identity, databases, secrets, CORS, and custom domains** strictly separate per product.

---

## 3. Resource group `TIC-Incubator`

**Appropriate** as a single incubator RG.

Recommended additions (Cloud Team should apply these at create time):

| Item | Value |
|---|---|
| Resource group | `TIC-Incubator` |
| Region | **Must be chosen before provisioning.** Prefer the region closest to the TIC / demo users (confirm with Pream). All compute + PostgreSQL must be in the **same region**. |
| Tags | `project=tic-incubator`, `env=dev`, `product=shared`, `owner=tic`, `cost-center=<confirm>` |
| Per-app tags | `product=onexso` or `product=oneverz` on each App Service and database |

Do **not** create a second RG unless Cloud Team already has a policy that one product = one RG. A later split is easy if names and tags are clean.

---

## 4. Database and supporting services — required changes

The original request is too vague here. Architecture for Onexso / ONEVO is **PostgreSQL + private object storage**, not SQL Server and not files in the database.

### 4.1 Database — change from “separate database” to this

**Recommended (cheaper, enough for non-prod):**

- **One** Azure Database for PostgreSQL **Flexible Server**
- SKU: **Burstable B1ms** (1 vCore, 2 GB) to start; raise to **B2s** if migrations / demos are slow
- Version: **PostgreSQL 16**
- Storage: **32 GB**, auto-grow on
- Backup retention: **7 days**
- Two databases on that server:
  - `onexso`
  - `oneverz`
- Two logins, each granted only its own database
- SSL required
- Public access **only** because Basic App Service **cannot** join a VNet
- Firewall: allow Azure services **or** lock to the App Service outbound IPs; deny `0.0.0.0/0`

**Not recommended unless Cloud / security insists:** two separate Flexible Servers. That roughly doubles DB cost for no functional gain in incubator.

**Do not provision:** Azure SQL Database, MySQL, or Cosmos for these two APIs.

Approximate extra cost: Flexible Server Burstable B1ms is typically **USD 12–20 / month** plus storage, on top of the B2 plan.

### 4.2 Supporting services to include in the same request

These are required for Onexso, OneVerz, and the WorkPulse tray app to actually run. They are **not** optional “later” items.

| Service | Why | Incubator recommendation |
|---|---|---|
| **App settings / Key Vault** | Connection strings, JWT signing keys, R2 keys, SMTP | Key Vault `kv-tic-incubator-dev` + App Service **system-assigned managed identity**. If Cloud Team wants to start cheaper, use App Service slot-sticky app settings first, then Key Vault. |
| **Application Insights** | API failures, cold starts, tray-agent ingest errors | One workspace `log-tic-incubator-dev` + two App Insights (`appi-onexso-dev`, `appi-oneverz-dev`) |
| **Cloudflare R2** (existing platform storage) | Face scans, logos, screenshots, documents. Architecture forbids public blob URLs and storing file bytes in PostgreSQL. | Do **not** add Azure Blob as the primary store unless Cloud Team cannot use R2 in this tenant. Confirm R2 credentials are available to the Onexso API. |
| **Entra app registrations** (same Unicorn Connected Apps tenant) | Angular SPAs + API auth + optional Microsoft login | Four app registrations, listed in the request below |
| **Custom domains + TLS** | Tray agent requires `https://` (`ApiBaseUrl` validation rejects non-HTTPS) | Either `*.azurewebsites.net` HTTPS (OK to start) or TIC custom hosts. **HTTPS only** on every App Service. |
| **Email provider** | Invites, password reset | Reuse existing Resend / SendGrid platform keys; do not invent a new SMTP server |
| **GitHub / Azure DevOps identity** | Deploy without passwords | OIDC federated credential or one service principal `sp-tic-incubator-dev` with `Website Contributor` + `Key Vault Secrets User` on this RG only |

### 4.3 Do not add yet

- Azure Front Door / WAF
- Azure SignalR Service (in-process SignalR is enough on 1 instance)
- Redis
- API Management
- Private Endpoints / VNet (blocked by Basic)
- Staging slots (blocked by Basic)
- Production-grade geo-backup

---

## 5. Security, networking, access control, deployment

Include these in the Cloud Team request. They are the items that usually get missed and then block the first deploy.

### Networking

- Basic B2 **cannot** use VNet integration or Private Endpoints. Databases will be public-endpoint + firewall. That is acceptable for **non-prod only**.
- App Services: **HTTPS Only = On**, TLS 1.2+, FTP **disabled**, SCM basic auth **disabled**.
- Enable **WebSockets** on both APIs.
- Configure CORS per product:
  - Onexso API allows only the Onexso Angular origin (and local `https://localhost:*` if needed)
  - OneVerz API allows only the OneVerz Angular origin
- Tray-agent calls are server-to-server from employee PCs to the Onexso API public HTTPS URL. The API must be reachable from the public internet for device check-in. Do not put the API behind a VPN-only rule if the tray app must work off-network.

### Identity and access

- Unicorn Connected Apps tenant is approved and is the correct tenant.
- Create Entra groups, not personal assignments:
  - `grp-tic-incubator-devs` → `Contributor` on `TIC-Incubator`
  - `grp-tic-incubator-readers` → `Reader` on `TIC-Incubator`
  - Cloud Team retain `Owner`
- Each App Service gets a **system-assigned managed identity**.
- No connection strings in source control. No shared SQL admin used by the apps.

### Deployment

Suggested names (Linux, same plan):

| Resource | Name | Runtime |
|---|---|---|
| App Service Plan | `asp-tic-incubator-dev` | Linux Basic B2, 1 instance |
| Onexso API | `app-onexso-api-dev` | .NET (current LTS available on App Service Linux; today that is .NET 8/9/10 — match the repo) |
| Onexso web | `app-onexso-web-dev` | Static / nginx |
| OneVerz API | `app-oneverz-api-dev` | .NET |
| OneVerz web | `app-oneverz-web-dev` | Static / nginx |
| PostgreSQL | `psql-tic-incubator-dev` | Flexible Server 16, Burstable |
| Key Vault | `kv-tic-incubator-dev` | RBAC mode |
| Log Analytics | `log-tic-incubator-dev` | 30-day retention |

Deploy each App Service independently (four pipelines). A bad OneVerz web deploy must not recycle the Onexso API.

Health paths: configure `/health` (or the existing API health endpoint) as the App Service health check once the APIs expose it.

### WorkPulse tray-app impact (this repo)

The agent does not run in Azure. It runs on the employee Windows PC and calls the **Onexso API**.

After provisioning, set:

```json
"Agent": {
  "ApiBaseUrl": "https://app-onexso-api-dev.azurewebsites.net"
}
```

in `ONEVO.Agent.Service` non-dev configuration. `ApiBaseUrl` **must** be HTTPS.

Endpoints that must work on the Onexso API in this environment:

- `/api/v1/monitoring/activation/*`
- `/api/v1/monitoring/check-in` and `/{id}/face-scan`
- `/api/v1/monitoring/work-sessions`
- `/api/v1/monitoring/activity|app-usage|device-state/snapshots`
- `/api/v1/monitoring/tray/*`
- `/api/v1/monitoring/biometrics/enrollment-attempts*`

If the API is idle on Basic, the first clock-in of the day may be slow. That is expected on B2 and should be called out to testers, not treated as an agent bug.

---

## 6. Cost picture (honest)

| Item | Approx. monthly USD | Notes |
|---|---|---|
| Linux App Service Plan B2 | 25–30 | Confirmed |
| PostgreSQL Flexible Server B1ms + 32 GB | 12–25 | Dominant extra cost |
| Application Insights / logs | 0–10 | Low in incubator if sampling is on |
| Bandwidth / TLS | low | |
| Key Vault | ~0–1 | |
| **Likely total** | **~40–70** | Not 25–30 once DB + logs are included |

State this clearly in the request so finance / Cloud Team are not surprised.

---

## 7. What to tell Pream / Cloud Team

**Approved as-is:**

- Tenant: Unicorn Connected Apps
- Resource group: `TIC-Incubator`
- One shared Linux Basic B2 plan `asp-tic-incubator-dev`
- Four App Services on that plan (two APIs + two Angular static sites)
- Separate Onexso and OneVerz databases
- Non-production / incubator use only

**Must add before they provision:**

1. Region
2. PostgreSQL Flexible Server (one server, two databases) — not Azure SQL
3. Resource names in the table above
4. Entra groups + four app registrations
5. HTTPS only, FTP off, SCM basic auth off, WebSockets on for APIs
6. Managed identities + secret store
7. Application Insights
8. Confirmation that Cloudflare R2 (or approved equivalent) is available for face-scan / file bytes
9. Subscription name and who is Owner vs Contributor

**Explicit non-goals for this ticket:**

- Production SLA
- VNet / Private Endpoint
- Deployment slots
- Autoscale
- Separate App Service Plans per product

---

## Ready-to-send provisioning request

Copy from the next heading into the email to Pream / Cloud Team.

---

### Subject: Request to provision TIC-Incubator non-prod Azure environment (Unicorn Connected Apps)

Hi Pream / Cloud Team,

Please provision the internal **non-production** environment below. This has been technically reviewed and is approved for incubator use (development, integration, testing, demonstrations only). It is **not** a production design.

**Tenant:** Unicorn Connected Apps (already approved for this purpose)

**Subscription:** _please confirm the subscription name / ID to use_

**Region:** _please confirm; all resources must be in the same region_

**Resource group:** `TIC-Incubator`

**Tags on the resource group and all resources:**

- `project=tic-incubator`
- `env=dev`
- `owner=tic`

#### Compute

| Resource | Name | SKU / notes |
|---|---|---|
| App Service Plan | `asp-tic-incubator-dev` | Linux, **Basic B2**, 2 vCPU, 3.5 GB, **1 instance** |
| Onexso API | `app-onexso-api-dev` | .NET on the shared plan. HTTPS only. WebSockets **On**. FTP **Off**. SCM basic auth **Off**. |
| Onexso Angular | `app-onexso-web-dev` | Static site / nginx on the shared plan. HTTPS only. |
| OneVerz API | `app-oneverz-api-dev` | Same as Onexso API. |
| OneVerz Angular | `app-oneverz-web-dev` | Same as Onexso web. |

One shared B2 plan for all four apps is intentional. Please do **not** create four plans.

Please assign each App Service a **system-assigned managed identity**.

#### Database

Please provision **Azure Database for PostgreSQL Flexible Server** (not Azure SQL, not MySQL):

| Item | Value |
|---|---|
| Server name | `psql-tic-incubator-dev` |
| Version | PostgreSQL 16 |
| SKU | Burstable **B1ms** (upgrade path to B2s if needed) |
| Storage | 32 GB, auto-grow on |
| Backup | 7 days |
| Databases | `onexso`, `oneverz` |
| Logins | one login per database, no shared superuser for the apps |
| TLS | required |
| Network | public endpoint + firewall; allow the App Services only (Basic plan cannot use VNet / Private Endpoint) |

#### Supporting resources

- Key Vault `kv-tic-incubator-dev` (RBAC), with App Service identities granted **Key Vault Secrets User**
- Log Analytics `log-tic-incubator-dev` (30-day retention)
- Application Insights `appi-onexso-dev` and `appi-oneverz-dev`
- Please confirm how we should store **Cloudflare R2** credentials for Onexso file / face-scan uploads (R2 is the approved object store; we should not put file bytes in PostgreSQL)

#### Entra ID (same tenant)

Please create:

1. Security group `grp-tic-incubator-devs` → **Contributor** on `TIC-Incubator`
2. Security group `grp-tic-incubator-readers` → **Reader** on `TIC-Incubator`
3. App registrations:
   - `sp-onexso-api-dev`
   - `spa-onexso-web-dev` (SPA redirect = Onexso web URL)
   - `sp-oneverz-api-dev`
   - `spa-oneverz-web-dev` (SPA redirect = OneVerz web URL)
4. A deploy identity (OIDC or service principal) with rights **only** on this resource group

Cloud Team remain Owner of the subscription / RG.

#### Out of scope for this request

VNet, Private Endpoints, Front Door, API Management, Redis, Azure SignalR, staging slots, autoscale, production backup / DR.

#### Cost expectation

- App Service Plan B2: ~USD 25–30 / month
- PostgreSQL + logs + Key Vault: typically another USD 15–40 / month
- Please confirm the subscription charge-back / cost-center tag if you need one

Once the App Services exist, please send us the four default hostnames, the PostgreSQL FQDN, and the Key Vault URI.

Thank you.
---
