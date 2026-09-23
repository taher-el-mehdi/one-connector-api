# Reliability

## Tracking

MySQL table `logs` (connector-owned, not a Sage table):

`Direction`, `EntityType`, `SageNumber`, `DocuWareDocumentId`, `Status`, `CreatedAt`, `UpdatedAt`, `LastAttemptAt`, `LastSuccessAt`, `RetryCount`, `LastError`, `Fingerprint`

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

Structured log templates include `SyncId`, `Direction`, `Entity`, `SageNumber`, `DocuWareDocumentId`, `Status`.

Passwords, OAuth tokens, and client secrets are never logged.

## Error isolation

A failure on one supplier/account/section is stored on that tracking row. The worker continues with the next item and the next direction.
