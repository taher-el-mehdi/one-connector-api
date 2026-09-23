# Troubleshooting

## How to test DocuWare connectivity

```http
GET /api/health/docuware
```

Healthy means the SDK connected, listed organizations, and resolved the three configured file cabinets.

Common failures:

- Empty `PlatformUrl` or UserName → configuration validation / health Unhealthy
- 401 → wrong user/password or App Registration client
- Cabinet not found → set `FileCabinets:*:Name` to the exact cabinet name, or set Id for that environment only in secrets

## How to test Sage connectivity

```http
GET /api/health/sage
```

Runs `SELECT 1` on the configured database.

Common failures:

- Windows auth from a service account that cannot reach SQL Server → use Sql auth in config or run the service as a domain user with rights
- TrustServerCertificate
- Wrong database name (Sage company DB, not `master`)

## Sync issues

| Symptom | Check |
|---|---|
| Duplicates in DocuWare | Tracking row + key-field query; use `POST /api/sync/retry/{id}` after fixing data, do not create manually |
| Sage 80011 | Do not map `CT_Type` on reverse; close the Sage fiche; `cbMarq` changed |
| Sage 80003 | Close Sage 100 and retry |
| Worker idle | `Synchronization:Enabled`, `IntervalSeconds`, `/api/sync/status` |
| Ping-pong updates | Fingerprint/writable comparison should skip; inspect tracking `Fingerprint` |
| Logs show secrets | They should not; file an issue if a token appears |

GET `/api/sync/errors` lists failed tracking rows with `LastError`.
