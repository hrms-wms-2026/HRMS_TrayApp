# 14 — Final UAT Readiness Gap Audit (`develop` @ `9a13436`)

**Date:** 2026-10-30 · **Scope:** AUD-001…AUD-036, AGT-01/02/03, SEC-01, T10, T15, UAT-01…UAT-14, plus the R01…R11 assignment.
**Method:** read-only audit. No product code was changed. Every status below was re-checked against `origin/develop` (`9a13436`) and, for contract items, against `HRMS-Backend-v1` `origin/development` (`8ce94e8`).

## A. Source-of-truth limitation (read first)

- The assignment names `tray-uat-audit-2026-10-27.zip` (`04-Issue-Register.md`, `05-Remaining-Work-Plan.md`, `10-Remaining-Manual-Testing-Checklist.md`) as the source of truth. **That ZIP is not in this repository or in this environment.** The register that is checked in is `03-issue-register.md`, and the README already notes that the AUD series' own register is not stored here.
- Consequence: for each AUD ID, this audit uses (1) the commit history and code on `develop`, and (2) the descriptions in the R01…R11 assignment. **AUD-027 and AUD-028 have no commit, code reference or assignment text**, so their title and status cannot be verified here (§C, marked *Unverifiable*). They are not marked completed. AUD-020 and AUD-034 were identified from commit bodies (`684af6f`, `b1b1072`) once the full history was fetched; AUD-016 and AUD-026 are known only as "security/legal decisions" from the assignment.
- The first pass ran on a shallow clone. All history checks in this document were re-run on the full history (2026-10-30).
- **Action (Human):** commit the ZIP's `04-Issue-Register.md` into `docs/uat/` (or attach it to the session) so the three unverifiable rows can be closed and AUD titles reconciled.

## B. Release-branch verification

| Check | Result | Evidence |
|---|---|---|
| Latest `develop` | `9a13436` (merge of PR #101), 2026-10-30 | `git log origin/develop` |
| Baseline `42592eb` (PR #98) contained | Yes | ancestor of `9a13436` |
| Open PRs in `HRMS_TrayApp` | **None** | GitHub PR list, 2026-10-30 |
| AUD-036 PR (#99) | **Already merged** (`a05e7d9`, CI run 197 green). R05 is done; nothing to merge | `b1b1072` |
| PR #100 (UAT-01, UAT-13), PR #101 (UAT-14) | Merged | `ae33883`, `9a13436` |
| CI on `develop` head | **Success** — run 220 | <https://github.com/hrms-wms-2026/HRMS_TrayApp/actions/runs/40999925848> |
| Every AUD/AGT fix commit on `develop` | Yes. Branches `fix/aud-018-*` have 0 commits not on `develop` (`git cherry`) | §C |
| Release branch / tag | **None.** No `release/*` branch (only `feature/tray-release-management`, 0 commits ahead of `develop`), no tags, no GitHub release. `main` is still `5cca93e` (2026-08-05), 340 commits behind `develop` (UAT-05) | `git ls-remote`, GitHub tags API |
| Version | `ApplicationDisplayVersion 1.0.0`, manifest `Version="1.0.0.0"`; the release workflow overrides it from a `vX.Y.Z` tag, but no tag exists | `ONEVO.Agent.TrayApp.csproj`, `release.yml` |

"Latest release branch" therefore means `develop` @ `9a13436`. A release candidate does not exist yet (R10).

## C. AUD-001…AUD-036 status matrix

Status vocabulary: **Fixed – verified** (merged + CI green + regression test) · **Fixed – device check pending** · **Partial** · **Open** · **Escalated** (needs a decision) · **Unverifiable** (no source here).
"AI" = an AI coding agent can complete the remaining work. "Human" = decision, device test or approval needed.

| ID | P¹ | Title (from commits / assignment) | Merged in | Status on `9a13436` | Remaining work | AI | Human / manual |
|---|---|---|---|---|---|---|---|
| AUD-001 | — | Day counted as idle after a Service restart | PR #69 `4a0677f` | Fixed – device check pending | None in code | — | 11 §C reboot mid-session |
| AUD-002 | — | Offline backlog flushed with no valid server policy | PR #68 `24c83a1` | Fixed – verified | None | — | — |
| AUD-003 | — | Device credential readable by users (data-folder ACL) | PR #73 `ba92c8b` | Fixed – device check pending | None in code; see UAT-09 | — | 11 §F |
| AUD-004 | — | Queued rows uploaded under another employee's token | PR #72 `0fd709c` | Fixed – device check pending | None | — | 11 §D re-enrol |
| AUD-005 | — | Tray not Offline / actions not blocked when Service unreachable | PR #76 `ce41265` | Fixed – device check pending | None | — | 11 §A Service restart |
| AUD-006 | — | Single instance, one break alert/day, double dispose | PR #76 `7f442c0` | Fixed – device check pending | None | — | 11 §A second instance |
| **AUD-007** | **P0** | Backend API contract alignment | — | **Open** | UAT-15 (daily-tasks endpoint not on backend `development`), UAT-16 (no dedupe on 3 ingest families), live contract tests | Yes (code + tests, both repos) | Backend owner approves contract; pick target backend branch |
| AUD-008 | P1 | Clock-action time budget (lifecycle) | PR #79 `d9cdfd0`; part 2 `2a41be6` (PR #102, CI 221 green) | **Partial** — lifecycle part fixed and tested; independent requests off the read loop on branch (UAT-17 part 1) | 17 non-lifecycle handlers still call the backend with `CancellationToken.None` on a serial pipe (UAT-17) | Yes | Approve the concurrency model |
| AUD-009 | — | Clock action "already done" not recovered | PR #75 `c0bf13e` | Fixed – verified | None | — | — |
| **AUD-010** | **P0** | Monitoring privacy gates (consent, policy, time-off) | — | **Open / Escalated** | Gates hard-coded true (UAT-18) | Yes, once the rules are decided | Product + privacy owner decide rules; device test |
| AUD-011 | — | Owner binding of queued rows (with AUD-004) | PR #72 `0fd709c` | Fixed – verified | None | — | — |
| AUD-012 | — | Clock action with expired token not recovered | PR #75 `c0bf13e` | Fixed – verified | None | — | — |
| **AUD-013** | **P0** | Dev bypass flags; UAT environment configuration | PR #89 `e8fd8ad` (part 1) | **Partial** — dev flags refused outside Development (verified); UAT config not done | UAT `ApiBaseUrl`, environment name and Service config delivery (UAT-11, raised to P1) | Partly (config file + validation test) | Backend/ops supply UAT URL; sign-off |
| AUD-014 | — | Lock / sleep / hibernate handling | PR #93 `0b8365c`, PR #94 `f40ec4b` | Fixed – device check pending | None in code | — | 11 §C, §B |
| AUD-015 | — | HTTP responses not disposed | PR #77 `0d13c3b` | Fixed – verified | None | — | — |
| AUD-016 | P1 | Security/legal decision (assignment R07) | — | **Escalated** — no commit, no code reference on `develop`; title not in this repo | Obtain finding text (§A); decide; implement | After decision | Security/legal owner |
| AUD-017 | — | Test runs touching real agent credentials | `55053dc`, PR #71 `41e18e2` | Fixed – verified | Add a whole-suite guard (UAT-20) | Yes | — |
| AUD-018 | — | Persistent logs and correlation ids (tray + Service) | PRs #81, #91, #92 | Fixed – verified | UAT-03 log-content policy | — | Policy owner (UAT-03) |
| AUD-019 | P1 | Live GPS persisted | PR #96 `ade39e2` | **Partial** — live fix kept for its own day only (verified); capture GPS is still stored with face-photo rows (UAT-12) | Resolve with UAT-12 | Yes, after decision | Privacy owner (retention) |
| AUD-020 | P1 | QuestPDF licence (named in `684af6f`: "QuestPDF (licence, AUD-020) … left for separate changes") | — | **Escalated** — `DailySummaryPdfBuilder.cs:34` sets `LicenseType.Community`; QuestPDF 2025.7.0 in `Directory.Packages.props`. Whether the Community licence applies to the company is a legal question | Legal decision; then keep, buy a licence, or replace the PDF library | Yes (replacement or licence key wiring) | Legal owner |
| AUD-021 | — | Long checkout path build failure | PR #87 `e2e941a` (docs) | Fixed (documented; accepted build constraint) | None | — | — |
| AUD-022 | — | Dependency servicing (.NET 10.0.14, MAUI 10.0.120) | PR #84 `684af6f` | Fixed – verified | Re-check servicing at RC cut (R10) | Yes | — |
| AUD-023 | — | No retry on timeouts / 408 | PR #78 `5b68379` | Fixed – verified | Note: increases duplicate risk without dedupe (UAT-16) | — | — |
| AUD-024 | — | Default signing-cert password in `build-msix.ps1` | PR #85 `5745a09` | Fixed – verified | None | — | — |
| AUD-025 | — | No coverage gate | PR #86 `9ee8a8b` | Fixed – verified | None | — | — |
| AUD-026 | P1 | Security/legal decision (assignment R07) | — | **Escalated** — no commit or code reference; title not in this repo | As AUD-016 | After decision | Security/legal owner |
| AUD-027 | ? | *Not documented in this repository* | — | **Unverifiable** | Obtain finding text (§A) | ? | Reconcile with ZIP register |
| AUD-028 | ? | *Not documented in this repository* | — | **Unverifiable** | Obtain finding text (§A) | ? | Reconcile with ZIP register |
| **AUD-029** | **P0** | Installer: Service + tray auto-start | — | **Open / Escalated** (= UAT-07, UAT-08) | Choose packaging; implement; clean install/upgrade/uninstall tests | Yes, after decision | Release owner decides; device tests 11 §A |
| **AUD-030** | **P0** | Tenant portal links / UAT portal domain | PR #90 `91cc7e5` | **Partial** — enrolled tenant's portal opened (verified); UAT `PortalRootDomain`/port not configured | With AUD-013 | Partly | Ops supply UAT portal domain |
| AUD-031 | — | Sync queue blocked by refused uploads | PR #74 `9831884` | Fixed – verified | None | — | — |
| AUD-032 | — | Unguarded `async void` UI handlers | PR #74 `9831884` | Fixed – verified | None | — | — |
| AUD-033 | — | Finished screenshots not uploaded immediately | PR #80 `256ef66`; test made deterministic in `86aa632` | Fixed – verified | None | — | — |
| AUD-034 | — | Service tests redirect agent data paths (`AgentDataPaths`) — named in `b1b1072` | PR #71 `41e18e2` (`TestAgentDataRoot` module initializer) | Fixed – verified (CI 220 Service 518/518). Title inferred from the commit body; confirm against the ZIP register | None | — | — |
| AUD-035 | — | Successful break reported as "Cannot end break." | PR #98 `86d2f57` | Fixed – verified | None | — | 11 §C |
| AUD-036 | P1 | Test runs writing to real `tray-boot.log` | PR #99 `b1b1072` | Fixed – verified (CI 197) | Whole-suite guard (UAT-20) | Yes | — |

¹ P0/P1 where the assignment states it; "—" where the original priority is in the missing register. Fixed rows keep their status regardless of priority; no fixed issue was reopened.

**Related series:** AGT-01 (per-record ack, quarantine) and AGT-02 (bounded queue) — Fixed – verified (PRs #54–#56). AGT-03 (stale `main`) — Open as UAT-05. SEC-01 (tray JWT secret) — backend branch `fix/sec-01-tray-jwt-secret` is merged into backend `development` (ancestor check). T10 — idle budgets done, active budgets open. T15 — Fixed (in-process E2E).

## D. New findings from this audit (UAT-15…UAT-21)

Each one is evidence under a parent AUD item, not a separate blocker count. Details are in `03-issue-register.md`.

| ID | P | Parent | Finding | Class |
|---|---|---|---|---|
| UAT-15 | P2 | AUD-007 | `GET /api/v1/monitoring/tray/daily-tasks` and the `day_clocked_in/out_at_utc` attendance fields exist only on backend branch `fix/dapi-seeder-skip-today` (`51d84d4c`), not on `development`. The tray gets 404 → empty daily summary; a restarted tray cannot show a finished day | Confirmed (code, both repos) |
| UAT-16 | P2 | AUD-007 | Backend app-usage, device-state and meeting-signal ingest has no replay de-duplication (non-unique indexes). With AUD-023 retries, a lost 202 inserts duplicates and inflates reports | Confirmed (code) |
| UAT-17 | P1 | AUD-008 | IPC messages are handled one at a time per connection; 17 non-lifecycle handlers (15 direct, 2 biometric via `EnrollmentCoordinator`) call the backend with `CancellationToken.None`. One slow call delays clock actions behind it | Confirmed (code) |
| UAT-18 | **P0** | AUD-010 | Consent, policy and time-off lifecycle gates are set to `true` after every enrolment; the tray starts activity capture on a local default policy before the server policy arrives; backend ingest checks the feature toggle but not consent or approved leave | Confirmed (code, both repos) |
| UAT-19 | P3 | AUD-029 | Manifest `MinVersion 10.0.17763` / `MaxVersionTested 10.0.19041` vs. release registration `minimumWindowsVersion 10.0.19041` and project `SupportedOSPlatformVersion 10.0.19041` | Confirmed (code) |
| UAT-20 | P3 | AUD-017/034/036 | The only guard test (`TestPathIsolationTests`) covers the tray-boot fallback path. No whole-suite check that real `%ProgramData%\ONEVO` / `%LocalAppData%\ONEVO` stay untouched | Confirmed (code) |
| UAT-22 | P3 | — | TrayApp Release build prints 49 XamlC `XC0025` warnings (bindings with explicit `Source` not compiled; performance hint, not a defect). Shared and Service build with 0 warnings | Verified (CI 220 log) |
| UAT-21 | P2 | R10 | No release candidate infrastructure in use: no tag, no release, no release branch; `scripts/check-footprint.ps1` not in CI | Verified (GitHub) |

## E. Focus-area results (assignment "Final Gap Audit" 1–10)

| # | Area | Result | Status |
|---|---|---|---|
| 1 | Merged fixes in release branch | All AUD/AGT fixes are on `develop`; CI 220 green. No release branch/tag exists | ✅ code · ❌ RC (R10) |
| 2 | Tray ↔ Backend compatibility | 37 of 38 tray routes exist on backend `development` with matching verbs; `tray/daily-tasks` missing (UAT-15). DTOs: tray tolerates missing fields (defaults). Ingest dedupe gap (UAT-16). No live contract test | ❌ Open (AUD-007) |
| 3 | Installer / Service / startup / upgrade / uninstall | MSIX ships tray only; Service installed/removed only by developer scripts (`scripts/install-service.ps1`, `uninstall-service.ps1`); no autostart; MSIX uninstall leaves the Service; upgrade never tested on a device | ❌ Blocker (AUD-029, UAT-07/08) |
| 4 | Auth, tenant isolation, data separation, privacy | All 23 backend controllers the tray calls checked: `TrayDevicePolicy` on every data endpoint; `[AllowAnonymous]` only on activation, device authorization, release check and legal complete-login (challenge cookie + CSRF); owner-bound queue (AUD-004); DPAPI LocalMachine + ACL (UAT-09). **Privacy gates not enforced (UAT-18)**; face photos unencrypted (UAT-12); counts in `tray.log` (UAT-03) | ❌ Blocker (AUD-010) |
| 5 | Offline sync, retry, duplicates, recovery | Durable queue, per-record outcomes, quarantine, 24 h purge (AGT-01/02, UAT-13) verified by tests and T15 E2E. Duplicates possible for 3 families (UAT-16) | ⚠ Partial |
| 6 | UAT config, secrets, local storage, integrations | No UAT configuration exists; shipped `ApiBaseUrl` is the dev host `https://onexso.com:7229`. Release secrets (`SIGNING_PFX_*`, `R2_*`, `RELEASE_INGEST_TOKEN`) referenced in `release.yml`; presence not verifiable from here. Release ingest route exists on backend (`POST /admin/v1/tray-releases/ingest`) | ❌ Open (AUD-013/030) |
| 7 | Missing automated tests / device tests | Missing: live contract tests (AUD-007), privacy-gate tests (AUD-010), IPC concurrency tests (AUD-008), installer smoke test, whole-suite isolation guard (UAT-20). Device tests: all of 11 not started | ❌ Open |
| 8 | Performance under active monitoring | Idle budgets only; active run never done (T10 phase B) | ❌ Untested |
| 9 | P0/P1 review | §F | — |
| 10 | Mandatory UAT documents / evidence | §H | ⚠ Partial |

## F. Remaining task backlog (every priority)

| Task | P | Items | Owner | AI can do | Depends on |
|---|---|---|---|---|---|
| R01 Installer | P0 | AUD-029, UAT-07, UAT-08, UAT-19 | AI + Release owner | Implement chosen option, scripts, CI smoke test | Packaging decision (D-01) |
| R02 Backend contracts | P0 | AUD-007, UAT-15, UAT-16 | AI + Backend owner | Merge/port daily-tasks; add dedupe migration + handler; contract tests both sides | D-02, D-03; migration applied by a developer (backend CLAUDE.md) |
| R03 UAT environment | P0 | AUD-013, AUD-030, UAT-11 | AI + Ops | `appsettings.Uat.json`/install-time config, validation test | UAT URLs (D-04) |
| R04 Privacy gates | P0 | AUD-010, UAT-18 | AI + Product/Privacy | Wire real consent/policy/leave sources into `LifecycleGate`; remove local default policy; tests | D-05 |
| R05 AUD-036 PR | P1 | AUD-036 | — | **Done** (PR #99 merged) | — |
| R06 IPC concurrency | P1 | AUD-008, UAT-17 | AI | Bounded concurrent dispatch **done in PR #102** (`2a41be6`, CI 221 green); per-handler budgets remain | D-06 (the conservative option was implemented: only stateless requests run concurrently) |
| R07 Security/legal | P1 | AUD-016, 019, 020, 026, UAT-03, UAT-09, UAT-12 | AI + Security/Legal | Implement decisions | D-07…D-10; §A for AUD-016/026 text |
| R08 Windows matrix | P1 | 11 §A–§F | Human (QA) | — | R01, R03 |
| R09 Active perf | P1 | T10 B | Human + AI (analysis, budgets file) | Budgets + CI/check script after data | R01, R04 |
| R10 RC | P1 | UAT-05, UAT-21, UAT-10 | AI + Release owner | Release PR `develop → main`, tag, notes, version matrix | R01–R07 |
| R11 Pilot | P1 | 11 §J | Human | — | R10 |
| P2 hygiene | P2 | UAT-13 ✅, UAT-15/16 (in R02), UAT-21 (in R10) | — | — | — |
| P3 hygiene | P3 | UAT-02, UAT-06, UAT-19, UAT-20, UAT-22 | AI | Yes, no decision needed (UAT-02 option choice is a code decision) | — |
| Unverifiable | ? | AUD-027, 028 | Human | After §A | ZIP register |

### F.1 AI-agent executable tasks (isolated branch + regression test + reviewed PR each)

| # | Task | Repo | Blocked by |
|---|---|---|---|
| AI-1 | ~~Bounded concurrent IPC dispatch~~ done (PR #102). ~~Read-only budgets~~ done (`923692a`). Remaining: budgets + recovery for writing requests | Tray | D-15 |
| AI-2 | Bring `GetTrayDailyTasks` + attendance day fields (`51d84d4c`) into backend `development` with its tests (UAT-15) | Backend | D-02 |
| AI-3 | Replay de-dupe for app-usage/device-state/meeting signals (natural key `(tenant, device, captured_at)` like activity), migration created not applied; idempotent-replay tests (UAT-16) | Backend | D-03 |
| AI-4 | Contract tests: tray wire records ↔ backend DTO JSON (snapshot of property names) on both sides | Both | AI-2 |
| AI-5 | Privacy gates: consent from `permission-consent`, policy from `PolicyCache`, leave from attendance-status; drop `CreateLocalDefaultPolicy` capture before policy; tests per gate (UAT-18) | Tray (+ backend leave field) | D-05 |
| AI-6 | UAT config: environment-specific Service config delivered by the installer, `ApiBaseUrl`/portal validation test | Tray | D-04 |
| AI-7 | Installer per decision: Service registration, `windows.startupTask`, uninstall cleanup, CI install smoke test on `windows-latest` | Tray | D-01 |
| AI-8 | Face-photo storage per decision (DPAPI spool + delete on ack, GPS removal) (UAT-12/AUD-019) | Tray | D-08 |
| AI-9 | `tray.log` counts → Debug or removed (UAT-03) | Tray | D-07 |
| AI-10 | UAT-02 single idle source; UAT-06 Windows-only test traits; UAT-19 version alignment; UAT-20 whole-suite path guard; UAT-22 compiled bindings | Tray | — |
| AI-11 | RC: release PR `develop → main`, `v1.0.0-uat` tag process, release notes, version matrix | Tray | R01–R07 |

## G. Decision register (product / security / legal / release)

| # | Decision | Items | Owner | Options | Status |
|---|---|---|---|---|---|
| D-01 | How the Service and tray autostart are installed | AUD-029, UAT-07/08 | Release/architecture | MSIX `desktop6:Service` + `windows.startupTask` · MSI/bootstrapper (WiX) + MSIX · MSI only | Open |
| D-02 | Target backend branch for UAT; merge daily-tasks | AUD-007, UAT-15 | Backend owner | Merge to `development` · drop the tray feature for UAT | Open |
| D-03 | Ingest replay de-duplication contract | UAT-16 | Backend owner | Unique key + skip · accept duplicates (document) | Open |
| D-04 | UAT `ApiBaseUrl`, portal domain/port, environment name | AUD-013/030, UAT-11 | Ops/backend | — | Open |
| D-05 | Monitoring gates: consent required before which collectors; behaviour on approved leave; behaviour before first policy | AUD-010, UAT-18 | Product + Privacy | — | Open |
| D-06 | IPC concurrency model and per-handler time budgets | AUD-008 | Tech lead | Concurrent dispatch with ordering for lifecycle · keep serial + per-handler timeouts | Open |
| D-07 | Activity counts in `tray.log` | UAT-03 | Monitoring-policy owner | Debug only · remove · keep (accept) | Open |
| D-08 | Face photo + capture GPS retention on device | UAT-12, AUD-019 | Privacy owner | Encrypt + delete on ack · delete photo data on ack · accept | Open |
| D-09 | DPAPI LocalMachine + ACL as credential protection | UAT-09 | Security owner | Accept after 11 §F · change scheme | Open |
| D-10 | AUD-016, AUD-026 (texts in missing register) | R07 | Security/Legal | — | Open |
| D-14 | QuestPDF Community licence eligibility | AUD-020 | Legal | Community licence applies · buy commercial licence · replace library | Open |
| D-15 | Time budget + recovery for writing IPC requests (consent submit, location change, activation, face enrol, legal acceptance) | UAT-17 | Tech lead | Budget + reconcile from backend (like AUD-008) · leave unbounded (accept) | Open |
| D-11 | Accept UAT-04 (60 s idle-end resolution) for UAT | UAT-04 | Product | Accept · shorten sample window | Proposed: accept |
| D-12 | Production code-signing certificate | UAT-10 | Release owner | — | Open |
| D-13 | Inform report consumers of UAT-01 value change | UAT-01 | Product | — | Open |

## H. Mandatory UAT documents and evidence

| Deliverable | Status | Where |
|---|---|---|
| Completed issue list / AUD status matrix | ✅ (with §A limitation) | §C, 03 |
| Verified code fixes + PR references | ✅ | §C |
| Full automated test results | ✅ for `develop`: CI 220 success — Shared 42/42, Service 518/518, TrayApp 582/582, 0 skipped, coverage gate ok. Not re-run locally (SDK download blocked) | 05 |
| Backend contract verification | ⚠ Static route/DTO check done (§E-2); live check not done | 06, §D |
| UAT environment configuration | ❌ Not available | 08 |
| Working install/upgrade package | ❌ | 07 |
| Real Windows compatibility report | ❌ Not started | 11 |
| Active monitoring performance report | ❌ Not started | 13 |
| Security & privacy decision log | ⚠ Register created, no decisions taken | §G |
| UAT release notes | ⚠ Draft for `develop` | 10 |
| Signed RC, version matrix | ❌ | R10 |
| Go/No-Go | ✅ | §J |

## I. Unresolved risk and deferred-issue register

| ID | P | Risk | Proposed resolution | Needs |
|---|---|---|---|---|
| UAT-18 / AUD-010 | P0 | Monitoring without valid consent or during approved leave | Must fix before UAT | D-05 |
| UAT-07/08 / AUD-029 | P1 (P0 for release) | Employees cannot run the agent from the released package | Must fix before UAT | D-01 |
| AUD-013/030, UAT-11 | P0 | UAT build points at dev host | Must fix before UAT | D-04 |
| UAT-12 / AUD-019 | P1 | Biometric photo + GPS on disk unencrypted for 24 h after upload (UAT-13 purge) | Fix or formally accept | D-08 |
| UAT-17 / AUD-008 | P1 | Clock action delayed behind slow backend call | Fix before UAT | D-06 |
| UAT-15 | P2 | Daily summary empty against backend `development` | Fix or defer with release note | D-02 |
| UAT-16 | P2 | Duplicate app-usage/device-state/meeting rows after lost responses | Fix or accept with report caveat | D-03 |
| UAT-03 | P3 | Per-minute counts in local log | Decide | D-07 |
| UAT-09 | P3 | Credential protection relies on ACL | Accept after 11 §F | D-09 |
| UAT-02, UAT-06, UAT-19, UAT-20, UAT-22 | P3 | Hygiene | AI fixes; may be deferred past UAT with this register as the record | Tech lead accepts deferral |
| UAT-04 | — | Idle-end resolution 60 s | Accepted limitation (proposed) | D-11 |
| UAT-05, UAT-21 | P2 | No release branch/tag; stale `main` | Fix in R10 | Release owner |
| UAT-10 | Info | Dev certificate needs manual trust | Production certificate | D-12 |
| AUD-016/026 | P1 | Unknown content | Obtain text; decide | D-10 |
| AUD-020 | P1 | Licence non-compliance if Community terms do not apply | Legal decision | D-14 |
| AUD-027/028 | ? | Unknown content | Obtain text | §A |

## K. Execution plan (2026-10-30)

One step at a time. Each AI step gets its own branch commits, regression tests, a PR into `develop`, green Windows CI and human review before the next step starts. Docs (03, 05, 10, 12, 14) are updated in the same PR.

| Step | Who | Work | Items | Needs first | Done when |
|---|---|---|---|---|---|
| 1 ✅ | AI | Read-only IPC time budgets | UAT-17 part 2 | — | Merged in PR #103 |
| 2 ✅ | AI | Build hygiene: compiled XAML bindings, single idle source | UAT-22, UAT-02 | — | Merged in PR #103 (warnings 49 → 5) |
| 3 | AI | CI guard: test run must not touch real `%ProgramData%\ONEVO` / `%LocalAppData%\ONEVO` | UAT-20 | — | CI step fails on a planted write; PR merged |
| 4 | AI | Windows-version declarations aligned | UAT-19 | Supported OS list (Release owner) | Manifest, csproj, `release.yml` agree |
| 5 | Human | Decision meeting: D-01…D-15 (14 §G) | all Escalated | — | Each decision recorded in §G with owner and date |
| 6 | AI | Privacy gates: consent, policy, leave enforced before any collector | AUD-010, UAT-18 | D-05 | Gate tests per rule; 11 §I passes |
| 7 | AI | UAT environment configuration and validation | AUD-013, AUD-030, UAT-11 | D-04 | UAT config shipped; validation test |
| 8 | AI | Backend contract: daily-tasks merge, ingest de-dupe (migration created, applied by a developer), contract tests | AUD-007, UAT-15, UAT-16 | D-02, D-03 | Backend + tray PRs merged; 11 §H passes on UAT backend |
| 9 | AI | Installer: Service + tray autostart, upgrade, uninstall, CI install smoke test | AUD-029, UAT-07, UAT-08 | D-01, D-12 | Signed package installs on a clean VM in CI |
| 10 | AI | Privacy/legal follow-ups | UAT-03, UAT-12, AUD-019, AUD-020 | D-07, D-08, D-14 | Per decision |
| 11 | AI | Budgets + recovery for writing IPC requests | UAT-17 rest | D-15 | Recovery tests |
| 12 | Human | Windows device matrix (11 §A–§I), active-monitoring run (13) | R08, R09, T10 B | Steps 6–9 | Checklist signed; active budgets in `footprint-budgets.json` |
| 13 | AI + Human | Release candidate: `develop → main`, tag, signed MSIX, release notes, version matrix | R10, UAT-05, UAT-21 | Step 12 | Immutable RC with CI evidence |
| 14 | Human | Pilot and Go/No-Go sign-off | R11 | Step 13 | Signed decision |

Steps 2–4 need no decision and can run while step 5 is being organised. Steps 6–11 can run in parallel once their decisions exist.

## J. Go / No-Go recommendation

**NO-GO for UAT distribution to employees.**

Blocking, in order:
1. **AUD-010 / UAT-18** — privacy gates are not enforced: monitoring can run without recorded consent and during approved leave.
2. **AUD-029 / UAT-07 / UAT-08** — the released package does not install the Service or start the tray at logon.
3. **AUD-013 / AUD-030 / UAT-11** — no UAT environment configuration; the shipped default is a development host.
4. **AUD-007** — backend contract not aligned for UAT (UAT-15, UAT-16) and not verified live.
5. No device test, no active-monitoring measurement and no signed RC (R08–R10).
6. AUD-016, AUD-026 (texts missing), AUD-027, AUD-028 (unverifiable) and AUD-020 (licence) need the ZIP register or a legal decision.

**GO** for: continuing AI-agent work AI-1, AI-10 (no decision needed) now; AI-2…AI-9 as soon as D-01…D-08 are taken; internal device testing of the existing fixes (11 §B–§F) with the developer install script.

Re-assess when R01–R04 are merged with green CI, 11 §A–§F has passed on Windows 10 and 11, and every row in §I is fixed or signed as accepted.
