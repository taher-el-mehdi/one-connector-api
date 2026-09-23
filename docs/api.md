# API

Controllers call application services. SDK types are not returned.

| Method | Path | Purpose |
|---|---|---|
| POST | `/api/auth/login` | Operator login. Returns a bearer token. No session required |
| POST | `/api/auth/logout` | Revokes the current bearer token |
| GET | `/api/auth/me` | Current operator name |
| GET | `/api/health` | App + DocuWare + Sage + tracking. Degraded probes return 503 with the same JSON body |
| GET | `/api/health/docuware` | DocuWare connectivity |
| GET | `/api/health/sage` | Sage SQL connectivity |
| GET | `/api/configuration` | Non-secret configuration. Passwords and client secret are booleans, never values |
| GET | `/api/sync/status` | Worker flags, cycle-in-progress, last cycle summary, recent tracking |
| GET | `/api/sync/errors` | Failed tracking rows |
| GET | `/api/suppliers` | Sage suppliers |
| GET | `/api/entities/tables` | Base tables in the connected Sage database |
| GET | `/api/entities/tables/{schema}/{name}` | One Sage table and its columns |
| GET | `/api/accounts` | Sage chart of accounts |
| GET | `/api/sections` | Sage analytic sections |
| GET | `/api/cabinets` | File cabinets in the connected DocuWare organization |
| GET | `/api/cabinets/{id}` | One cabinet and its DocuWare index fields |
| POST | `/api/sync/run` | Queue a full cycle (returns 202) |
| POST | `/api/sync/supplier/{number}` | Queue one supplier |
| POST | `/api/sync/account/{number}` | Queue one account |
| POST | `/api/sync/section/{code}` | Queue one section |
| POST | `/api/sync/document/{documentId}?entityType=Supplier` | Queue one DocuWare document |
| POST | `/api/sync/retry/{syncId}` | Re-queue a tracking row |

`GET /api/health` and `POST /api/auth/login` do not require a session. Other routes require `Authorization: Bearer`.

Manual posts enqueue work. They do not keep the HTTP request alive until DocuWare/Sage finish.
