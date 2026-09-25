# DocuWare Sage 100 Connector

ASP.NET Core 10 API and background worker that keep Sage 100 SQL data in step with DocuWare file cabinets. The operator console is the React app in [`frontend/`](frontend/README.md).

HTTP requests queue work. They do not hold a full sync cycle open. The worker, in the same process as the API, runs the built-in entity cycle and any named synchronization that is due.

The connector store is the SQL Server database defined in [`Database/schema.sql`](Database/schema.sql). Database name: `one_connector`.

## Built-in entities

| Sage table | Filter | DocuWare cabinet (default name) | Key |
|---|---|---|---|
| `dbo.F_COMPTET` | `CT_Type = 1` | Fournisseur | `NUM` / `CT_Num` |
| `dbo.F_COMPTEG` | `CG_Type = 0` | Plan Comptable | `CG_NUM` / `CG_Num` |
| `dbo.F_COMPTEA` | all rows | Section analytique | `CODE` / `CA_Num` |

Operators can also define field mappings and named synchronizations in the console. A named synchronization points at one mapping and runs one way (Sage to DocuWare, or DocuWare to Sage) or both ways. The worker claims due rows, applies the chosen direction, and records success or failure. `POST /api/synchronizations/{id}/run` queues one immediately.

## Data model

Eight tables. Foreign keys are the only relationships the schema enforces. `created_by` and `updated_by` store a `user.id` as `char(36)`, and they have no foreign key.

```mermaid
erDiagram
    user {
        char id PK
        nvarchar username UK
        nvarchar password_hash
        nvarchar display_name
        nvarchar email
        bit is_active
        datetime2 created_at
        datetime2 updated_at
        datetime2 last_login_at
    }

    setting {
        nvarchar type PK
        nvarchar code PK
        nvarchar key PK
        nvarchar description
        nvarchar value
        char created_by
        datetime2 created_at
        datetime2 updated_at
        char updated_by
        bit status
    }

    mapping_table {
        int id PK
        nvarchar entity_name
        nvarchar cabinet_name
        nvarchar entity_code
        nvarchar entity_type
        nvarchar cabinet_code
        nvarchar cabinet_type
        nvarchar code UK
        nvarchar description
        char created_by
        datetime2 created_at
        datetime2 updated_at
        char updated_by
    }

    mapping_field {
        int id PK
        int id_mapping_table FK
        nvarchar entity_field_name
        nvarchar cabinet_field_name
        nvarchar entity_type_name
        nvarchar cabinet_type_name
        int entity_type_long
        int cabinet_type_long
        bit is_key
        int key_order
        char created_by
        datetime2 created_at
        datetime2 updated_at
        char updated_by
    }

    synchronization {
        int id PK
        int id_mapping_table FK
        nvarchar direction
        nvarchar source
        nvarchar destination
        nvarchar code UK
        nvarchar description
        nvarchar status
        int max_retries
        int timeout_seconds
        bit recurrence_enabled
        nvarchar recurrence_type
        time recurrence_time
        int interval_value
        nvarchar interval_unit
        nvarchar timezone
        bit recurrence_mondays
        bit recurrence_tuesdays
        bit recurrence_wednesdays
        bit recurrence_thursdays
        bit recurrence_fridays
        bit recurrence_saturdays
        bit recurrence_sundays
        char created_by
        datetime2 created_at
        datetime2 updated_at
        char updated_by
    }

    synchronization_filter {
        int id PK
        int synchronization_id FK
        nvarchar field_name
        nvarchar operator
        nvarchar value
        nvarchar logical_operator
        int sort_order
        char created_by
        datetime2 created_at
        datetime2 updated_at
        char updated_by
    }

    synchronization_run {
        uniqueidentifier id PK
        int synchronization_id FK
        nvarchar status
        datetime2 start_at
        datetime2 completed_at
        nvarchar error_message
        int records_inserted
        int records_updated
        datetime2 created_at
    }

    synchronization_record {
        uniqueidentifier id PK
        int synchronization_id FK
        uniqueidentifier last_run_id FK
        nvarchar entity_id
        nvarchar docuware_id
        nvarchar error
        datetime2 created_at
        datetime2 updated_at
    }

    mapping_table ||--o{ mapping_field : "fk_mapping_field_table"
    mapping_table ||--o{ synchronization : "fk_synchronization_mapping"
    synchronization ||--o{ synchronization_filter : "fk_sync_filter_synchronization"
    synchronization ||--o{ synchronization_run : "fk_synchronization_run_synchronization"
    synchronization ||--o{ synchronization_record : "fk_synchronization_record_synchronization"
    synchronization_run ||--o{ synchronization_record : "fk_synchronization_record_last_run"
```

| Table | Role |
|---|---|
| `user` | Operator account. `username` is unique. `is_active` defaults to `1`. |
| `setting` | DocuWare, Sage, and sync key/value rows. Primary key is `(type, code, key)`. `status` defaults to `0`. |
| `mapping_table` | One Sage entity paired with one DocuWare cabinet. `code` is unique. The entity and cabinet identity together are unique. |
| `mapping_field` | Field pair on a mapping. Unique on `(id_mapping_table, entity_field_name)`. Deleting the mapping deletes its fields. `is_key` defaults to `0`. |
| `synchronization` | Named job on one mapping. `code` is unique. `status` is `success`, `failed`, or null. Recurrence is `DAILY`, `WEEKLY`, or `INTERVAL` when enabled. |
| `synchronization_filter` | Ordered predicate on a job. `logical_operator` defaults to `AND`. Deleting the job deletes its filters. |
| `synchronization_run` | One execution. `status` is `pending`, `running`, `success`, or `failed`. Counts must be zero or greater. `completed_at` is null or at or after `start_at`. |
| `synchronization_record` | Last outcome of one source row. Unique on `(synchronization_id, entity_id)`. `last_run_id` points at the run that wrote it. |

`mapping_field.id_mapping_table` and `synchronization_filter.synchronization_id` cascade on delete. The other foreign keys do not.

## Class model

Namespaces under `DocuWareSageConnector`. Controllers depend on application interfaces. Infrastructure and the DocuWare and Sage adapters implement those interfaces. Domain types do not reference the DocuWare SDK or `Microsoft.Data.SqlClient`.

### Domain

```mermaid
classDiagram
    class SageEntityRecord {
        +string Key
        +int CbMarq
        +IReadOnlyDictionary Columns
    }

    class DocuWareDocumentInfo {
        +int Id
        +string Title
        +IReadOnlyList~IndexFieldValue~ Fields
    }

    class IndexFieldValue {
        +string Name
        +IndexFieldType Type
        +object Value
    }

    class FieldMapping {
        +string DocuWareField
        +string SageColumn
        +IndexFieldType Type
        +bool IsKey
        +bool SkipSageWrite
        +bool IsComputed
    }

    class SyncTrackingRecord {
        +Guid Id
        +int SynchronizationId
        +SyncDirection Direction
        +EntityType EntityType
        +string SageNumber
        +int DocuWareDocumentId
        +SyncStatus Status
        +int RetryCount
        +string ErrorMessage
        +string Fingerprint
        +BindSynchronization()
        +SerializeFile()
    }

    class EntityMapper {
        <<static>>
        +MapSageToDocuWare()
        +MapDocuWareToSage()
        +WritableSageColumns()
    }

    class SyncDirection {
        <<enumeration>>
        SageToDocuWare
        DocuWareToSage
    }

    class EntityType {
        <<enumeration>>
        Supplier
        ChartOfAccounts
        AnalyticSection
    }

    class SyncStatus {
        <<enumeration>>
        Pending
        Processing
        Completed
        Failed
        Skipped
    }

    class IndexFieldType {
        <<enumeration>>
        Text
        Numeric
        DateTime
    }

    DocuWareDocumentInfo "1" *-- "many" IndexFieldValue
    IndexFieldValue --> IndexFieldType
    FieldMapping --> IndexFieldType
    SyncTrackingRecord --> SyncDirection
    SyncTrackingRecord --> EntityType
    SyncTrackingRecord --> SyncStatus
    EntityMapper ..> SageEntityRecord : reads columns
    EntityMapper ..> IndexFieldValue : builds
    EntityMapper ..> FieldMapping : uses
```

`ErrorClass` is `Transient` or `Permanent`. `DocuWareAuthenticationMode` is `UserPassword` or `AppRegistration`. `SageAuthenticationMode` is `Windows` or `Sql`.

### Application

```mermaid
classDiagram
    class ISynchronizationOrchestrator {
        <<interface>>
        +RunCycleAsync()
    }

    class SynchronizationOrchestrator

    class ISyncWorkQueue {
        <<interface>>
        +EnqueueAsync()
        +ReadAllAsync()
    }

    class SyncWorkQueue

    class ISyncStatusSnapshot {
        <<interface>>
        +LastCycleAt
        +CycleInProgress
        +BeginCycle()
        +Record()
        +EndCycle()
    }

    class SyncStatusSnapshot

    class ISageToDocuWareSyncUseCase {
        <<interface>>
        +ExecuteAsync()
    }

    class SageToDocuWareSyncUseCase

    class IDocuWareToSageSyncUseCase {
        <<interface>>
        +ExecuteAsync()
    }

    class DocuWareToSageSyncUseCase

    class IMappedSageToDocuWareSync {
        <<interface>>
        +ExecuteAsync()
    }

    class MappedSageToDocuWareSync

    class SynchronizationWorker {
        +ExecuteAsync()
    }

    class SyncCycleRequest {
        +EntityType EntityType
        +int SynchronizationId
        +bool SageToDocuWare
        +bool DocuWareToSage
        +Guid RunId
    }

    class SyncCycleResult {
        +Guid SyncId
        +IReadOnlyList~EntitySyncResult~ Entities
    }

    ISynchronizationOrchestrator <|.. SynchronizationOrchestrator
    ISyncWorkQueue <|.. SyncWorkQueue
    ISyncStatusSnapshot <|.. SyncStatusSnapshot
    ISageToDocuWareSyncUseCase <|.. SageToDocuWareSyncUseCase
    IDocuWareToSageSyncUseCase <|.. DocuWareToSageSyncUseCase
    IMappedSageToDocuWareSync <|.. MappedSageToDocuWareSync
    SynchronizationWorker --> ISyncWorkQueue
    SynchronizationWorker --> ISynchronizationOrchestrator
    SynchronizationOrchestrator --> ISageToDocuWareSyncUseCase
    SynchronizationOrchestrator --> IDocuWareToSageSyncUseCase
    SynchronizationOrchestrator --> IMappedSageToDocuWareSync
    SynchronizationOrchestrator --> ISyncStatusSnapshot
    SynchronizationOrchestrator ..> SyncCycleRequest
    SynchronizationOrchestrator ..> SyncCycleResult
```

Store contracts and the rows they read and write:

```mermaid
classDiagram
    class IEntityMappingStore {
        <<interface>>
        +ListAsync()
        +CreateAsync()
        +AddFieldAsync()
    }

    class SqlEntityMappingStore

    class ISynchronizationStore {
        <<interface>>
        +ListAsync()
        +ClaimAsync()
        +CompleteAsync()
        +ListFiltersAsync()
    }

    class SynchronizationStore

    class ISynchronizationExecutionStore {
        <<interface>>
        +ListRunsAsync()
        +UpsertRecordAsync()
        +ListRecordsAsync()
    }

    class SynchronizationExecutionStore

    class IConnectorIdentity {
        <<interface>>
        +LoginAsync()
        +AuthenticateAsync()
    }

    class ConnectorIdentityStore

    class IDocuWareSettingsStore {
        <<interface>>
    }

    class DocuWareSettingsStore

    class ISageSettingsStore {
        <<interface>>
    }

    class SageSettingsStore

    class ISynchronizationSettingsStore {
        <<interface>>
    }

    class SynchronizationSettingsStore

    class IConfigurationCatalog {
        <<interface>>
    }

    class ConfigurationCatalogStore

    class ISyncTrackingStore {
        <<interface>>
        +UpsertAsync()
        +ListErrorsAsync()
    }

    class SqlSyncTrackingStore

    class IDocuWareDocumentService {
        <<interface>>
        +FindByKeyAsync()
        +CreateDocumentAsync()
        +FindInCabinetAsync()
    }

    class DocuWareDocumentService

    class ISageEntityRepository {
        <<interface>>
        +GetAllAsync()
        +UpdateAsync()
        +InsertAsync()
    }

    class SageSqlEntityRepository

    class ISageRepositoryFactory {
        <<interface>>
        +Get()
    }

    class SageRepositoryFactory

    IEntityMappingStore <|.. SqlEntityMappingStore
    ISynchronizationStore <|.. SynchronizationStore
    ISynchronizationExecutionStore <|.. SynchronizationExecutionStore
    IConnectorIdentity <|.. ConnectorIdentityStore
    IDocuWareSettingsStore <|.. DocuWareSettingsStore
    ISageSettingsStore <|.. SageSettingsStore
    ISynchronizationSettingsStore <|.. SynchronizationSettingsStore
    IConfigurationCatalog <|.. ConfigurationCatalogStore
    ISyncTrackingStore <|.. SqlSyncTrackingStore
    IDocuWareDocumentService <|.. DocuWareDocumentService
    ISageEntityRepository <|.. SageSqlEntityRepository
    ISageRepositoryFactory <|.. SageRepositoryFactory
    SageRepositoryFactory --> ISageEntityRepository

    SqlEntityMappingStore ..> mapping_table
    SqlEntityMappingStore ..> mapping_field
    SynchronizationStore ..> synchronization
    SynchronizationStore ..> synchronization_filter
    SynchronizationExecutionStore ..> synchronization_run
    SynchronizationExecutionStore ..> synchronization_record
    ConnectorIdentityStore ..> user
    DocuWareSettingsStore ..> setting
    SageSettingsStore ..> setting
    SynchronizationSettingsStore ..> setting
```

API controllers are `AuthController`, `HealthController`, `ConfigurationController`, `CabinetsController`, `MappingsController`, `SynchronizationsController`, and `SyncController`. Each is a `ControllerBase`. Mapping and synchronization requests use `EntityMappingDto`, `EntityMappingFieldDto`, `SynchronizationRecordDto`, `SynchronizationFilterDto`, `SynchronizationRunDto`, and `SynchronizationSourceRecordDto` in `Application/DTOs`.

## Quick start

Requirements: .NET 10 SDK, Node.js 22 or newer, SQL Server for the connector store and for the Sage company database, and a DocuWare Cloud or on-prem Platform user. ODBC is not required. Both databases use `Microsoft.Data.SqlClient`.

```powershell
cd docuware_sage_100_connector
copy .env.example .env
```

Fill `ConnectorStore__Server`, `ConnectorStore__Database`, `ConnectorStore__LoginMode`, and `ConnectorStore__SecretsKey` in `.env`. `ConnectorStore__LoginMode` is `Windows_Login` or `SQL_Server_Login`.

- `Windows_Login` uses the Windows account that runs the API or CLI. Leave `ConnectorStore__User` and `ConnectorStore__Password` empty. That account needs access to the database.
- `SQL_Server_Login` uses a SQL Server login. Set `ConnectorStore__User` and `ConnectorStore__Password`. The instance must allow SQL Server authentication.

DocuWare, Sage, synchronization settings, mappings, named synchronizations, and operator accounts live in that SQL Server database, not in `appsettings.json`. On an empty database, the first start applies [`Database/schema.sql`](Database/schema.sql) and inserts blank setting rows. Fill those rows in SQL Server, then restart the API.

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
- UI tests: `npm test` from `frontend/`

`appsettings.Development.json` sets `Synchronization:Enabled` to `false`, so the worker does not poll. `POST /api/synchronizations/{id}/run` still queues that job. To poll locally, set `Synchronization:Enabled` to `true` in that file, or remove the key so the SQL Server value is used.

The HTTPS launch profile is `--launch-profile https` (`https://localhost:7277`).

## Add an operator

Operator accounts are rows in the SQL Server `user` table. This command does not start the API and is not an HTTP route. Run it from this folder so it can read `.env`:

```powershell
dotnet run --project Cli -- add-user --username admin
```

The command prompts for a password (8 to 256 characters) so it stays out of shell history. To pass it on the command line instead:

```powershell
dotnet run --project Cli -- add-user --username admin --password "change-me" --display-name "Admin" --email admin@example.com
```

`--display-name` and `--email` are optional. The username must be unique. Sign in with that username and password in the operator console.

## Where settings live

| Kind | Where |
|---|---|
| SQL Server connection and the secrets key | `.env` or environment variables. Not committed. |
| DocuWare, Sage, and sync settings | SQL Server `setting` key/value rows. Passwords are encrypted with `ConnectorStore__SecretsKey`. |
| Mappings and named synchronizations | SQL Server `mapping_table`, `mapping_field`, and `synchronization`. |
| Synchronization history | SQL Server `synchronization_run` and `synchronization_record`. |
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
