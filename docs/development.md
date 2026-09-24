# Development and production

## How to run the API and worker

The worker is hosted in the same process as the API:

```powershell
cd docuware_sage_100_connector
dotnet restore
dotnet run --launch-profile http
```

HTTPS profile: `--launch-profile https` (`https://localhost:7277`).

`Synchronization:Enabled=false` in `appsettings.Development.json` starts the API without polling, even when the MySQL row is enabled. `POST /api/synchronizations/{id}/run` still queues that job, and the worker executes queued work.

To run continuous sync locally, set `Synchronization:Enabled` to `true` in `appsettings.Development.json`, or remove that key so the MySQL value is used.

## Development setup

- .NET 10 SDK
- MySQL 8 for connector settings and operator accounts (connection in `.env`)
- ODBC is not required; the connector uses `Microsoft.Data.SqlClient`
- SQL Server access to the Sage company database
- DocuWare Cloud or on-prem Platform user

```powershell
dotnet test ..\docuware_sage_100_connector.Tests\docuware_sage_100_connector.Tests.csproj
```

Unit tests mock DocuWare and Sage. They do not need a live environment.

## Production deployment

- Publish: `dotnet publish -c Release`
- Host as Windows Service, IIS, or container
- Set secrets via environment variables or a vault
- `Synchronization:Enabled=true`
- Choose an interval that respects DocuWare Cloud throttling (start at 30s or higher)
- Keep `InsertMissingInSage=false` unless inserts are explicitly required
- Close Sage 100 company/fiche locks before reverse writes if 80003 errors appear
- Back up the MySQL database; synchronization state is the `logs` table
- Do not expose the administrative API to the public internet without authentication at the reverse proxy

## Administration UI

The React app lives in `frontend/`. It calls this API and does not open DocuWare or SQL itself. `webapp/` is the previous Flask interface and is not the supported UI.

Prerequisites: Node.js 22 or newer, and the API running.

```powershell
cd frontend
npm install
npm run dev
```

- UI: `http://localhost:5173`
- API base URL: `VITE_API_BASE_URL` in `frontend/.env.development` (default `http://localhost:5175`, the `http` launch profile)
- Dev-server proxy target, used only when the base URL is empty: `VITE_API_PROXY_TARGET`
- Status refresh interval: `VITE_STATUS_POLL_INTERVAL_MS` (default 8000, minimum 2000). Health checks are manual because each one contacts DocuWare and Sage.

```powershell
npm test
npm run build
```

`npm run build` typechecks and writes `frontend/dist`. Serve that directory behind the same proxy as the API, or set `VITE_API_BASE_URL` at build time to the API origin and allow that origin in `Cors:AllowedOrigins`.

### Where configuration belongs

| Kind | Where it lives |
|---|---|
| API base URL and UI poll interval | Frontend environment variables. They are public once the bundle is built. |
| DocuWare, Sage, and sync settings | MySQL `setting` rows. The API loads them at startup. |
| MySQL connection and the secrets key | `.env` or environment variables. Not committed. |
| Operator passwords | `user.password_hash` in MySQL. The UI receives a signed bearer token. |
| DocuWare and Sage passwords | Encrypted columns in MySQL. The UI receives a configured/not-configured flag. |
| `frontend/.env` | Do not put DocuWare or Sage credentials here. |

Copy `frontend/.env.example` when you need a local override. `frontend/.env.local` is gitignored.

