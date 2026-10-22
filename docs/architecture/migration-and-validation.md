# Architecture implementation ledger and deployment requirements

This branch is in implementation. **It is not a completed delivery of the expanded plan.** The operation matrix tracks remaining adapter/client parity; real provider/platform validation and the full v2 transport rollout must not be inferred from the database foundation. The [provider checkpoint](provider-authentication.md) and [session synchronization model](session-synchronization.md) distinguish implemented Entra/passkey/native refresh integration from unverified real Windows/browser/mobile platforms.

## Database boundary

QR login adds the `QrLoginDevice` table and trusted-API-only `RegisterQrLoginDevice`, `RevokeQrLoginDevice`, and `TouchQrLoginDevice` reducers. This is additive: publish the module before the API, regenerate bindings and completion observers, and preserve existing rows. See [QR protocol, configuration and rollout](qr-login.md). Broker sessions are process-local and do not survive restarts.

The QR client rollout adds display/approval/self-scan windows and shares the existing Windows-login response application/second-factor wizard. Deploy the QR-capable API before these clients. Windows device keys use DPAPI; unsupported platforms retain keys only in process memory until a verified host keystore is registered. Do not advertise durable mobile enrollment or native camera support from generic desktop/MAUI compilation alone. Client and platform caveats are detailed in [QR clients](qr-login.md#clients).

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

## Client screen migration checkpoint (batch 1)

Migrated shared-transport screens: Buses, Jobs, Employees, Maintenance and both sales-report screens (plus session/access checks). Employees and Maintenance now use fixed 100-row server pages, server search, live snapshot/invalidation synchronization, permission-driven commands and cancellable dialog services. Their conflict reads require the new authorized `employees.get` / `maintenance.get` API operations; deploy the capable API before the client. `routes.get`, `tickets.get`, `schedules.get`, `roles.get` and `permissions.get` are also available for later batches; existing `jobs.get` / `users.get` are retained, not duplicated. New single-row reads return the list's typed row/revision, are nonmutating and nonsubscribable, and return `not_found` after deletion. Per-call permission gates and generic HTTP 404 mapping apply.

### Batch 2: Routes and Tickets

Routes and Tickets now use the same typed live catalog/command/editor plumbing as batch 1, including fixed 100-row server pages/search, stable selection, current page permission flags, stale-row mutation gating, cancellation, shared HTTP rollback, retained drafts/operation IDs, explicit conflict-row replacement and receipt/delete-snapshot reconciliation. Existing view layouts remain intact except for Jobs-identical pagers. Route metadata and activation are persisted in one revisioned mutation. Ticket route display fields are typed; seat checks use visible bus capacity or rely on the server when capacity is unavailable. Driver selection preserves the first-driver-job rule through the shared bounded/subscribable `employees.drivers` query and legacy adapter.

Deploy the module with the in-place extended v2 route signatures, regenerated 2.1.0 reducer bindings, API and client together; legacy reducer signatures and shared revision counters remain compatible. Editable administrative lists now return `CatalogPageResult<T>` permission flags, not bare pages. Route list/get projections include permission-filtered active-ticket counts; ticket list/get expose matching route endpoints and duration. No business table columns or stored records are removed.

Remaining screen migrations: **Users**, **Roles**, and **Permissions**. Their legacy controller/client paths are not implicitly migrated by adding server operations. Broad client reconciliation and real native/browser/mobile validation remain incomplete. See [client rollout](realtime-v2.md#client-rollout) for draft retention, receipt-based uncertainty handling, delete snapshot reconciliation and debugging behavior. Headless tests cover migrated catalog screens, actual editor/dialog HTTP wiring and bound-row log capture; real-fixture tests cover current/list row equality, permission flags, metadata/revisions, counts, driver selection/bounds and deleted-entity failures.

### Batch 3: Route Schedules and Sales

Schedules and Sales now use the shared typed live catalogs, fixed 100-row Jobs-identical accessible pagers, cancellable server filters/search, stable selected rows, current query permission flags and stale-state mutation gating. Their dialog services retain drafts and operation IDs across close/reopen, reload conflicts via authorized current-row reads, replace drafts only on explicit action, reconcile uncertain saves by receipt and uncertain deletes by fresh snapshots. Sale ticket/date/seller/recorded amount remain immutable; creation preserves buyer/POS defaults and accepts an empty phone. Schedule editors validate days/stops/times/distances, UTC timestamps, finite prices and integral counts without inventing route distances or bus types.

Deploy the module with the extended v2 schedule command signatures and regenerated pinned-2.1.0 reducer bindings before the capable API/client. Schedule activation is atomic with metadata; existing inactive-route/recent-sale guards still apply. `sales.get` requires `sales.view`, returns the current typed row/revision, cannot be subscribed, and returns `not_found` after deletion. `sales.list` now returns `CatalogPageResult<SaleRow>` permissions and full-filtered-query known/unknown totals; current-ticket-price filters retain legacy search semantics, not historical amount semantics. Seller login labels degrade to null without `users.view`. These are contract/signature upgrades, not data deletion or a schema rewrite. Use `BRU_TRANSPORT=http` to roll migrated reads/writes back to shared HTTP adapters; do not replay uncertain writes.

Shared lifecycle/editor and production-HTTP matrices now run for both screens, including permission revocation, cancellation, unknown-delete reconciliation, immutable sale fields, lookup denial, null amount/date dumps, operation outcomes and runtime pager names/focus. Real Windows/mobile visuals and assistive-technology checks remain deployment gates.

## Fleet migration

The additive `BusRevision` table treats existing vehicles as revision zero until their first mutation. Legacy reducers advance the same revision as v2 writers. Bus receipts share the existing actor-scoped receipt table and preserve the exact created ID. Deploy the module, regenerated 2.1.0 bindings/API and client together. The unchanged bus schema now receives capacity, type, year, VIN, plate, current status and active state from existing controls. Null detail fields mean preserve; empty strings clear string fields. Deletion refuses routes (including inactive routes), maintenance, location/fuel/seat/incident references; deactivate historical vehicles instead of deleting their relationships. No rows are removed by the migration.

## Dependency and CI safety

Existing package families are retained. DataProtection.Extensions is patched to 10.0.13; SQLite's native library is pinned to 2.1.14; OTLP exporter/hosting use 1.15.3 with matching 1.15 instrumentation. The standalone Services graph pins IdentityModel 6.34.0 and its compatible encoder 4.7.2, preserving OpenIddict 4.1.0. Higher API/client IdentityModel versions remain unchanged. Audit all transitive dependencies with `dotnet list <project> package --vulnerable --include-transitive --format json`; absence of reported advisories does not replace application authorization tests.

SecurityCodeScan installs all three declared SDKs and builds module, API, Aspire host, desktop and generic MAUI from their project directories. This preserves their individual SDK selection and avoids using SDK 10 for the SDK 8 WASI module or net11 MAUI. The Windows workflow still needs execution in GitHub Actions. CodeQL and DevSkim remain configured.

The [local security validation ledger](security-validation.md) records the disabled
workflows' local equivalents, raw/triaged findings, the additional Desktop SDK
10.0.201 prerequisite, and repeatable commands. It also describes the verified
Bearer-only OAuth form authorization change and historical transport/log changes.
Current behavior is governed by [network policy](network-policy.md) and
[debug traffic diagnostics](diagnostics.md): LAN HTTP defaults, readable escaped
logs plus JSONL, credential-only masking and visible business data. The unresolved AppHost
KubernetesClient advisory requires an owner-authorized compatible dependency
upgrade; successful application builds are not a clean security signoff.

### Additive catalog revisions

`EntityRevision` records route, employee, job and maintenance revisions without rewriting existing business rows. Existing rows begin at revision zero; legacy writes advance the same counter as v2 mutations. New receipts are stored in the existing actor-scoped `MutationReceipt` table. Publish the module and regenerated 2.1.0 API bindings together. Legacy direct create calls now require current sender permissions; API callers must use the actor-aware v2 adapters. No accounts or business IDs are cleared.

The query-change journal is process memory, contains resource names only and is deliberately not durable. API/database reconnects change its epoch. Clients must reacquire snapshots; a previous process watermark is never accepted as current.

## Initial credentials and API-owned startup checkpoint

After publishing the additive permission/index/reducer upgrade, the deployment owner must call `EnsureDefaultPermissions`. It idempotently creates missing default grants (including jobs, schedules and permission administration) for the Administrator role without removing custom grants or rewriting business rows. Fresh initialization performs the same repair. UserRole.UserId and RolePermission.RoleId authorization lookups now use indexed equality filters.

`AuthenticateUserChecked` continues to reject invalid credentials. The API then best-effort calls trusted-API-only `RecordAuthenticationFailure` with a five-second bound; that reducer independently re-verifies the credentials and commits a Failed authentication event only for an actual failure. Valid credentials cannot forge a failure. The inactive credential-less guest is only a demo seller and cannot be claimed.

HTTP mutation adapters echo their normalized effective receipt key in `Operation-Id`; v1 realtime mutations require stable GUID operation/request IDs. V2 ping/capabilities do not consume the 10,000 counted-request allowance; native sessions rotate between operations at 9,000 counted requests. See [realtime v2](realtime-v2.md).

The module adds `BootstrapInstallation` (owner-only, initial-password and trust changes atomic) and `AuthenticateUserChecked` (rejected credentials fail the reducer). No user IDs or existing password hashes are rewritten. The API supports its own first connection followed by explicitly enabled owner-authorized provisioning and reports pending trust via readiness. The deployment owner token is used only by a temporary connection; the API's persisted token belongs to its separate generated identity. [Bootstrap instructions](database-bootstrap.md) describe masked/manual and API-startup paths, secret-file permissions, existing-password handling and legacy token cutover.

Actual synthetic database tests cover both paths, password rejection, no overwrite, scope isolation, restart identity reuse and revocation. A fresh production-module CLI smoke confirms that initialization plus initial admin-password provisioning works without manually hashing/inserting credentials. Native Windows ACL and production secret-mount configuration still require deployment-host verification.
# Secret storage

See [secrets and credential migration](secrets.md) for required rotation of historical keys, mounted configuration and startup policy.
# Administrative operation parity — additive module rollout

Publish the module **before** deploying the matching API/bindings. This rollout
adds 21 reducers: ticket create/update/delete; schedule create/update/delete/active;
user create/profile update/delete/active/assign-role/remove-role; role
create/update/delete/grant-permission/revoke-permission; permission
create/update/delete (all `*V2`). It reuses the existing `EntityRevision` and
`MutationReceipt` tables. Existing rows start at revision zero. Public legacy
reducer names and signatures remain available, share their core paths, and
advance revisions; deleted entities retain revision tombstones.

No data rewrite is required for this additive operation rollout. The independent
[private credential migration and compromised-secret rotation](secrets.md) still
apply. New account creation uses that private credential helper. New reducer
guards also apply to legacy writers: no self-deactivation/deletion or removal of
the last active Administrator; no deletion of assigned/system roles or sold
tickets; no Administrator permission revocation or nonadministrator privilege
escalation through grants/assignment/permission renaming.

Verify each resource with a fresh isolated fixture, including concurrent same-ID
replay, mismatched replay, legacy/v2 revision conflicts, reference failures and
permission denial. The integration harness also checks actual public projections
and access/resource invalidation journals. Api.Contracts checks capabilities,
validation before persistence, HTTP/v1 adapters, framed v2 dispatch, six new
subscription leases and the complete reflected endpoint coverage ledger.

The [coverage ledger](operation-coverage.md) has explicit dispositions for all
current controller/method/verb/route combinations. Its drift test rejects
missing/stale rows, empty dispositions and unregistered operation references.
Protocol-only auth/QR/provider/upgrade/diagnostic endpoints are not claimed as
CRUD parity; legacy read projections remain labelled. See
[administrative compatibility notes](realtime-v2.md#administrative-contracts-and-compatibility)
for profile-only user updates, activation payloads and atomic permission sets.

## Provider checkpoint — additive own-link unlink reducer

Publish the additive `UnlinkOwnExternalAccount(provider, actingUser)` reducer
before the matching API and regenerated bindings/observer. It requires a trusted
API sender, resolves the effective actor, requires an active account, and deletes
only that actor's links for the requested provider. No schema or data rewrite is
required. It intentionally does not ensure another usable credential remains.
Fresh-fixture Architecture.Integration verifies Entra `LinkOwnExternalAccount`,
committed own unlink, absent-link rejection, preservation of another user's link
and the actor's other-provider links, and untrusted-sender rejection. The WASI
build and full integration suite also include the preceding `users.create`
idempotency-hash fix without further production changes to that operation.

Entra is disabled by default and must be configured/validated with a real tenant
before rollout. Provider flows are bounded process memory and lost on restart.
The v2 refresh adapter is tested with fake orchestration; production native
orchestration and real-module family rotation/reuse are tested separately. See
the [refresh checkpoint](provider-authentication.md#refresh-checkpoint).

## Passkey storage and browser rollout

Publish the additive protected `WebAuthnUserHandle` table and trusted-API
`EnsureWebAuthnUserHandle` / `RemoveOwnWebAuthnCredential` reducers first. Preserve
all existing credential/profile rows. The legacy `DeactivateWebAuthnCredential`
signature now appends an optional acting-user argument and enforces ownership;
deploy regenerated pinned-2.1.0 bindings and the completion observers together.
Review and rehearse the server's required client-break migration plan; do not
clear data. Counter reducers now reject nonmonotonic values except 0→0; obsolete
clients that wrote arbitrary counters or removed by bare IDs are not compatible.

Handles are generated once per active user on demand using transaction-seeded
module pseudo-random bytes (not an OS CSPRNG), are opaque public identifiers and
are protected by the same trusted-API RLS partition. New registrations use those
handles; no username/identity/JWT-secret derivation is used. WebAuthn writes are
now committed; legacy stored challenges are decoded correctly and all options
require user verification. Authenticators without UV may require replacement.

Keep `Authentication:Passkeys:Enabled=false` until explicit public origin,
Fido2 RP id/origins and proxy path base are configured and validated. Then enable
the capability-gated login/key-management UI. Existing list/delete feature flags
must also permit credential management. Rollback disables the browser endpoints
and UI without deleting keys. Real-reducer integration covers persistent handle
idempotency/RLS, duplicate credentials, monotonic/zero counters and own-only
committed service/removal behavior. Real Fido2 software-authenticator tests and
headless Avalonia tests do not replace real browser/Windows Hello validation.

## Native refresh and session synchronization rollout

Publish the additive protected `RefreshTokenRecord` table and `IssueRefreshToken`,
`RotateRefreshToken`, `RevokeRefreshFamily`, `RevokeAllRefreshTokens` reducers
**before the API**, with regenerated bindings/observers. Preserve existing rows
and trusted API provisioning; rehearse any client-break plan without data deletion.
Then deploy the API and capable native clients. Password/credential changes and
account deactivation/deletion now revoke refresh families in the module.

The [session synchronization guide](session-synchronization.md) defines the
native-header/no-Origin rule, absolute lifetimes, strict duplicate rotation
(one successor then whole-family revocation), rate/retention limits, conditional
origin-bound storage and failure behavior. Older clients/browsers get access-only
sessions and must sign in at expiry. Refresh cannot repair uncertain mutations;
they are never replayed automatically. Windows DPAPI and memory-only unsupported
platform behavior remain unchanged; real platform storage/UI checks are required.

Native-refresh checkpoint validation passed Api.Contracts (including reflected
endpoint-ledger drift), Client.Presentation, Client.Infrastructure and a fresh
Architecture.Integration fixture plus catalog/fleet/bootstrap/diagnostics modes.
API and desktop nonincremental builds, production WASI publish and generic MAUI
`EnableMobileTargets=false` build passed. The existing handoff-8 warning baseline
and this checkpoint have the same 185 deduplicated path/code/message signatures;
no new warnings were introduced. This is synthetic/headless/Linux validation,
not real Windows Hello, mobile keystore or distributed-deployment certification.

## Remaining database-access and lifecycle inventory (handoff 10)

Inventory taken at `8150c2d`, before changes. Paths below are relative to
`BRU-AVTOPARK-AspireAPI/`: **Services** means `TicketSalesApp.Services/Implementations/`;
**API** means `BRU-AVTOPARK-AspireAPI.ApiService/`. Counts are textual call sites,
not a claim that every `.Result` property is a task or every dispatch is unobserved.
The installation provisioner's temporary owner connection needs separate review.
`TwoFactorService`, `UsersController` and `TicketsController` already have no
raw connection access/dispatch sites in this checkout. Generated bindings and
SpacetimeDBService's serialized SDK internals are outside the application boundary.

| File | Before | After |
| --- | --- | --- |
| Services/AdminActionLogger.cs | 3 raw accesses; 1 dispatch | Queued materialized reads and committed audit write with request cancellation; no invented receipt |
| Services/AuthenticationService.cs | 3 raw accesses; 1 dispatch | Queued reads, async role lookup and committed registration; v2 allocation when no explicit account identity |
| Services/AuthorizationStore.cs | 14 raw accesses/accessor declarations; 4 dispatches | Queued cancellable detached reads and committed create/update/delete/prune; removes polling/pending-task caches and PKCE/property-value logs |
| Services/DataService.cs | 5 raw accesses; 11 dispatches | Queued materialized reads; Core-backed writes committed with typed readback; no invented receipts |
| Services/DatabaseInstallationProvisioner.cs | 1 temporary-owner dispatch | Sole justified direct dispatch: temporary owner connection cannot use the API's trust-gated queue; bounded/cancellable own committed observer, unsubscribed on every exit |
| Services/MagicLinkService.cs | 3 raw accesses; 2 dispatches | Queued reads; committed create/use, no send before commit |
| Services/OpenIdConnectService.cs | 1 raw access | Queued role projection |
| Services/PermissionService.cs | 8 raw accesses; 3 dispatches | Queued materialized reads; Core-backed committed create/update/delete |
| Services/QRAuthenticationService.cs | 3 raw accesses; 1 dispatch | Queued reads/async role lookup; committed legacy audit dispatch; no cache-row mutation or parsed proof-value logs |
| Services/RoleService.cs | 9 raw accesses; 8 dispatches | Queued materialized reads; existing v2 composite create/update commits metadata and permission sets atomically; other Core-backed writes committed |
| Services/RouteScheduleService.cs | 9 raw accesses; 5 dispatches | Queued reads/committed Core-backed writes; create uses existing v2 receipt, removes notes tagging and async-void callbacks; disposal cancels service lifetime |
| Services/SettingsService.cs | 6 raw accesses; 5 dispatches; fallback disables 2FA on errors | Queued reads/committed writes; errors no longer fabricate 2FA-disabled defaults |
| Services/SpacetimeDBService.cs | 1 public raw accessor declaration | Removed public accessor; serialized SDK owner only |
| Services/SpacetimeDBService.Changes.cs and .ReducerObservers.cs | Anonymous connection-owned observers with no explicit detach | Generator emits paired unsubscribe actions; disconnect releases all and reconnect installs exactly one set |
| Services/SpacetimeDBService.cs HTTP helpers | Per-call clients and undisposed responses | Process-lifetime handler pool, per-request authorization/deadline and disposed requests/responses |
| Services/TicketSalesService.cs | 9 raw accesses | Queued reads; report joins and aggregation run within one serialized read callback |
| Services/TicketService.cs | 13 raw accesses; 7 dispatches (includes commented obsolete call) | Queued reads/committed Core-backed writes; obsolete payment-field update returns false instead of unsubmitted success |
| Services/TotpService.cs | 6 raw accesses; 5 dispatches | Queued reads/committed writes; challenge consumption uses atomic replay-checking reducer; HMAC disposed |
| Services/UserService.cs | 8 raw accesses; 3 dispatches | Queued materialized reads; committed v2 create allocates distinct account identity; Core-backed update/delete; no sleep polling |
| TicketSalesApp.Services/Interfaces/ISpacetimeDBService.cs | 1 raw accessor declaration | Removed; request callers use the bounded serialized operations |
| API/Controllers/AuthController.cs | 2 raw accesses | Queued cancellable detached profile reads |
| API/Controllers/DebugController.cs | 1 raw access | Queued cancellable bounded-page rendering; property/bearer payload values omitted from logs |
| API/Experimental/Services/Implementations/AuthOrchestrationService.cs | 9 raw accesses; 2 dispatches | Queued reads; committed claim/settings; async role/token calls; no polling |
| API/Experimental/Services/Implementations/IdentityService.cs | 2 raw accesses | Async queued identity/profile reads |
| API/Experimental/Services/Implementations/IdentityService.cs HTTP identity request | Per-call custom handler disabled certificate verification and logged raw identity JSON | Factory-pooled verified-TLS client; disposed request/response; raw response/error body logging removed |
| API/Experimental/Services/Implementations/ProfileService.cs | 1 raw access | Queued projected profile read |
| API/Experimental/Services/Implementations/TokenService.cs | 2 raw accesses | Async queued active-profile/role/permission snapshot for signing |
| API/Controllers/BaseController.cs | 8 sync-over-async wrappers | Removed; all controller callers await authentication/access/identity checks |
| API/Experimental/Services/Implementations/HtmlRenderingService.cs | 16 sync-over-async rendering wrappers | Return rendering tasks; interfaces and all controller callers await them |
| API/Program.cs | 1 detached Task.Run startup task; fatal startup logging | OAuth registration owned by cancellable/observed BackgroundService; readiness waits cancel on shutdown; fatal logs remain startup-only |
| API/Controllers/RealtimeController.cs | 1 detached Task.Run connection task | Streams own tracked completions/CTS; stop/disconnect cancels, awaits and disposes all; heartbeat/receive joined before socket disposal |
| API/Realtime/Infrastructure/SignalRRealtimeEventBus.cs | 1 synchronous semaphore wait during disposal | Short synchronous channel/dictionary gate replaces disposable semaphore; repeated sync/async/DI disposal safe |

`DatabaseAccessPolicyTests` now scans application C# for raw connections,
reducer dispatches and task blocking, with the sole startup owner-bootstrap
dispatch exception explained above. Non-task MVC/verification/tuple result
properties are explicitly distinguished
from blocking task waits. Negative fixtures prove new raw access and task blocking
are rejected. `LogHygieneTests` checks credential-bearing log templates/arguments,
including disguised value placeholders; negative fixtures include mixed safe
fingerprint plus unsafe secret arguments. Fingerprints, derived counts, stable
storage IDs and protocol outcome categories are not credential values. Fatal
severity is confined to process startup. Raw QR verification values, bearer
prefixes, claim values and debug property/bearer payload dumps were removed.

The serialized database boundary now detaches generated row results using the
pinned SDK's BSATN row serializer, including row lists/arrays and tuple results,
before releasing them to callers. Mutating a returned entity cannot modify the
SDK cache. Application DTO projections must contain scalar/projected data, not
SDK table handles. Real-fixture tests check that modifying a returned permission
does not alter a subsequent read until the update reducer commits.

`ServiceLifecycleTests` checks cancellable startup shutdown and observed failure,
DI-disposed schedule cancellation and repeated event-bus stop/sync/async disposal
with active subscription completion under a two-second deadline.

`RequestTraceMiddleware` scopes all application request logs with the existing
`HttpContext.TraceIdentifier` and returns it in `Trace-Id` on every HTTP response,
including errors and early validation/routing failures. This preserves existing
body shapes and statuses; application error bodies and ProblemDetails also carry
`traceId`. It runs before routing/validation/exception handling. RequestTraceTests
checks correlation and response compatibility.

Observer ownership is checked against the real SDK: the private unsubscribe list
is nonempty while connected, empty after disconnect and restored to exactly the
same count after reconnect. Regenerate both observer files with
`python3 tools/generate-reducer-observers.py` after binding changes.

Legacy service interfaces need not grow a cancellation parameter to honor HTTP
disconnects: default-token queued reads/writes inherit `RequestAborted` from the
DI-provided context accessor. Explicit cancellation tokens remain supported;
startup OpenIddict calls propagate the host stopping token through the managers
and stores. Tests verify pre-dispatch request-abort cancellation and all three
startup application-manager paths.

Handoff-10 final validation passed Api.Contracts (198-route drift ledger plus
database/log policy, cancellation, disposal and actual exception-pipeline trace
tests), Client.Presentation, Client.Infrastructure and Architecture.Integration
on a newly published isolated fixture, followed by catalog/fleet/bootstrap/
diagnostics modes. API/desktop/generic-MAUI nonincremental builds and production
WASI publish passed with zero errors; `git diff --check` passed. Comparing
deduplicated path/code/message warning signatures against handoff 9 found 179
current versus 185 baseline, **zero new signatures** (six removed).
Real platform UI/keystore/Negotiate deployment and distributed production load
remain outside this synthetic/headless/Linux checkpoint. No production module
schema change is required by this cleanup; only synthetic fixture helpers changed.
