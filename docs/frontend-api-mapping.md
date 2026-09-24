# Frontend to API mapping

The React app calls the ASP.NET Core API only. Paths below are the current API, not the Flask routes in `webapp/app.py`.

## Feature mapping

| Old UI feature | Old JS function | Current API endpoint | React component | Status |
| --- | --- | --- | --- | --- |
| Login | login form `fetch("/api/login")` | None | — | Obsolete. API has no auth |
| Logout / current user | `btnLogout`, startup `GET /api/me` | None | — | Obsolete |
| Connection health badges | `refreshHealth` | `GET /api/health` | `HealthStrip` | Preserved |
| Test DocuWare | part of `refreshHealth` | `GET /api/health/docuware` | `SystemPage` | Preserved |
| Test Sage | part of `refreshHealth` | `GET /api/health/sage` | `SystemPage` | Preserved |
| Load settings | `loadSettings` | `GET /api/configuration` | `ConfigurationPage` | Preserved as read-only non-secret view |
| Save settings | `collectSettings` + `PUT /api/settings` | None | — | Not available. Change user secrets or environment variables, then restart |
| List DocuWare cabinets | `loadCabinets` | `GET /api/cabinets` | `CabinetsPage` | Lists every cabinet DocuWare returns. Purge and document counts stay unavailable |
| Choose cabinet before sync | `ensureCabinetSettingsSaved` | None | — | Cabinet resolution stays in the API |
| Sync fournisseurs once | `btnSyncOnce` | `POST /api/sync/run` | `SyncControls` | Replaced by a full cycle. The API does not run suppliers alone unless `POST /api/sync/supplier/{number}` |
| Sync plan once | `btnPlanSyncOnce` | `POST /api/sync/run` or `POST /api/sync/account/{number}` | `SyncControls`, `EntityBrowserPage` | Full cycle, or one account |
| Sync section once | `btnAnalytiqueSyncOnce` | `POST /api/sync/run` or `POST /api/sync/section/{code}` | `SyncControls`, `EntityBrowserPage` | Full cycle, or one section |
| Force / rescan / all types checkboxes | sync form bodies | None | — | Obsolete as request flags |
| Start / stop / status watch | `btnWatchStart`, `btnWatchStop`, `btnWatchStatus` (and plan/section copies) | `GET /api/sync/status` (`workerEnabled`, `intervalSeconds`, `cycleInProgress`) | `SyncStatusCard` | Watchers replaced by the worker |
| Invoice fetch and table | `btnFactures` | None | — | Obsolete. No invoice API |
| DocuWare → Sage (unused in the old HTML) | Flask `api_sync_dw_to_sage` | Included in `POST /api/sync/run` when `docuWareToSage` is true | `DirectionPanel` | Monitoring plus the full cycle. No separate direction command |
| Cabinet count | `btnPurgeCount` | None | — | Not available |
| Purge cabinet / trash | `runPurge` | None | — | Not available. Not reimplemented |
| Sync status | JSON `<pre>` after each call | `GET /api/sync/status` | `SyncStatusCard`, `DashboardPage` | Preserved |
| Errors | `error` fields in JSON logs | `GET /api/sync/errors` | — | API only. No Errors screen |
| Retry one record | None in the old UI | `POST /api/sync/retry/{syncId}` | — | API only |
| Sync one document | None in the old UI | `POST /api/sync/document/{documentId}?entityType=` | `SyncControls` | New API capability |
| Dashboard totals | None | `GET /api/sync/status` (`lastCycle`, `recent`) | `DashboardPage` | Built from real status data |

## Frontend-consumed endpoints

| Method | Endpoint | Purpose | Frontend usage |
| --- | --- | --- | --- |
| GET | `/api/health` | Overall status plus DocuWare, Sage, and tracking probes | Dashboard, System. HTTP 503 still carries the health body |
| GET | `/api/health/docuware` | DocuWare connectivity | System connection test |
| GET | `/api/health/sage` | Sage SQL connectivity | System connection test |
| GET | `/api/configuration` | Non-secret configuration snapshot | Configuration |
| GET | `/api/sync/status` | Worker flags, in-progress flag, last cycle summary, latest 50 tracking rows | Dashboard, Synchronization |
| GET | `/api/sync/errors` | Latest 100 failed tracking rows | API only |
| GET | `/api/entities/tables` | Base tables in the connected Sage database | Entities |
| GET | `/api/entities/tables/{schema}/{name}` | One Sage table and its columns | Entity table |
| GET | `/api/cabinets` | File cabinets in the connected DocuWare organization | Cabinets |
| GET | `/api/cabinets/{id}` | One cabinet and the index fields DocuWare defines on it | Cabinet detail |
| POST | `/api/sync/run` | Queue a full cycle. Returns 202 | Synchronization |
| POST | `/api/sync/supplier/{number}` | Queue one supplier. Returns 202 | Suppliers and Synchronization |
| POST | `/api/sync/account/{number}` | Queue one account. Returns 202 | Chart of accounts and Synchronization |
| POST | `/api/sync/section/{code}` | Queue one section. Returns 202 | Analytic sections and Synchronization |
| POST | `/api/sync/document/{documentId}?entityType=` | Queue one document. Returns 202 | Synchronization |
| POST | `/api/sync/retry/{syncId}` | Re-queue a tracking row with force. Returns 202 or 404 | API only |

There is no stop, dry-run, or “sync this direction only” endpoint. Those buttons are not in the UI.

`lastCycle` is the last cycle finished in the current process. It is empty after a restart. Tracking rows in `recent` and `errors` come from MySQL `logs` and survive restarts. The UI labels the two sources separately.

`recent` is capped at 50 and `errors` at 100 by the API. Dashboard counts say so.

## Backend adjustments

### 1. Read-only configuration

- Problem: the configuration screen had nothing real to show.
- Current API behavior before the change: configuration existed only in `IOptions` and was not exposed.
- Why the frontend cannot do this alone: browser code must not read user secrets, `appsettings.json`, or SQL/DocuWare itself.
- Change: `GET /api/configuration` returns platform URL, organization, authentication mode, user names, cabinet name/id, Sage server/database/auth/timeouts/filters, synchronization flags, and the tracking path. Passwords and the client secret are booleans (`passwordConfigured`, `clientSecretConfigured`).
- Impact: read-only. Sync behavior is unchanged. The endpoint is unauthenticated, like the rest of this API, so it must stay behind the same network boundary.

A write endpoint was not added. The API has no authentication, options are bound at startup, and a browser that can submit passwords would be publishing those secrets.

### 2. Last cycle summary and in-progress flag

- Problem: the dashboard needs processed, succeeded, failed, and skipped counts, and whether a cycle is running.
- Current API behavior before the change: `SyncStatusSnapshot` kept only `LastCycleAt` and `LastSyncId`. The orchestrator already computed `SyncCycleResult` and then dropped the counts.
- Why the frontend cannot do this alone: tracking rows are per record, not per cycle, and the latest 50 rows are not a cycle total.
- Change: the snapshot also stores the last `SyncCycleResult` and a `cycleInProgress` flag. `GET /api/sync/status` adds `cycleInProgress` and `lastCycle`. Existing fields stay.
- Impact: additive JSON. No change to matching, retries, or what gets written to DocuWare or Sage.

### 3. CORS

- Problem: the Vite dev server is a different origin from `http://localhost:5175`.
- Change: `Cors:AllowedOrigins` is applied when the list is non-empty. Development settings allow `http://localhost:5173`. An empty list does not allow every origin.
- Impact: browser preflight only. Same-origin calls are unchanged.
