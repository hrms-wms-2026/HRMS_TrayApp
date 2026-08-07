# Postman — ONEVO Tray + Monitoring (Local)

## Files

| File | Use |
|------|-----|
| `ONEVO-Tray-Monitoring.postman_collection.json` | Import as Collection |
| `ONEVO-Local.postman_environment.json` | Import as Environment (optional; collection has vars too) |

## Import steps

1. Open **Postman**
2. **Import** → select both JSON files above  
   Path: `C:\HR\tray_app_maui\docs\postman\`
3. Top-right environment: select **ONEVO Local (Tray)** (or use collection variables)
4. SSL: Settings → turn **OFF** “SSL certificate verification” for local HTTPS  
   (or enable with your local cert)

## Local URLs

| Service | URL |
|---------|-----|
| Backend API | `https://localhost:7229` |
| Swagger | `https://localhost:7229/swagger` |
| Agent Service | Named pipe only (no HTTP) |
| Tray App | Desktop UI |

## Request order (copy this flow)

```
1. Tenant Auth → Login (base host)
   └─ if 202: Select Workspace (slug: acme)

2. Tray Activation → Generate Activation Code
   └─ needs employee/tenant Bearer token
   └─ saves {{activation_code}}

3. Tray Activation → Exchange Activation Code (Tray)
   └─ no auth
   └─ saves {{tray_access_token}} + {{tray_refresh_token}}

4. Tray Ingest → Ingest Activity Snapshots
   └─ Bearer {{tray_access_token}}
   └─ expect 202 Accepted → rows in activity_snapshots

5. HR Query → Get Activity Snapshots
   └─ Bearer tenant token + employee_id + date
```

## Endpoints cheat sheet

| Method | Path | Auth |
|--------|------|------|
| POST | `/api/v1/auth/login` | none |
| POST | `/api/v1/auth/login/select-workspace` | none |
| POST | `/api/v1/monitoring/activation/generate` | Tenant JWT |
| POST | `/api/v1/monitoring/activation/exchange` | none |
| POST | `/api/v1/monitoring/activation/refresh` | none |
| POST | `/api/v1/monitoring/activity/snapshots` | **Tray** JWT |
| POST | `/api/v1/monitoring/check-in` | Tray JWT |
| GET | `/api/v1/monitoring/activity/snapshots` | Tenant JWT + `monitoring:read` |
| GET | `/api/v1/monitoring/activity/daily-summary` | Tenant JWT |
| GET | `/api/v1/monitoring/activity/daily-range` | Tenant JWT |

## Notes

- Activation **code length is 8** (A–Z and 2–9), not 6.
- Tray app Connect UI currently allows 6 chars — backend expects **8**. Use Postman exchange until UI is aligned.
- Dev smoke tenants after backend seed: `acme`, `dapi` (when seed runs on API start).
- Fresh DB was recreated; set real `tenant_email` / password if seed users differ from env file defaults.
- Agent Service still needs Device JWT on disk to auto-sync; Postman can write DB **without** tray sync.
