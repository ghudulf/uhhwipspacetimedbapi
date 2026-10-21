# Native refresh tokens and session synchronization

## Model and storage boundary

Native application sessions use short-lived existing access tokens plus opaque,
single-use refresh proofs. They are separate from OpenIddict OAuth refresh tokens
and browser cookies. The API generates 32 CSPRNG bytes encoded as 43-character
base64url text. Only SHA-256/base64url hashes reach the module. Never log proofs,
access tokens, request bodies or reducer arguments containing credentials.

`RefreshTokenRecord` stores the hash, user, family, issue/absolute expiry times,
used/revoked times and a sanitized device label (maximum 64 characters). The root
hash identifies the family. The table has trusted-API sender-filtered RLS; every
refresh reducer also requires a provisioned trusted API. Public table metadata is
not permission to read its rows. An active application user is required.

## Rotation, reuse and strict concurrency

`IssueRefreshToken` commits a new family. `RotateRefreshToken` atomically marks
the presented proof used and inserts one successor with the same family and
absolute expiry. The API waits for reducer completion and then checks that its
specific successor exists, belongs to the original user/family and is live before
minting an access token. A committed callback alone does not establish success.

Presenting a used, unexpired, unrevoked proof commits revocation of **the whole
family, including the successor**, and returns without inserting a successor.
It must not throw after revocation: a reducer exception would roll it back. The
API maps this committed denial to the same `invalid_refresh_token` result used
for unknown, expired, revoked and inactive-account proofs.

Concurrency is deliberately strict, not an idempotent retry window. N concurrent
rotations of the same proof create exactly one successor; the next serialized
attempt detects reuse and revokes both records. Remaining attempts reject. Even
the first response may already contain a revoked refresh proof by delivery time.
Independent processes/devices must not share a family. There is no grace period
and no replay of a refresh request after timeout, disconnect or lost response.
The client must sign in again if the outcome is uncertain.

## Lifetimes, bounds and revocation

Access-token lifetime continues to follow existing token configuration; refresh
does not extend a family's lifetime. `Authentication:RefreshTokens:LifetimeSeconds`
defaults to 30 days and is clamped to one hour–90 days by both API and module.
There are at most ten live families per user; issuance revokes the oldest excess
families. A hard 4,096-row per-user retention bound prevents unbounded rotations.
Issuance/rotation opportunistically prune at most 128 expired/revoked records;
used live records are retained as reuse evidence. This is not scheduled cleanup.

Native logout submits the stored proof to revoke its family, then clears local
credentials even if the network is unavailable. Offline logout cannot attest
server revocation. Legacy logout without a proof does not revoke families; trusted
administrative callers can use `RevokeAllRefreshTokens` explicitly.
Credential replacement/password change or reset through module writers, identity migration, account
deactivation and deletion invoke module-level revocation hooks transactionally.
Refresh revocation does not retroactively blacklist existing access tokens;
protected operations continue to validate token lifetime and active account/access.
The module's own `ChangePassword` and administrator reset writers enforce the
hook. The separate legacy API `ChangePasswordAsync` orchestration remains an
unimplemented ceremony; this rollout does not claim to complete that endpoint.

## Explicit native opt-in, not a browser credential

Issuance and refresh require `X-BRU-Native-Login: 1`, **absence of the Origin
header**, and transport permitted by the shared [network policy](network-policy.md)
(default `lan` allows LAN HTTP; `never` requires HTTPS even on loopback).
This header is an opt-in, not proof of client identity. An Origin-bearing request
never receives a native refresh proof, even with the opt-in header. Password,
Windows, QR, Entra and passkey results share finalization; a required second factor
gets no refresh proof until that factor succeeds. `X-BRU-Device` is descriptive
only, not an authorization input. Do not add native opt-in headers to browsers.

HTTP uses `POST /api/auth/refresh {refreshToken}`; v2 uses
`auth.refresh {refreshToken}`. Neither relies on an expired access token as the
refresh credential. V2 replaces credentials, advances session generation and
invalidates open leases with `session_changed`. Native clients currently rotate
through HTTP and rebind their selected application transport.

Process-local sliding one-minute budgets allow 20 issuances/source address,
30 refresh attempts/source address and ten refreshes/family; native family logout
has a 30/source-address budget. Budget storage is capped at 20,000 keys. V2 also
applies its existing shared authentication/connection budgets. Limits are not
distributed across API replicas; configure trusted proxy forwarding correctly and
add deployment-level abuse controls where required. Denials fail closed.

## Native storage and recovery

Stored sessions are bound to the selected server origin and distinguish native
from OAuth sessions. Windows uses CurrentUser DPAPI. Unsupported platforms keep
tokens in process memory until a verified host keystore is registered; generic
MAUI compilation is not evidence of durable mobile storage. Legacy adjacent-key
token files are removed and require reauthentication. Never persist plaintext
refresh proofs or use an unverified weaker storage fallback.

`ISessionRefresher` dispatches exactly once. It conditionally replaces the stored
token pair before applying the new access token, rejecting a response for a
different account/server or superseded access token. Network, timeout, malformed
or rejected responses clear only the matching stored session. No credentials or
exception payloads are written to diagnostics.

The shared `SessionRefreshCoordinator` single-flights concurrent read and
subscription expiries, caches the replacement for late arrivals, and applies a
30-second failure cooldown. One caller's cancellation does not cancel the shared
rotation. Reads retry once with the replacement; subscriptions emit an
unsynchronized `session_changed` update and reacquire a fresh snapshot. A failed
session raises `SessionExpired` once for the expired session and returns the UI
to login. **Mutations are never automatically refreshed or retried**, including
unknown commit outcomes; reconcile their operation receipt explicitly.

## Deployment and compatibility

1. Back up and rehearse an additive module deployment; never clear application
   data. Publish `RefreshTokenRecord` and the four refresh reducers **before the
   matching API**, with pinned-2.1.0 bindings and completion observers. Preserve
   trusted API provisioning/RLS. Review any server-required client-break plan.
2. Deploy the API with HTTPS, correct proxy policy and the desired absolute family
   lifetime. Do not conflate the native protocol with OpenIddict refresh grants.
3. Deploy native clients with single-flight coordination and safe storage. Older
   clients and browsers without native opt-in continue to receive access tokens
   only and must sign in after expiry; existing sessions are not retrofitted with
   refresh proofs. Clients may ignore the optional response field.
4. Roll back native opt-in/client recovery if needed; do not delete token-family
   history as a rollback mechanism. Existing access sessions remain governed by
   their normal expiry. Multi-replica rate limits and real Windows/mobile keystore,
   browser and UI behavior still require deployment/platform validation.

The contract suites cover production orchestration, native-only/second-factor
issuance, token-free logging, logout and budgets; real-module integration covers
strict replay/concurrency, expiry/caps, lifecycle hooks and untrusted/RLS denial.
Shared and headless presentation tests cover single-flight persistence, stale
response rejection, SessionExpired coalescing and no mutation replay.
