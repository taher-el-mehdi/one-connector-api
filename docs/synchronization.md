# Synchronization

Two use cases run separately: `SageToDocuWareSyncUseCase` and `DocuWareToSageSyncUseCase`.

Shared pipeline per entity:

Detect → Load → Validate → Map → Check existing → Create/update → Record state → Log

## Sage → DocuWare

1. Load Sage rows (optionally one key).
2. Skip empty keys.
3. Map fields and compute SHA-256 fingerprint.
4. Resolve existing DocuWare document by stored id or key-field query.
5. Skip when fingerprint is unchanged.
6. Update index fields, or create a document with `EasyUpload` and a JSON stub file.
7. Record tracking (`Completed` / `Failed` / `Skipped`).

Never create a second document for the same Sage number when a DocuWare document already exists for that key.

## DocuWare → Sage

1. Load documents by key, document id, or paged cabinet query (key lookup preferred for single-item API calls).
2. Skip empty keys.
3. Map to Sage columns; drop protected columns.
4. If the Sage row exists and writable values already match, skip.
5. If `ApplySageWrites` is false, skip (dry).
6. UPDATE with `cbMarq` lock.
7. If missing in Sage: skip unless `InsertMissingInSage` is true.

There is no delete propagation in either direction.

## Worker

`SynchronizationWorker` (`BackgroundService`):

- Uses the MySQL `logs` table (`id_synchronization`, `error_message`, `file` JSON for per-record payload).
- Fingerprint and business keys for skip/idempotency live in `file`, not as columns.
- If `Synchronization:Enabled`, runs one cycle immediately, then every `IntervalSeconds`.
- Also drains jobs queued by `POST /api/synchronizations/{id}/run`.
- Uses `CancellationToken` throughout.
- Catches per-cycle exceptions so the process stays up.

Order each cycle: Sage→DocuWare then DocuWare→Sage, for Supplier, Chart of Accounts, Analytic Section.
