# Legacy UI analysis

The previous interface is a Flask application in `webapp/` with static files in `webapp/static/`. It talks to Python modules that connect to DocuWare and Sage SQL directly. It is a functional and UX reference. It is not the architecture of the React application.

The supported administration UI is the React app in `frontend/`. It talks only to the ASP.NET Core API.

## What the old interface does

The UI is in French. After a session login it shows six tabs.

| Screen | Purpose |
|---|---|
| Factures | Choose a DocuWare cabinet and download invoice index data into a JSON file, then show a summary table |
| Sage → Fournisseurs | One-shot sync of `F_COMPTET` into a chosen cabinet, plus a background watcher |
| Sage → Plan comptable | Same pattern for `F_COMPTEG` |
| Sage → Section analytique | Same pattern for `F_COMPTEA` |
| Vider armoires | Count, dry-run, and delete every document in a cabinet, with optional trash purge and tracking reset |
| Paramètres | Edit DocuWare, SQL, sync, invoice, and UI-login settings stored in a JSON file |

Persistent behaviors:

- Session login (`/api/login`, `/api/me`, `/api/logout`). A 401 sends the browser to `/login`.
- Settings load and save. Passwords are not written back into the form. Empty password fields are omitted so a save does not clear them.
- SQL user and password inputs are disabled when authentication is Windows.
- File cabinets are listed from DocuWare and copied into name/id fields. Extra cabinets can be added and removed. Removal is local until save.
- Health badges poll DocuWare and SQL on load and when the user clicks “Tester connexions”.
- Each sync tab can force an update, rescan DocuWare ids, and (for suppliers and accounts) widen the Sage filter. The chosen cabinet is saved before the sync runs.
- Watch start, stop, and status exist independently for suppliers, chart of accounts, and analytic sections.
- Invoice fetch writes a JSON file on the server and renders a table (id, document number, company, supplier, amounts, status, allocation, articles).
- Cabinet purge requires the word `VIDER`, then a browser `confirm`, unless the action is a dry run.
- Results are shown as pretty-printed JSON in a `<pre>`, plus a small toast.

## Flask routes the old JavaScript calls

| Method | Path | Caller |
|---|---|---|
| POST | `/api/login` | Login page |
| POST | `/api/logout` | Déconnexion |
| GET | `/api/me` | Header user chip |
| GET | `/api/settings` | `loadSettings` |
| PUT | `/api/settings` | Save, and `ensureCabinetSettingsSaved` before each sync |
| GET | `/api/health` | `refreshHealth` |
| GET | `/api/cabinets` | `loadCabinets` |
| POST | `/api/factures` | Récupérer |
| POST | `/api/sync/sage-to-docuware` | Sync fournisseurs une fois |
| GET | `/api/sync/watch` | Statut watch fournisseurs |
| POST | `/api/sync/watch/start` | Démarrer watch fournisseurs |
| POST | `/api/sync/watch/stop` | Arrêter watch fournisseurs |
| POST | `/api/sync/plan-comptable` | Sync plan une fois |
| GET/POST | `/api/sync/plan-comptable/watch` and `.../start`, `.../stop` | Plan watcher |
| POST | `/api/sync/section-analytique` | Sync section une fois |
| GET/POST | `/api/sync/section-analytique/watch` and `.../start`, `.../stop` | Section watcher |
| GET | `/api/cabinets/{id}/count` | Compter les documents |
| POST | `/api/cabinets/purge` | Simuler / Vider |

`app.py` also exposes `GET /api/sync/docuware-fournisseurs` and `POST /api/sync/docuware-to-sage`. The static UI never calls them.

## What to preserve

| Behavior | How the React app preserves it |
|---|---|
| See whether DocuWare and Sage are reachable | `GET /api/health`, `GET /api/health/docuware`, `GET /api/health/sage` |
| See which cabinets, filters, interval, and directions are configured | `GET /api/configuration` (non-secret view) |
| Run synchronization on demand | `POST /api/sync/run` |
| Run one supplier, account, or section | `POST /api/sync/supplier/{number}`, `/account/{number}`, `/section/{code}` |
| Run one DocuWare document | `POST /api/sync/document/{documentId}` |
| See recent activity, direction, entity, and errors | `GET /api/sync/status` and `GET /api/sync/errors` |
| Retry a failed record | `POST /api/sync/retry/{syncId}` (the API sets `Force`) |
| Browse Sage tables (configured mappings) | `GET /api/entities/tables` |
| Mask secrets in the UI | Configuration returns `passwordConfigured` / `clientSecretConfigured`, never the secret |
| Confirm destructive or forceful actions | Confirmation dialog before a full cycle and before retry |
| Toast feedback | Sonner toasts. No `alert()` |

## What the backend already replaced

| Old behavior | Replacement |
|---|---|
| Three in-process watchers with start/stop | `SynchronizationWorker`. `Synchronization:Enabled` and `IntervalSeconds` are configuration, not HTTP commands |
| One-shot sync functions inside the web process | The worker runs queued `SyncCycleRequest` values. HTTP returns 202 |
| Direct DocuWare and SQL calls from the UI process | API adapters only |
| Settings file `uploads/app_settings.json` applied on each request | `appsettings`, user secrets, and environment variables bound at startup |
| Per-request `force`, `refresh_doc_ids`, and “all types” flags | Fingerprint skip logic, cabinet resolution, and `Sage:SuppliersOnly` / `ChartOfAccountsTypeZeroOnly`. Retry is the only force operation |
| DocuWare → Sage dry-run (`apply`) | `Synchronization:ApplySageWrites` and `InsertMissingInSage` on the worker. There is no dry-run endpoint |

## What is obsolete for this API

These old features are not reimplemented. The React app must not call DocuWare or SQL to recreate them.

| Feature | Why it is not in the React app |
|---|---|
| UI username/password login | The ASP.NET API has no authentication. Protect it at the reverse proxy, as `docs/development.md` already states |
| Editing passwords, connection strings, or cabinet ids from the browser | No authenticated write API. Writing secrets to an open endpoint would publish them to every browser |
| Invoice download (`POST /api/factures`) | The connector synchronizes suppliers, chart of accounts, and analytic sections. It has no invoice endpoint |
| Listing every DocuWare cabinet | No cabinet-catalog endpoint. Configured cabinet name and id are on `GET /api/configuration` |
| Extra cabinets beyond the three entities | The API maps exactly three cabinets |
| Emptying a cabinet or the trash | Destructive DocuWare operation with no API. Must not be rebuilt in the browser |
| Raw JSON logs as the primary result | Tracking rows and the last cycle summary are shown as tables and cards. Technical details stay collapsed |

## Backend gaps found during discovery

1. Configuration was not readable, so a configuration screen could only invent values.
2. The status snapshot stored `LastCycleAt` and `LastSyncId`, not the cycle counts the worker already computed.
3. The API had no CORS policy, so a Vite app on another origin could not call it.

Those three are addressed by small additive API changes. Synchronization use cases are unchanged. See `docs/frontend-api-mapping.md`.
