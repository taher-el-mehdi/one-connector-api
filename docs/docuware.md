# DocuWare

## Authentication

Default: official SDK `ServiceConnection.CreateAsync(uri, user, password, loginData)` with optional organization and `CancellationToken`. From DocuWare 7.11 / SDK 13+, this performs OAuth2 internally. The connector does not reimplement the Python REST password grant.

`AuthenticationMode = AppRegistration` obtains an access token from the Identity Service (`/Home/IdentityServiceInfo` + token endpoint) using the configured client id/secret, then calls `ServiceConnection.CreateWithJwtAsync`.

Invalid credentials are classified as permanent errors and are not retried indefinitely.

## SDK usage

The adapter `DocuWareDocumentService` uses documented SDK operations:

- Organizations and file cabinets
- Default search dialog + `DialogExpression`
- Key lookup (`NUM`, `CG_NUM`, `CODE`, or `DWDOCID`) instead of scanning the whole cabinet for single-entity sync
- Index update: `PutToFieldsRelationForDocumentIndexFieldsAsync`
- Create: `EasyUploadSingleDocumentAsync` with a JSON stub file and index fields

File cabinet resolution: configured Id if present, otherwise case-insensitive name match.

## Isolation

Controllers and Domain never reference `DocuWare.Platform.ServerClient` types.
