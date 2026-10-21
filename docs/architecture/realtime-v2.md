# Realtime v2 rollout and debugging

`GET /api/realtime/stream` accepts `bru.api.v2`. The existing `bru.events.v1` endpoint remains available. V2 requires HTTPS; a loopback peer in Development can use HTTP. Browser origins must exactly match `Realtime:AllowedOrigins`; native clients send no Origin. Configure forwarded headers for trusted proxies before relying on remote-address limits.

This is a partial operation rollout. Capability negotiation is authoritative: operations absent from it are not implemented in v2. All-operation parity, receipts for the remaining resources, provider ceremony binding and broad screen migration remain outstanding. Snapshot subscriptions are available for the explicitly listed queries below.

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

Session attach revalidates token proof; every protected command revalidates its token and current database permissions. Query subscriptions reauthorize on delivery and expire their snapshot lease when committed data changes. Password/TOTP orchestration shares the existing authentication service. Windows/Entra/WebAuthn browser and platform ceremonies must not be replaced with client-supplied usernames or biometric data.

Sale, fleet, route, employee, job and maintenance mutations require a stable `operationId` in the envelope. The ID is scoped to actor and payload by the database. After disconnect or `commit_outcome_unknown`, query `mutations.receipt`. A missing receipt does not prove that a submitted operation cannot still commit. Never generate a replacement operation ID and blindly resubmit. Receipts contain no tokens or credentials.

The shared HTTP adapter is `POST /api/operations/{operation}` with the same payload and bearer authorization. Mutations use `Idempotency-Key`; GUID text in D/N/B/P format is normalized to N. HTTP adapters generate a key if omitted and always echo the effective key in `Operation-Id` for receipt reconciliation. V1 realtime mutations require a stable GUID `operationId`, falling back to a valid GUID `requestId`, normalized to N; neither may be silently replaced with a fresh ID. Existing HTTP sale/fleet mutations and resource socket mutations preserve their original response formats. Existing `/api/session/access` also invokes the shared handler.

## Client rollout

`shared/BRU.Avtopark.Client` provides one serialized writer and one receiver with correlated pending requests. It shares contracts with the API, has no Avalonia or generated database dependency, and never retries writes automatically. Cancellation sends a cancel command after a complete frame; a partially sent frame closes the socket. Disconnect during a mutation raises `commit_outcome_unknown`.

Avalonia/MAUI share `ApplicationClientCoordinator`. It binds a transport to the selected server and token, disposes it on session changes, and rejects stale results. Access checks, both reporting screens and the bus screen use it. Fleet queries return `BusPageResult` with paging and current create/edit/delete eligibility, preserve row identity/selection on refresh, and cancel superseded loads. Unauthorized dependent counts are null, not fabricated zeroes. Fleet mutations use shared commands and retain the dialog draft on rejection or uncertainty. All existing bus form fields are persisted atomically. `BRU_TRANSPORT=http` selects the shared HTTP adapters for migrated reads and fleet writes; writes are dispatched once and never replayed after an uncertain response. The remaining screens still need migration. The new report operation must be deployed with the client; an older API does not have the server projection.

The `reports.sales` projection uses `Business:TimeZone` (default `UTC`) for inclusive business dates; `reports.income` remains a UTC calendar-month summary. It retains sales with missing related rows, counts unknown historical amounts, and averages only recorded amounts (including genuine zero amounts). Every requested day and month is represented, including zero-sale periods; ranges remain bounded to 3,661 inclusive days. More than 200 route groups are folded into a clearly labelled remainder, retaining overall counts and revenue. View activation starts loading; deactivation cancels it. Superseded results are ignored and errors preserve previously loaded data with retry controls. Account changes clear the prior report.

## Diagnostics and verification

Server operation logs include connection ID, request ID, generation, outcome and duration. Envelopes carry trace IDs. `BRU.Application` emits command duration/count, connection and pending-work metrics and activities through the configured OpenTelemetry pipeline. Client logs include operation, request ID, transport, generation, outcome and elapsed time. Payloads, password/second-factor values and bearer tokens are excluded from these diagnostics.

`tests/Api.Contracts` exercises framed sockets and an ephemeral Kestrel host with the production shared client. It covers capability negotiation, explicit dispatch, anonymous rejection, budgets, challenge ownership, cancellation, logout, account revocation, token expiry and uncertain mutation disconnect. Authentication/database test doubles isolate these boundaries; the separate database integration suite validates actual receipts and reducer integrity. These tests do not establish real Windows/Entra/Hello interoperability or visual correctness.

## Query subscriptions and resynchronization

`subscriptions.open` accepts `{subscriptionId, operation, payload}`. Use a GUID in N format for the subscription ID. Supported queries: `session.access`, `buses.list`, `sales.list`, `reports.sales`, `reports.income`, `routes.list`, `employees.list`, `jobs.list`, `maintenance.list`. Mutations, token lookup and authentication operations cannot be subscribed. A socket may hold 16 snapshot leases.

The correlated response contains `subscriptionId`, `operation`, `data` and `watermark: {epoch, sequence}`. The server captures the watermark **before** acquiring the snapshot. Committed table callbacks append resource-only invalidations to a bounded journal; changes during snapshot acquisition remain visible after that watermark. There are no credentials, row payloads or uncommitted mutation notifications in this journal.

When a dependency changes, the server pushes a result envelope with `type: "invalidation"`. Its data contains `subscriptionId`, `from`, `watermark` and `reason` (`changed`, `forbidden` or `resync_required`). This ends the snapshot lease. Reopen the same subscription ID to obtain an authoritative replacement snapshot. Changes may be coalesced; sequence ranges are not counts of visible entities. These are query invalidations, not row patches. Do not merge partial history into local data.

The journal holds 2,048 entries. An expired watermark, overflow or database connection-epoch change requires a fresh snapshot. The server rechecks token proof and current operation access on the serialized worker at least once per idle second while subscriptions exist. Failed authorization ends the subscription. Failed reauthorization, outbound queue overflow or a slow writer closes the connection, requiring authentication and fresh snapshots. Logout/attach changes the session generation and clears all leases. `subscriptions.close` explicitly releases a lease.

The shared client's `WatchAsync` buffers an invalidation arriving before the snapshot waiter resumes, detects watermark mismatch, emits an unsynchronized state and acquires a new snapshot. The native coordinator reconnects with bounded backoff/jitter. Account/origin changes cancel the previous stream. The bus screen uses this path, preserves row/selection identity, keeps stale rows visible and disables mutations until synchronized. `BRU_TRANSPORT=http` remains an explicit transport rollback; it uses authoritative reads without live subscriptions.

## Catalog mutations

`routes`, `employees`, `jobs` and `maintenance` expose `.list`, `.create`, `.update`, `.delete`; routes also exposes `.set-active`. Lists are bounded, sorted pages of explicit DTOs. Writes return actor/payload-bound committed receipts. `EntityRevision` is additive; existing rows start at revision zero, and both legacy and v2 writes advance it. Deletes retain revision tombstones. Optional `expectedRevision` rejects stale drafts.

Existing HTTP/resource-socket catalog mutations share the same committed services and preserve their response shapes. Creation returns the exact receipt entity, including concurrent employees with identical names. Legacy direct creation of routes/employees/jobs now checks the sender's permission; it no longer accepts unauthenticated writes. Trusted API delegation remains required for v2 actors. Route/job/employee deletion protects existing references, and maintenance date/reference validation occurs in the transaction.

### Jobs client adoption

The jobs screen now uses shared paged queries and v2 snapshot subscriptions, awaited activation and cancellable deactivation. Updates preserve row identity and selection; mutation commands follow current permissions and synchronization state. Related employee counts are calculated server-side and stay unavailable when employee visibility is absent. `jobs.get` is an explicit authorized read for conflict review. The editor retains drafts on rejection, retrieves the current row/revision, and replaces a draft only on explicit action. A desktop or embedded-view dialog host keeps presentation out of the view model. Uncertain writes are not automatically retried; receipt lookup uses the selected read transport, including HTTP rollback mode.
