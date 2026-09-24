# Reliability

## Tracking

MySQL table `logs` (connector-owned, not a Sage table):

| Column | Role |
|---|---|
| `id` | Row id |
| `id_synchronization` | Optional FK to `synchronization.id` |
| `status` | Pending / Processing / Completed / Failed / Skipped |
| `created_at`, `updated_at`, `last_attempt_at`, `last_success_at` | Timestamps |
| `retry_count` | Failure budget counter |
| `error_message` | Failure text |
| `file` | JSON payload (`direction`, `entityType`, `sageNumber`, `docuWareDocumentId`, `fingerprint`) |

Statuses: `Pending`, `Processing`, `Completed`, `Failed`, `Skipped`.

## Retry strategy

Polly retry pipeline wraps DocuWare SDK calls.

- Exponential backoff from `FirstRetryDelaySeconds`
- Cap: `MaxRetries`
- HTTP 429: honor `Retry-After` when present
- Transient: timeouts, 5xx, 429, `HttpRequestException`
- Permanent (no retry): invalid credentials, 401/403, malformed data, Sage 80011 (`CT_Type` / `cbMarq`)
- Sage 80003 (record locked): failed item, continue cycle; operator should close Sage 100

## Logging

Structured log templates include `SyncId`, `SynchronizationId`, `Direction`, `Entity`, `SageNumber`, `DocuWareDocumentId`, `Status`.

Passwords, OAuth tokens, and client secrets are never logged.

## Error isolation

A failure on one supplier/account/section is stored on that tracking row (`error_message` + `file`). The worker continues with the next item and the next direction.
