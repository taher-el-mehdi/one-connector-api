# Sage

Access is SQL Server via `Microsoft.Data.SqlClient`, matching the existing Python business implementation (`F_COMPTET`, `F_COMPTEG`, `F_COMPTEA`). Sage Objets Métiers / COM are not used.

## Connection

Windows Authentication (`Integrated Security`) or SQL authentication. Connection strings are built from options, never concatenated with user input.

## Queries

Column and table names are whitelisted. Values are always passed as parameters (`@key`, `@p0`, `@cbMarq`).

Supplier default filter: `CT_Type = 1`. Chart of accounts default filter: `CG_Type = 0`. Both are configurable.

## Writes

Updates require matching `cbMarq` (Sage optimistic lock). Zero rows → treated as Sage 80011 and recorded as a failed item without stopping the cycle.

Protected columns are never written back:

- Keys: `CT_Num`, `CG_Num`, `CA_Num`
- `CT_Type` (Sage trigger 80011)
- `cbCreation`, `cbModification`

Inserts into Sage happen only when `Synchronization:InsertMissingInSage` is `true`.

## Abstraction

`ISageEntityRepository` / `SageSqlEntityRepository` keep the sync engine independent from SQL so a future Sage Web Services implementation can replace the repository only.
