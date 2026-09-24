# Frontend architecture

```text
React administration UI
        |
        | HTTP / REST
        v
docuware_sage_100_connector API
        |
        +---- DocuWare
        |
        +---- Sage 100 SQL
        |
        +---- Synchronization worker
```

The UI is a Vite + React + TypeScript application in `frontend/`. TanStack Query owns server state. React Router owns screens. The API client in `frontend/src/api/` is the only place that calls `fetch`.

Synchronization, cabinet resolution, fingerprints, retries, and SQL stay in the API. The browser never receives a connection string, a DocuWare password, or a client secret.

## Screens

| Route | Screen | API |
|---|---|---|
| `/` | Operator sign-in | None. Session stays in the browser |
| `/login` | Operator sign-in | Same as `/` |
| `/app` | Overview | `GET /api/health`, `GET /api/sync/status`, `GET /api/configuration` |
| `/app/synchronization` | Both directions and queue controls | status, configuration, `POST /api/sync/run`, per-record posts |
| `/app/entities` | Sage tables | `GET /api/entities/tables` |
| `/app/entities/table/:schema/:name` | One Sage table and its columns | `GET /api/entities/tables/{schema}/{name}` |
| `/app/cabinets` | DocuWare file cabinets | `GET /api/cabinets` |
| `/app/cabinets/:id` | One cabinet and its index fields | `GET /api/cabinets/{id}` |
| `/app/configuration` | Read-only non-secret settings | `GET /api/configuration` |
| `/app/system` | Connection tests and about | `GET /api/health`, `/api/health/docuware`, `/api/health/sage` |

Invoice download, cabinet purge, and watcher start/stop are not screens. They have no API. The product login is a browser session for the operator console. See `docs/legacy-ui-analysis.md` and `docs/frontend-api-mapping.md`.

## Request path

```text
API error
   -> api/client.ts
   -> ApiError (message, HTTP status, redacted body)
   -> TanStack Query
   -> error panel or toast
```

`GET /api/health` may return HTTP 503 with a normal health body when DocuWare, Sage, or tracking is down. The client treats that body as data so the dashboard can show which dependency failed. Other errors become `ApiError`. Technical details sit in a collapsed section. Stack traces and fields whose names look like secrets are not shown.

## Refresh

`GET /api/sync/status` refetches while the tab is visible. The interval is `VITE_STATUS_POLL_INTERVAL_MS`, and values under 2000 ms are raised to 2000. Health is not on that timer: each health call opens DocuWare and SQL. Operators use Test all, Test DocuWare, and Test Sage.

A later SignalR, WebSocket, or SSE client can invalidate the `sync` query key. The screens read that cache and do not need a rewrite.

`lastCycle` on the status payload is the last cycle finished in the current API process. Tracking rows survive restarts. The UI labels the two sources separately.

## Configuration boundary

| Setting | Owner |
|---|---|
| `VITE_API_BASE_URL` | Frontend. Public in the built bundle. |
| `VITE_API_PROXY_TARGET` | Vite dev server only. |
| `VITE_STATUS_POLL_INTERVAL_MS` | Frontend. |
| DocuWare, Sage, synchronization, tracking | API. The UI displays `GET /api/configuration` and cannot save it. |
| Passwords and client secret | API secret store. The UI shows whether they are set. |

Business components do not embed a server name, cabinet id, or API host. The dev default `http://localhost:5175` is the API launch profile, set in `frontend/.env.development`.
