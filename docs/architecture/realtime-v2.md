# Realtime v2 rollout and debugging

`GET /api/realtime/stream` accepts `bru.api.v2`. The existing `bru.events.v1` endpoint remains available. V1/V2 transport follows the shared [network policy](network-policy.md): default `lan` permits LAN HTTP/WS; `never` requires HTTPS/WSS even on loopback. Browser origins must exactly match `Realtime:AllowedOrigins`; native clients send no Origin. Configure forwarded headers for trusted proxies before relying on remote-address limits.

Capability negotiation is authoritative: operations absent from it are not implemented in v2. Administrative operation parity now includes tickets, route schedules, users, roles and permissions, with receipts and revisions. Provider ceremony binding and broad screen migration remain separate work. The [endpoint ledger](operation-coverage.md) distinguishes shared adapters from retained legacy reads and protocol-only endpoints; it is not a blanket all-endpoint parity claim.

## Messages

The server first sends a `welcome` result with protocol, operation descriptors, authentication methods, database state, connection ID and limits. Commands use:

```json
{"requestId":"a4eb970bff5040698f4f3b4b54f802bf8","operation":"sales.list","payload":{"page":1,"pageSize":100}}
```

Results contain `type`, `requestId`, `success`, optional `data`/`code`, `traceId`, and session `generation`. Use a new 32-character GUID (`N` format) for every counted request. A connection closes after 10,000 counted request IDs to bound duplicate tracking. `ping` and `capabilities` are validated and subject to the per-minute command budget, but exempt from this cap and duplicate-ID retention. The shared client exposes `CountedRequestsSent`; the native coordinator rotates at 9,000 counted requests under its gate between operations when no requests are outstanding, then reacquires snapshots.

The receiver, command worker and writer are separate. Each connection accepts at most eight outstanding operations, 64 KiB per command, 1 MiB per response, and 32 queued responses. The command deadline includes queue time. Commands execute serially, so session changes cannot race a business operation. Queued commands from an earlier session generation are rejected. Cancel uses `{"operation":"cancel","payload":{"requestId":"..."},"requestId":"new-guid"}` and can interrupt the worker. A cancellation after reducer submission can still mean `commit_outcome_unknown`.

Connections are limited to 128 per process and four per source address. Authentication attempts share a ten-per-minute source-address budget across reconnects. Commands are capped at 180 per minute per connection. Anonymous sessions have a 45-second deadline; a pending second factor has two minutes. Idle authenticated connections close after 90 seconds. A slow outbound write has ten seconds to complete.

## Implemented operations

| Operation | Permission / behavior |
| --- | --- |
| `capabilities`, `ping` | Anonymous allowlist |
| `auth.attach` | Validate supplied bearer token; establish a new generation |
| `auth.login` | Existing password login and local second-factor policy |
| `auth.totp` | Uses the challenge held by this socket; clients cannot substitute another challenge |
| `auth.logout` | Clear this socket's credential/challenge; token revocation elsewhere remains a separate operation |
| `buses.list` | Paged fleet DTOs and authorized route/maintenance counts |
| `buses.create/update/delete/set-active` | Fleet permissions, actor-scoped receipts, revision conflicts and transactional reference checks |
| `session.access` | Active account; current database roles and permissions |
| `sales.list` | `sales.view`; deterministic paged detached DTOs |
| `sales.create`, `sales.update`, `sales.delete` | Matching create/edit/delete permission and transactional reducer checks |
| `mutations.receipt` | Active account; only that actor's receipt |
| `reports.income`, `reports.sales` | `sales.view`; recorded amounts and explicit historical gaps |
| `routes.list`, `employees.list`, `jobs.list`, `maintenance.list` | Corresponding `.view`; paged catalog projections |
| `routes.get`, `employees.get`, `jobs.get`, `maintenance.get`, `tickets.get`, `schedules.get`, `roles.get`, `permissions.get` | Corresponding `.view`; current typed row and revision; missing entity is `not_found` (HTTP 404); these reads cannot be subscribed |
| `routes.create/update/delete/set-active`, `employees.create/update/delete`, `jobs.create/update/delete`, `maintenance.create/update/delete` | Corresponding create/edit/delete permissions and committed receipts |
| `tickets.list` | `tickets.view`; paged/searchable route, seat, price, payment, purchase time, active state and revision |
| `tickets.create`, `tickets.update`, `tickets.delete` | `tickets.create/edit/delete`; finite price 0..1e12, route/seat checks and transactional sale-reference protection |
| `schedules.list` | `schedules.view`; paged route/bus/driver labels, times, active state and revision |
| `schedules.create`, `schedules.update`, `schedules.delete`, `schedules.set-active` | `schedules.create/edit/delete`; set-active uses edit and retains route/recent-sale activation rules |
| `users.list`, `users.get` | `users.view`; credential-free profile and role-name projection; numeric legacy user ID |
| `users.create`, `users.update`, `users.delete`, `users.set-active` | `users.create/edit/delete`; private credential provisioning on create, profile-only update, transactional self/last-active-Administrator guards |
| `users.assign-role`, `users.remove-role` | `assign_roles`; assigning Administrator requires an Administrator; last active Administrator cannot be removed |
| `roles.list` | `roles.view`; permission names/IDs, assigned-user count and revision |
| `roles.create`, `roles.update`, `roles.delete` | `roles.create/edit/delete`; assigned/system roles protected; optional permission sets commit atomically with metadata and separately require grant authority |
| `roles.grant-permission`, `roles.revoke-permission` | `grant_permissions`; nonadministrators cannot grant unheld permissions; Administrator permission revocation refused |
| `permissions.list`, `permissions.create`, `permissions.update`, `permissions.delete` | `permissions.view/create/edit/delete`; assigned definitions cannot be deleted; all writes return receipts |

Session attach revalidates token proof; every protected command revalidates its token and current database permissions. Query subscriptions reauthorize on delivery and expire their snapshot lease when committed data changes. Password/TOTP orchestration shares the existing authentication service. Windows/Entra/WebAuthn browser and platform ceremonies must not be replaced with client-supplied usernames or biometric data.

`auth.refresh {refreshToken}` supports native single-use refresh families after
access expiry. It requires the native opt-in handshake header and no Origin,
uses shared authentication budgets, revalidates the replacement access token,
advances generation and invalidates every lease with `session_changed`. Reuse
commits whole-family revocation rather than returning a successor. Concurrent
duplicates deliberately create one successor then revoke the whole family.
The native client currently rotates over HTTP, conditionally stores the pair and
rebinds its transport. One shared flight recovers concurrent reads/subscriptions;
reads retry once and subscriptions reacquire snapshots. Mutations never refresh
or retry automatically. See [session synchronization](session-synchronization.md)
for storage, failure/cooldown rules, native-only issuance and rollout order.

Sale, fleet, catalog and administrative mutations require a stable `operationId` in the envelope. The ID is scoped to actor and payload by the database. After disconnect or `commit_outcome_unknown`, query `mutations.receipt`. A missing receipt does not prove that a submitted operation cannot still commit. Never generate a replacement operation ID and blindly resubmit. Receipts contain no tokens or credentials.

The shared HTTP adapter is `POST /api/operations/{operation}` with the same payload and bearer authorization. Mutations use `Idempotency-Key`; GUID text in D/N/B/P format is normalized to N. HTTP adapters generate a key if omitted and always echo the effective key in `Operation-Id` for receipt reconciliation. V1 realtime mutations require a stable GUID `operationId`, falling back to a valid GUID `requestId`, normalized to N; neither may be silently replaced with a fresh ID. Existing HTTP sale/fleet mutations and resource socket mutations preserve their original response formats. Existing `/api/session/access` also invokes the shared handler.

## Client rollout

All editable catalog/admin lists (`routes`, `employees`, `jobs`, `maintenance`, `tickets`, `schedules`, `users`, `roles`, `permissions`) return `CatalogPageResult<T>` with page metadata and `CanCreate`/`CanEdit`/`CanDelete` from that query's current access snapshot. The five administrative lists no longer return bare `PageResult<T>`; deploy the API and client together. Capability permissions and mutation classifications are unchanged.

Route commands now persist number (<=50 characters), stop count (0..10,000), length (finite 0..1,000,000 km), description (<=2,000) and type (<=100) atomically with the existing fields and active flag. Omitted update values preserve existing data; empty strings/zero values explicitly clear/reset fields. Route list/get share the same metadata projection and expose active-ticket counts only with `tickets.view` (otherwise null). The in-place v2 reducer signature upgrade requires matching 2.1.0 bindings/API/module deployment; legacy reducer signatures and revision advancement are retained.

Ticket list/get also share one projection: `RouteStartPoint`, `RouteEndPoint` and `RouteTravelTime` accompany the existing `RouteLabel`. All joined route fields are null without `routes.view`; clients never parse the formatted label to recover endpoints.

`shared/BRU.Avtopark.Client` provides one serialized writer and one receiver with correlated pending requests. It shares contracts with the API, has no Avalonia or generated database dependency, and never retries writes automatically. Cancellation sends a cancel command after a complete frame; a partially sent frame closes the socket. Disconnect during a mutation raises `commit_outcome_unknown`.

Avalonia/MAUI share `ApplicationClientCoordinator`. It binds a transport to the selected server and token, disposes it on session changes, and rejects stale results. Access checks, both reporting screens, buses, Jobs, Employees and Maintenance use it. Fleet queries return `BusPageResult` with paging and current create/edit/delete eligibility, preserve row identity/selection on refresh, and cancel superseded loads. Unauthorized dependent counts are null, not fabricated zeroes. Fleet mutations use shared commands and retain the dialog draft on rejection or uncertainty. All existing bus form fields are persisted atomically. `BRU_TRANSPORT=http` selects the shared HTTP adapters for migrated reads and writes; writes are dispatched once and never replayed after an uncertain response. Routes, Sales, Tickets, Schedules and Users/administration remain to migrate. The new report and single-row operations must be deployed with the client; an older API does not have those projections.

Employees and Maintenance use `LiveCatalogViewModel` with a fixed page size of 100 and the same Previous/PageInfo/Next/Refresh/loading controls as Jobs. Search is sent in `PageRequest.Search`. Snapshot/invalidation/watermark/reconnect handling stays in the shared subscription coordinator; stale rows remain visible, but create/edit/delete commands require synchronized snapshots and current page permissions. Refresh preserves row identity/selection. Activation, deactivation and session changes cancel pending queries, lookups, confirmations and editor work. Missing dates/costs remain unavailable rather than becoming 1970/zero. Every applied snapshot dumps the actual bound items through `DataGridDiagnostics` under the configured debug-traffic policy.

`EmployeeEditorService` loads paged job choices; `MaintenanceEditorService` loads paged bus choices, both through shared queries. Russian validation mirrors the existing command limits. Rejected drafts remain editable where correction is safe (including create conflicts). Update/delete commands carry expected revisions. Conflict review reads the current row through `employees.get` / `maintenance.get`; only explicit replacement overwrites the draft and adopts its revision. Each submitted attempt retains its operation ID until its result is known. An uncertain editor attempt is retained across dialog closing/reopening within the same session and blocks resubmission; `mutations.receipt` is the only success reconciliation, and a missing receipt does not authorize replay. Unknown deletes are gated until a fresh snapshot reconciles the current row. Editor/mutation outcomes log operation IDs, codes and revisions. Desktop/embedded dialog wiring and production HTTP adapters are covered headlessly; this does not establish native visual/platform correctness.

The `reports.sales` projection uses `Business:TimeZone` (default `UTC`) for inclusive business dates; `reports.income` remains a UTC calendar-month summary. It retains sales with missing related rows, counts unknown historical amounts, and averages only recorded amounts (including genuine zero amounts). Every requested day and month is represented, including zero-sale periods; ranges remain bounded to 3,661 inclusive days. More than 200 route groups are folded into a clearly labelled remainder, retaining overall counts and revenue. View activation starts loading; deactivation cancels it. Superseded results are ignored and errors preserve previously loaded data with retry controls. Account changes clear the prior report.

## Diagnostics and verification

Server operation logs include connection ID, request ID, generation, outcome and duration. Envelopes carry trace IDs. `BRU.Application` emits command duration/count, connection and pending-work metrics and activities through the configured OpenTelemetry pipeline. Client logs include operation, request ID, transport, generation, outcome and elapsed time. Existing operation-boundary and raw traffic diagnostics can include business payloads/results and bound rows; credential fields, password/second-factor values and bearer tokens are masked under the normal debug policy. See [diagnostics](diagnostics.md) for explicit traffic modes and storage/retention considerations.

`tests/Api.Contracts` exercises framed sockets and an ephemeral Kestrel host with the production shared client. It covers capability negotiation, explicit dispatch, anonymous rejection, budgets, challenge ownership, cancellation, logout, account revocation, token expiry and uncertain mutation disconnect. Authentication/database test doubles isolate these boundaries; the separate database integration suite validates actual receipts and reducer integrity. These tests do not establish real Windows/Entra/Hello interoperability or visual correctness.

## Query subscriptions and resynchronization

`subscriptions.open` accepts `{subscriptionId, operation, payload}`. Use a GUID in N format for the subscription ID. Supported queries: `session.access`, `buses.list`, `sales.list`, `reports.sales`, `reports.income`, `routes.list`, `employees.list`, `jobs.list`, `maintenance.list`, `tickets.list`, `schedules.list`, `users.list`, `users.get`, `roles.list`, `permissions.list`. `users.get` takes its normal `{id}` payload. Mutations, token lookup and authentication operations cannot be subscribed. A socket may hold 16 snapshot leases. Role/user-role/role-permission/permission changes invalidate `access` as well as their affected resource projections; entity-revision changes also invalidate their resource.

The correlated response contains `subscriptionId`, `operation`, `data` and `watermark: {epoch, sequence}`. The server captures the watermark **before** acquiring the snapshot. Committed table callbacks append resource-only invalidations to a bounded journal; changes during snapshot acquisition remain visible after that watermark. There are no credentials, row payloads or uncommitted mutation notifications in this journal.

When a dependency changes, the server pushes a result envelope with `type: "invalidation"`. Its data contains `subscriptionId`, `from`, `watermark` and `reason` (`changed`, `forbidden` or `resync_required`). This ends the snapshot lease. Reopen the same subscription ID to obtain an authoritative replacement snapshot. Changes may be coalesced; sequence ranges are not counts of visible entities. These are query invalidations, not row patches. Do not merge partial history into local data.

The journal holds 2,048 entries. An expired watermark, overflow or database connection-epoch change requires a fresh snapshot. The server rechecks token proof and current operation access on the serialized worker at least once per idle second while subscriptions exist. Failed authorization ends the subscription. Failed reauthorization, outbound queue overflow or a slow writer closes the connection, requiring authentication and fresh snapshots. Logout/attach changes the session generation and clears all leases. `subscriptions.close` explicitly releases a lease.

The shared client's `WatchAsync` buffers an invalidation arriving before the snapshot waiter resumes, detects watermark mismatch, emits an unsynchronized state and acquires a new snapshot. The native coordinator reconnects with bounded backoff/jitter. Account/origin changes cancel the previous stream. The bus screen uses this path, preserves row/selection identity, keeps stale rows visible and disables mutations until synchronized. `BRU_TRANSPORT=http` remains an explicit transport rollback; it uses authoritative reads without live subscriptions.

## Catalog mutations

`routes`, `employees`, `jobs` and `maintenance` expose `.list`, `.create`, `.update`, `.delete`; routes also exposes `.set-active`. Lists are bounded, sorted pages of explicit DTOs. Writes return actor/payload-bound committed receipts. `EntityRevision` is additive; existing rows start at revision zero, and both legacy and v2 writes advance it. Deletes retain revision tombstones. Optional `expectedRevision` rejects stale drafts.

Existing HTTP/resource-socket catalog mutations share the same committed services and preserve their response shapes. Creation returns the exact receipt entity, including concurrent employees with identical names. Legacy direct creation of routes/employees/jobs now checks the sender's permission; it no longer accepts unauthenticated writes. Trusted API delegation remains required for v2 actors. Route/job/employee deletion protects existing references, and maintenance date/reference validation occurs in the transaction.

### Administrative contracts and compatibility

Administrative writes use the existing `CatalogMutationExecutor`, `CommitCatalogMutation`,
`EntityRevision` and actor-scoped `MutationReceipt`, not a parallel receipt mechanism.
Both legacy and v2 writers advance revisions; deletes retain tombstones. Optional
`expectedRevision` rejects stale edits. HTTP supports the model field or `If-Match`;
v1 resource commands support the payload field and generic v1 deletes accept a root
`expectedRevision`. All administrative operations are also available through
`POST /api/operations/{operation}`. Payloads and credentials are never telemetry tags.
Ticket/schedule resource sockets allow mutation-only accounts without opening a view
event feed; commands still reauthorize individually, and mutation-only responses do
not fetch or return an unauthorized collection snapshot.

`users.create` exists because the legacy admin form creates accounts. Its input-only
`initialCredential` goes through `RegisterUserCore` and private credential storage;
the receipt stores only a one-way mutation digest and the committed numeric ID.
User responses never contain credential-shaped members. There is no stored display
name column, so none was invented. Profile-only `users.update` accepts email/phone;
login/credential/role changes in the old update model are explicitly rejected, not
silently applied or echoed. Use the account-security password ceremony and explicit
role operations. Activation-only old model payloads map to `.set-active`; mixed
profile/activation requests and previously ignored `lastLoginAt`/`validUntil` edits
are rejected before persistence. Submit distinct operations with distinct IDs.

Role create/update forms may retain `permissionIds`: their entire metadata/grant set
commits in one reducer transaction, and each grant/revoke enforces `grant_permissions`
and anti-escalation checks. No sequence of independently committed adapter writes is
used. Schedule creation preserves bus types atomically. Seat numbers must be positive,
unoccupied for an active ticket, and within seated/total capacity when the linked bus
has a known capacity; legacy rows with unknown capacity do not invent a seat table.
Generic v1 schedules retain numeric Unix-millisecond inputs and row-shaped create
responses; HTTP/resource sockets retain their date models. Omitted generic arrival
time keeps the existing departure-plus-one-hour default. Route endpoint labels and
empty optional stop metadata retain their legacy defaults. Generic v1 tickets keep
their optional zero price and explicit purchase time, and return the existing
five-field create payload; absent purchase time is assigned once by the reducer in
milliseconds and reconciled from the committed row, not a fresh timestamp on retry.

The secret-member reflection test targets administrative command/row contracts.
Authentication/QR protocol contracts intentionally retain token/secret input fields;
they are not administrative response projections. No native screens changed here.

### Jobs client adoption (existing client)

The jobs screen now uses shared paged queries and v2 snapshot subscriptions, awaited activation and cancellable deactivation. Updates preserve row identity and selection; mutation commands follow current permissions and synchronization state. Related employee counts are calculated server-side and stay unavailable when employee visibility is absent. `jobs.get` is an explicit authorized read for conflict review. The editor retains drafts on rejection, retrieves the current row/revision, and replaces a draft only on explicit action. A desktop or embedded-view dialog host keeps presentation out of the view model. Uncertain writes are not automatically retried; receipt lookup uses the selected read transport, including HTTP rollback mode.
