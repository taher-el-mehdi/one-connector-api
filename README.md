# DocuWare Sage 100 Connector

ASP.NET Core 10 API and background worker that keep Sage 100 SQL data in step with DocuWare file cabinets. The operator console is the React app in [`frontend/`](frontend/README.md).

HTTP requests queue work. They do not hold a full sync cycle open. The worker, in the same process as the API, runs the built-in entity cycle and any named synchronization that is due.

## Built-in entities

| Sage table | Filter | DocuWare cabinet (default name) | Key |
|---|---|---|---|
| `dbo.F_COMPTET` | `CT_Type = 1` | Fournisseur | `NUM` / `CT_Num` |
| `dbo.F_COMPTEG` | `CG_Type = 0` | Plan Comptable | `CG_NUM` / `CG_Num` |
| `dbo.F_COMPTEA` | all rows | Section analytique | `CODE` / `CA_Num` |

Operators can also define field mappings and named synchronizations in the console. A named synchronization points at one mapping and runs one way (Sage to DocuWare, or DocuWare to Sage) or both ways. The worker claims due rows, applies the chosen direction, and records success or failure. `POST /api/synchronizations/{id}/run` queues one immediately.

## Quick start

Requirements: .NET 10 SDK, MySQL 8, Node.js 22 or newer, SQL Server access to the Sage company database, and a DocuWare Cloud or on-prem Platform user. ODBC is not required. Sage uses `Microsoft.Data.SqlClient`.

```powershell
cd docuware_sage_100_connector
copy .env.example .env
```

Fill `ConnectorStore__*` in `.env` (MySQL host, database, user, password, and a 32-byte secrets key). DocuWare, Sage, synchronization settings, mappings, named synchronizations, and operator accounts live in that database, not in `appsettings.json`. On an empty database, the first start applies `Database/Migrations` and imports `Database/seed.local.json` when `setting` or `user` is empty. That seed file is not committed.

```powershell
dotnet run --launch-profile http
```

In a second terminal:

```powershell
cd frontend
npm install
npm run dev
```

- API: `http://localhost:5175`
- UI: `http://localhost:5173`
- OpenAPI (Development): `http://localhost:5175/openapi/v1.json`
- Health: `GET /api/health` (no sign-in)
- API tests: `dotnet test` from this folder
- UI tests: `npm test` from `frontend/`

`appsettings.Development.json` sets `Synchronization:Enabled` to `false`, so the worker does not poll. `POST /api/synchronizations/{id}/run` still queues that job. To poll locally, set `Synchronization:Enabled` to `true` in that file, or remove the key so the MySQL value is used.

The HTTPS launch profile is `--launch-profile https` (`https://localhost:7277`).

## Where settings live

| Kind | Where |
|---|---|
| MySQL connection and the secrets key | `.env` or environment variables. Not committed. |
| DocuWare, Sage, and sync settings | MySQL `setting` key/value rows. Passwords are encrypted with `ConnectorStore__SecretsKey`. |
| Mappings and named synchronizations | MySQL `mapping_table`, `mapping_field`, and `synchronization`. |
| Synchronization history | MySQL `logs`. |
| Operator passwords | `user.password_hash`. The UI stores a signed bearer token. |
| API origin and status refresh | `frontend/.env.development` or `frontend/.env`. Public in the built bundle. |

`GET /api/health` and `POST /api/auth/login` do not require a session. Other routes require `Authorization: Bearer`. `GET /api/configuration` returns passwords and the DocuWare client secret as configured/not-configured flags. Revealing a stored secret requires the operator's own password.

## Documentation

Deeper notes are in [docs/](docs/).

| Topic | Document |
|---|---|
| Architecture and project layout | [docs/architecture.md](docs/architecture.md) |
| Configuration and secrets | [docs/configuration.md](docs/configuration.md) |
| DocuWare authentication | [docs/docuware.md](docs/docuware.md) |
| Sage SQL | [docs/sage.md](docs/sage.md) |
| Field mapping | [docs/mapping.md](docs/mapping.md) |
| Sync flows | [docs/synchronization.md](docs/synchronization.md) |
| State, retries, and errors | [docs/reliability.md](docs/reliability.md) |
| Local run and production hosting | [docs/development.md](docs/development.md) |
| API routes | [docs/api.md](docs/api.md) |
| Connectivity checks | [docs/troubleshooting.md](docs/troubleshooting.md) |
| Administration UI | [frontend/README.md](frontend/README.md), [docs/frontend-architecture.md](docs/frontend-architecture.md) |
