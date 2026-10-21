# Architecture implementation ledger and deployment requirements

This branch is in implementation. **It is not a completed delivery of the expanded plan.** The operation matrix tracks remaining adapter/client parity; full provider validation, Entra/Hello orchestration and the full v2 transport rollout must not be inferred from the database foundation.

## Database boundary

QR login adds the `QrLoginDevice` table and trusted-API-only `RegisterQrLoginDevice`, `RevokeQrLoginDevice`, and `TouchQrLoginDevice` reducers. This is additive: publish the module before the API, regenerate bindings and completion observers, and preserve existing rows. See [QR protocol, configuration and rollout](qr-login.md). Broker sessions are process-local and do not survive restarts.

- The hosted frame coordinator processes callbacks during connection/subscription establishment and resets readiness after disconnection or subscription failure.
- The typed queue holds at most 256 operations and dispatches at most 32 per frame. Each operation has a deadline, cancellation and completion. SDK 2.1.0 retains the original reducer object in its pending-call map and applies changes before its callback; reference identity distinguishes concurrent identical arguments.
- A cancellation before submission prevents dispatch. Cancellation/timeout after submission raises `CommitOutcomeUnknownException`; it is not an instruction to retry a mutation.
- Legacy string commands remain adapters during migration. Services still using legacy fire-and-forget reducers require conversion and parity checks.

## Mandatory deployment migration

Do not deploy the new module alone. Back up the database and rehearse on a restored synthetic copy. Never use `--delete-data` against an existing application database.

1. Keep the original database identity, account IDs and business rows. The changes add authority, trusted API identity, mutation receipt, sale revision, bus revision and token revision tables. Existing account password hashes are not rewritten.
2. New database initialization records its publisher as the deployment authority. Existing databases have no authority row. For those, the actual module owner must publish a migration build containing a local implementation of `ConfigureLegacyDeploymentOwner(ref string? owner)` with that **verified deployment owner's public identity**, then call `ProvisionTrustedApi` as the same owner. The default build rejects this bootstrap when the authority is absent. An application Administrator role cannot bootstrap ownership. Delete that local implementation from future builds once the authority is committed. Do not select the first remote caller as owner.
3. Generate and persist the API connection identity through its first connection, then provision it using the deployment owner connection. The [bootstrap tool](database-bootstrap.md) performs this ordering and can initialize the administrator password without manual hashing. Restart the API after provisioning. Auth reducers require a provisioned API sender; other delegated reducers reject a different acting identity from untrusted callers. Revocation removes delegation and sender-filtered secret access.
4. Existing sensitive tables retain their IDs and columns, with a new defaulted `ApiAccessPartition` column and index appended for indexed 2.1.0 visibility joins restricting them to provisioned API identities (the SpacetimeDB owner retains host-enforced access). This is an intermediate storage migration; splitting profile projections and physical private secret tables remains outstanding.
5. New installations no longer have hardcoded admin/guest passwords. The deployment owner provisions credentials using the masked prompt in `tools/DatabaseBootstrap`; the tool verifies committed password authentication. Existing passwords are unaffected.
6. Review the 2.1.0 pre-publish migration plan. The appended columns require a coordinated client binding upgrade (`--break-clients`), which disconnects old clients; this does not require deleting records. The local synthetic rehearsal used the server-issued migration token and preserved existing rows. Reject a plan requiring deletion or manual migration until that migration has been explicitly rehearsed.
7. Regenerate with the pinned CLI 2.1.0, then run `python3 tools/generate-reducer-observers.py`. Do not remove generated nullable columns based on older migration notes.

## OpenIddict compatibility

OpenIddict remains 4.1.0. Authorization storage remains disabled; PKCE metadata stays in the token payload/properties. The new entity separates stable ID, database ID and mutable reference ID. Creation/update/deletion await committed callbacks. Full replacements store every nullable field and use a revision comparison to reject concurrent redemption. No process-local mapping cache is needed.

Old tokens whose mappings existed only in static dictionaries cannot be reconstructed reliably; require reauthentication during coordinated rollout. Do not guess stable IDs from reference IDs. New token mappings survive service reconstruction/reconnect. Test full authorization code/PKCE/refresh flows before production rollout.

Outside Development, configure `OpenIddict:Issuer`, `OpenIddict:SigningCertificatePath` and a private signing certificate. Supply its password through the existing secret configuration system. Certificate thumbprints provide stable key IDs. Preserve retired verification keys during a planned rotation; a certificate replacement is not a transparent migration of already issued tokens.

`JwtSettings:Secret` must encode exactly 32 UTF-8 bytes for the existing AES key. For an existing longer secret that was previously truncated, explicitly enable `JwtSettings:AllowLegacyKeyTruncation` during migration to preserve token compatibility. Short secrets are rejected; zero padding is no longer accepted. Never print key material in diagnostics.

## Sales

Create/update/delete v2 reducers store actor-scoped operation receipts atomically. Reusing an operation ID with different arguments is rejected. Edits retain the original sale ID, seller, time, ticket and amount. Current ticket prices must not be used to rewrite historical revenue.

New rows have an explicit known recorded amount, including valid zero-price sales. Historical nonzero stored totals can be reported; historical zero values are unknown because they were also the old default. The income summary reports a known subtotal and unknown count. Legacy scalar income queries fail explicitly on incomplete history.

Clients should persist an operation ID until the result is known, query/reconcile it after connection loss, and submit expected revisions when editing. HTTP mutations accept `Idempotency-Key`. The broad client reconciliation workflow remains outstanding.

## Validation ledger

Authoritative per-check results and tested input hashes are in the workspace validation record, outside the repository. API/database and Avalonia 12 baseline builds passed. The revoked Avalonia 11 package was replaced through the authorized dependency upgrade, with package signature checking retained. Windows account support is implemented but requires platform validation; see [provider configuration](windows-authentication.md). Real Windows/domain, Microsoft callbacks, Hello, desktop/mobile visual checks and security workflows remain outstanding. See [packaging](../../packaging/README.md) for Flatpak/MSIX/App Installer tooling and platform gates.

`tests/Architecture.Integration` exercises actual linked module code via the local synthetic database. `tests/DatabaseFixture` supplies bounded seed data while retaining production tables/reducers. It must never be published to a real application database. Fixture validation does not establish readiness of the production demo-data initializer.


## Additional migration details

The pinned SpacetimeDB 2.1.0 runtime requires **indexed equality joins** for protected table subscriptions. A cross join can publish and still reject non-owner subscriptions. Protected tables now append a defaulted, indexed `ApiAccessPartition = 0` column and join it to the provisioned API identity table. Synthetic migration was rehearsed with AutoMigrate and a server-issued migration token; it retained records. Coordinate regenerated clients and use the server-approved break-clients migration policy. Never bypass a rejected migration by clearing data.

Fresh production initialization creates deployment authority, the initial account, roles and permissions. Business demo data is now an explicit owner-only `SeedDemoData(stage)` operation (stages 1–8) for synthetic environments. Existing production records are not reseeded. WASI 2.1.0 role initialization trapped on runtime Unicode casing; optional role normalization now uses deterministic ASCII casing and leaves Unicode names exact. Legacy password hash casing remains compatible.

Application and scope stores now await committed metadata/revision updates, persist localized names and properties, and restore them after reconnect. Legacy updates advance the corresponding revision so stale store writers cannot overwrite them unnoticed. Second-factor consumption is atomic and refuses replay, mutation of challenge identity, expiry extension and deactivated accounts.

Native token files are bound to the server origin. Windows uses CurrentUser DPAPI; other platforms currently keep sessions in memory. Legacy token files with adjacent encryption keys are removed; require reauthentication. Avalonia 12 uses the compatible ReactiveUI, Semi, WebView and DataGrid packages; the LiveCharts Avalonia 12 adapter is currently a pinned prerelease. MAUI is aligned to .NET 11 RC2 and the matching Avalonia MAUI RC2 dependency set, with its SDK scoped by a project-local global.json.

Four legacy authentication counter names previously fell through to zero. The corrected counters initialize above the existing maximum ID, preserving existing challenge/credential rows, and reject unknown counter names. Repeated second-factor creation must be tested on an already populated database; a fresh database alone masked this failure.

The legacy login HTTP adapter now distinguishes a required second factor from rejected primary authentication. Previously its early `!Success` check returned 401 before reaching the second-factor response. Native OAuth uses public-client PKCE without embedding a client secret. OAuth WebView navigation preserves the configured HTTPS scheme, and callback diagnostics strip credentials, query strings, fragments and state values.

## Shared operations and reporting rollout

The initial `bru.api.v2` implementation and shared client are described in [realtime-v2.md](realtime-v2.md). Deploy API and client together for the new server report projections. `BRU_TRANSPORT=http` rolls the migrated reads back to their shared HTTP adapters without reverting database safety changes. Configure `Business:TimeZone` explicitly for business-date reporting. Windows Negotiate is pinned to 9.0.21; platform authentication still requires domain verification. Debug and Linux self-contained Release builds passed after guarding debug-tools attachment from Release compilation.

## Fleet migration

The additive `BusRevision` table treats existing vehicles as revision zero until their first mutation. Legacy reducers advance the same revision as v2 writers. Bus receipts share the existing actor-scoped receipt table and preserve the exact created ID. Deploy the module, regenerated 2.1.0 bindings/API and client together. The unchanged bus schema now receives capacity, type, year, VIN, plate, current status and active state from existing controls. Null detail fields mean preserve; empty strings clear string fields. Deletion refuses routes (including inactive routes), maintenance, location/fuel/seat/incident references; deactivate historical vehicles instead of deleting their relationships. No rows are removed by the migration.

## Dependency and CI safety

Existing package families are retained. DataProtection.Extensions is patched to 10.0.13; SQLite's native library is pinned to 2.1.14; OTLP exporter/hosting use 1.15.3 with matching 1.15 instrumentation. The standalone Services graph pins IdentityModel 6.34.0 and its compatible encoder 4.7.2, preserving OpenIddict 4.1.0. Higher API/client IdentityModel versions remain unchanged. Audit all transitive dependencies with `dotnet list <project> package --vulnerable --include-transitive --format json`; absence of reported advisories does not replace application authorization tests.

SecurityCodeScan installs all three declared SDKs and builds module, API, Aspire host, desktop and generic MAUI from their project directories. This preserves their individual SDK selection and avoids using SDK 10 for the SDK 8 WASI module or net11 MAUI. The Windows workflow still needs execution in GitHub Actions. CodeQL and DevSkim remain configured.

### Additive catalog revisions

`EntityRevision` records route, employee, job and maintenance revisions without rewriting existing business rows. Existing rows begin at revision zero; legacy writes advance the same counter as v2 mutations. New receipts are stored in the existing actor-scoped `MutationReceipt` table. Publish the module and regenerated 2.1.0 API bindings together. Legacy direct create calls now require current sender permissions; API callers must use the actor-aware v2 adapters. No accounts or business IDs are cleared.

The query-change journal is process memory, contains resource names only and is deliberately not durable. API/database reconnects change its epoch. Clients must reacquire snapshots; a previous process watermark is never accepted as current.

## Initial credentials and API-owned startup checkpoint

After publishing the additive permission/index/reducer upgrade, the deployment owner must call `EnsureDefaultPermissions`. It idempotently creates missing default grants (including jobs, schedules and permission administration) for the Administrator role without removing custom grants or rewriting business rows. Fresh initialization performs the same repair. UserRole.UserId and RolePermission.RoleId authorization lookups now use indexed equality filters.

`AuthenticateUserChecked` continues to reject invalid credentials. The API then best-effort calls trusted-API-only `RecordAuthenticationFailure` with a five-second bound; that reducer independently re-verifies the credentials and commits a Failed authentication event only for an actual failure. Valid credentials cannot forge a failure. The inactive credential-less guest is only a demo seller and cannot be claimed.

HTTP mutation adapters echo their normalized effective receipt key in `Operation-Id`; v1 realtime mutations require stable GUID operation/request IDs. V2 ping/capabilities do not consume the 10,000 counted-request allowance; native sessions rotate between operations at 9,000 counted requests. See [realtime v2](realtime-v2.md).

The module adds `BootstrapInstallation` (owner-only, initial-password and trust changes atomic) and `AuthenticateUserChecked` (rejected credentials fail the reducer). No user IDs or existing password hashes are rewritten. The API supports its own first connection followed by explicitly enabled owner-authorized provisioning and reports pending trust via readiness. The deployment owner token is used only by a temporary connection; the API's persisted token belongs to its separate generated identity. [Bootstrap instructions](database-bootstrap.md) describe masked/manual and API-startup paths, secret-file permissions, existing-password handling and legacy token cutover.

Actual synthetic database tests cover both paths, password rejection, no overwrite, scope isolation, restart identity reuse and revocation. A fresh production-module CLI smoke confirms that initialization plus initial admin-password provisioning works without manually hashing/inserting credentials. Native Windows ACL and production secret-mount configuration still require deployment-host verification.
