# Configuration

Connector settings are loaded from MySQL. `appsettings.json` contains logging and hosts only. The keys below are the shape the API binds after reading the active profile.

## Sections

### DocuWare

| Key | Purpose |
|---|---|
| `PlatformUrl` | Platform URL, for example `https://host/DocuWare/Platform` |
| `Organization` | Optional organization name. Empty = first organization |
| `AuthenticationMode` | `UserPassword` or `AppRegistration` |
| `UserName` / `Password` | UserPassword (and App Registration password grant) |
| `ClientId` / `ClientSecret` / `Scope` | App Registration |
| `FileCabinets:Supplier:Name` / `Id` | Id wins; name is used when Id is empty |
| `FileCabinets:ChartOfAccounts:Name` / `Id` | Same |
| `FileCabinets:AnalyticSection:Name` / `Id` | Same |

Cabinet GUIDs from any other environment must not be committed. Resolve by name when Id is empty.

### Sage

| Key | Purpose |
|---|---|
| `Server` | SQL Server instance |
| `Database` | Sage company database |
| `Authentication` | `Windows` or `Sql` |
| `UserName` / `Password` | SQL authentication only |
| `TrustServerCertificate` | TLS certificate handling |
| `SuppliersOnly` | `true` → `CT_Type = 1` |
| `ChartOfAccountsTypeZeroOnly` | `true` → `CG_Type = 0` |

### Synchronization

| Key | Purpose |
|---|---|
| `Enabled` | Worker polling on/off. Manual API queue still works |
| `IntervalSeconds` | Polling interval. Never hardcoded |
| `MaxRetries` | Transient retry cap |
| `FirstRetryDelaySeconds` | Exponential backoff base |
| `InsertMissingInSage` | Default `false` |
| `ApplySageWrites` | Default `true` for the worker |
| `SageToDocuWare` / `DocuWareToSage` | Enable each direction |

### Tracking

Synchronization state is the MySQL table `logs`.

## Connector store

The process needs a MySQL connection before it can read DocuWare, Sage, synchronization, or tracking settings. Put that connection in `.env` (or real environment variables). `.env` is not committed. Copy `.env.example`.

| Key | Purpose |
|---|---|
| `ConnectorStore__Host` | MySQL hostname. `localhost` only when MySQL runs on this computer. On Hostinger, use the hostname from hPanel. |
| `ConnectorStore__Port` | Default `3306` |
| `ConnectorStore__Database` | Database name |
| `ConnectorStore__User` | Database user |
| `ConnectorStore__Password` | Database password |
| `ConnectorStore__SslMode` | `Preferred`, `Required`, or `None` |
| `ConnectorStore__SecretsKey` | Base64 32-byte key. Encrypts DocuWare and Sage passwords at rest. The same key must stay with the data. |

Startup applies the SQL in `Database/Migrations`. While `setting` or `user` is empty, the API imports `Database/seed.local.json` once. That file is not committed. After the import, change rows in MySQL and restart the API.

In Development, `appsettings.Development.json` forces `Synchronization:Enabled` to `false` so the worker does not poll. The database keeps the stored value.

| Table | Columns | Role |
|---|---|---|
| `setting` | `type` (`Docuware`, `Sage`, `synchronization`), `code`, `description`, `key`, `value` | One row per setting. Each configuration code keeps the keys for its type |
| `user` | account columns | Operators. `password_hash` is an ASP.NET Identity hash |
| `logs` | id_synchronization, status, error_message, file (JSON), timestamps | Synchronization state |

Cabinet names stay the code defaults (`Fournisseur`, `Plan Comptable`, `Section analytique`). Synchronization state is `logs`.

`POST /api/auth/login` checks `user` and returns a signed bearer token. The UI keeps that token in `sessionStorage`. Operator passwords are not in the frontend. `GET /api/health` stays open. Every other API route requires `Authorization: Bearer`.

## Secrets

Do not put the MySQL password, the secrets key, DocuWare passwords, or operator passwords in source control.

When `Synchronization:Enabled` is `true`, startup validation requires Platform URL, DocuWare user (for UserPassword), Sage server, and Sage database.

`GET /api/configuration` returns the loaded options for the React app. Passwords and `ClientSecret` are booleans (`passwordConfigured`, `clientSecretConfigured`). The API does not accept configuration writes.

`Cors:AllowedOrigins` lists browser origins allowed to call the API. Development allows `http://localhost:5173`. An empty list does not allow every origin. In production set `Cors__AllowedOrigins__0` to the UI origin.

