# System-browser provider authentication — implementation checkpoint

**Delivered:** bounded provider broker; Microsoft Entra ID code+PKCE server,
shared client and Avalonia login/link/status/unlink UI; system-browser passkey
registration/login, real software-authenticator tests, committed WebAuthn storage
and own-key management UI; own-account unlink reducer; native refresh issuance,
rotation, shared session-refresh coordination and native token-storage integration
([session model and rollout](session-synchronization.md)). Windows Hello is accessed only through
the system browser's WebAuthn/platform authenticator, not native Hello APIs.

## Entra protocol and security boundary

`POST /api/auth/entra/start` accepts only `{purpose:"login"|"link"}`. Link requires
a fully validated bearer and captures the active application actor server-side.
No body can select a target account. The response includes a flow ID, a secret
returned once, expiry, polling interval and the Microsoft authorization URL.

The five-minute process-local broker stores SHA-256 hashes of flow secrets and
OIDC state. State is consumed atomically before the code exchange; nonce and
PKCE verifier are retained only until callback consumption or termination. The
callback exchanges the code with S256 PKCE and validates real discovery/JWKS
signatures using RS256 only, exact client audience, tenant-scoped Microsoft
issuer, explicitly allowed tenant GUID, nonce and token lifetime (two-minute
maximum skew). Both `oid` and `tid` are required GUIDs. Identity is the canonical
`oid` plus tenant issuer, never email, username or the token's `sub`.

This addresses login CSRF/state replay, code interception, mix-up/tenant confusion
and token substitution. Discovery, JWKS and token I/O use the named `entra`
client: no redirects/cookies, HTTPS only, ten-second bounds, 256 KiB metadata
and 64 KiB token-response limits. Discovery endpoints and JWKS are pinned to
the configured authority host/port. Unknown signing keys request a manager
refresh and one bounded foreground metadata/JWKS retrieval. Unknown keys still
fail closed. No IdP errors are reflected to the browser.

The callback serves a static Russian completion page without scripts, with
`default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'none'`,
`Referrer-Policy: no-referrer`, `nosniff` and no-store. Unknown, replayed, expired
and rejected callbacks share one generic failure page. The browser never receives
an application token or flow redemption secret.

`POST /api/auth/entra/flows/{flowId}/complete` accepts `{flowSecret}`. Wrong,
missing and unknown secrets return the same 404 `not_found`. Redemption is
atomic, exactly once and burns the flow **before** token issuance or linking.
Unlinked login returns only `not_linked`; linked login uses the existing active
account and second-factor policy, including `RequiresTwoFactor`. Linking
revalidates the captured actor and awaits the committed reducer. Rejected
mapping creation returns `already_linked`; uncertain outcomes are never retried
as a new mutation. `.../cancel` accepts the same secret and terminates live flows.

`POST /api/auth/entra/link/status` and `POST /api/auth/entra/unlink` require the
current full bearer and expose/change only that actor's Entra links. Unlink
does **not** enforce another usable credential: users can lock themselves out;
an administrator must then reset/provision an alternative. It removes all of
the actor's mappings for that provider, not another user's mapping or Windows
links. The trusted API reducer rejects inactive actors and absent links.

Broker limits: 2,000 live flows, five live per source IP, ten creations/minute/IP,
90 polls/minute/flow, and bounded tombstone/rate dictionaries. Mutations sweep
expiry and old entries. TTL clamps to 60–600 seconds. Flows do not survive API
restart; deploy behind affinity or restart the ceremony after an instance change.
No URLs containing queries, state, nonce, codes, subjects, emails or tokens are
logged by provider code. Named-client logging is removed; framework request-start
logging is raised to Warning because it includes callback queries. Apply the same
redaction to ingress/proxy/access logs and production telemetry.

## Configuration and Entra registration

All configuration uses existing configuration/secret-file sources. Never commit
the optional client secret. For example, mount an owner-only
`Authentication__Entra__ClientSecret` file through `BRU_SECRETS_DIR`.

| Key | Default / requirement |
|---|---|
| `Authentication:Entra:Enabled` | `false`; disabled routes return 404, including invalid JSON |
| `Authentication:Entra:TenantId` | Required tenant GUID |
| `Authentication:Entra:ClientId` | Required application/client GUID |
| `Authentication:Entra:ClientSecret` | Optional; mounted secret/environment, never diagnostics |
| `Authentication:Entra:RedirectUri` | API public origin/path-base + `/api/auth/entra/callback` |
| `Authentication:Entra:Authority` | `https://login.microsoftonline.com/{tenant}/v2.0` |
| `Authentication:Entra:AllowMultiTenant` | `false` |
| `Authentication:Entra:AllowedTenants` | Explicit GUID array required for multi-tenant aliases |
| `Authentication:Providers:FlowSeconds` | 300; clamped 60–600 |

`common`, `organizations` and `consumers` are rejected unless multi-tenant mode
is explicitly enabled with a nonempty explicit tenant list. Authority must be
HTTPS with no user-info/query/fragment. Redirect must have no user-info/query/
fragment and be HTTPS, except HTTP loopback in Development. Invalid enabled
configuration fails startup. Scopes are fixed to `openid profile email`.

1. Register an application in the intended Entra tenant; select only the intended
   account/tenant scope. Copy its tenant and application GUIDs.
2. Add a **Web** redirect URI exactly matching the externally reachable callback,
   including any reverse-proxy path base. Do not use a desktop loopback callback.
3. Configure authority, client ID and optional confidential-client secret. PKCE
   is always used, including when a secret is configured.
4. Set trusted forwarded-header proxies and `AllowedHosts` for the deployment;
   verify the resulting public HTTPS callback before rollout.
5. Sign in locally, complete configured 2FA, then choose **Связать учётную запись
   Microsoft**. Check status, explicitly link through the browser and verify it
   before using Microsoft login. Owner-managed provisioning remains available.

## Clients and capabilities

`GET /api/auth/providers` returns only `entra`, `passkeys`, `windows`, `qr` flags,
no-store and without browser CORS. QR reflects its generation/status flags.
Unknown/unavailable capabilities keep the new provider selectors hidden.
Windows also requires an explicitly trusted native origin as before.

Shared `ProviderFlow` launches only HTTPS Entra URLs on explicitly allowed
authority hosts (`login.microsoftonline.com` by default), with no user-info or
fragment. Future passkey browser URLs must match the API origin, deployment
prefix, exact browser path, flow ID and exactly the `f`/`b` query keys. Unsafe
URLs launch nothing and send no redemption/cancellation secrets. HTTP clients
must use `AllowAutoRedirect=false`; they enforce response caps and safe errors.
Polling clamps to 500–5,000 ms, bounds transient retries to three consecutive
failures, honors local expiry, cancels at most once and surfaces completion once.
An interrupted redemption can be consumed: no automatic mutation retry is safe.

Avalonia uses `Process.Start(... UseShellExecute=true)` after URL validation,
never an embedded WebView for this provider. Browser-launch failures are shown
without URLs. Login results use `AuthViewModel.ApplyExternalLoginAsync`, so
the normal second-factor wizard still applies. Login/link windows cancel and
close on session changes. Link/unlink controls require an explicit status check
after uncertain results. Every added interactive control has an accessibility
name; status/countdown have polite announcements.

MAUI hosts can replace `ProviderClientContext.Browser` with a verified platform
`IBrowserLauncher`. No native browser/authenticator integration is claimed from
the generic build. The desktop launcher rejects HTTP unless explicitly created
with development-loopback permission. The shipped desktop view model uses the
default Microsoft authority host; custom host allow-list plumbing is not yet
exposed in desktop configuration (the shared flow accepts an injected list).

## Passkey / browser Windows Hello protocol

Passkeys are default-off. Enabling them requires these **explicit** configuration
values; the historical localhost Fido2 defaults do not satisfy the startup check:

| Key | Requirement |
|---|---|
| `Authentication:Passkeys:Enabled` | `false` by default |
| `Authentication:Passkeys:PublicOrigin` | Exact externally reachable API origin, e.g. `https://api.example.org` |
| `Fido2:ServerDomain` | RP id; conservatively required to equal the public-origin hostname |
| `Fido2:Origins` | Nonempty origin array containing the exact public API scheme/host/port |

Origins must have no user-info, query, fragment or application path. All configured
origins must be HTTPS; HTTP loopback is permitted **only in Development**. Missing
or inconsistent enabled configuration fails startup with a clear, non-secret
message. Configure trusted proxy forwarding and the request path base for a
deployment prefix. The browser URL uses the configured public origin plus that
path base; WebAuthn origin/RP validation never trusts a caller-supplied username
or a browser's claimed origin. `GET /api/auth/providers` reports `passkeys:true`
only after enabled options have passed the registered startup validator.

`POST /api/auth/passkey/start` accepts `{purpose:"login"|"register", username?}`.
Registration requires a validated full bearer and binds its active actor
server-side; a registration body cannot name an account. Login may be
discoverable/usernameless, or hinted by username. Unknown/inactive/no-credential
hints still return assertion options with the same fields and an empty allow
list; a hinted assertion must ultimately belong to that active hinted account,
never silently fall back to another discoverable account.

The start response contains `flowId`, a one-time-returned `flowSecret`, expiry,
poll interval and `browserUrl`. The latter contains `f` and a **separate** random
32-byte `b` browser token. Only hashes of both secrets are stored. Possession of
the browser token can drive a ceremony but can never redeem an application login.
The browser token is consumed separately for the landing page, options and
assertion phases; phase replays are rejected. Three bad token/phase attempts
burn the flow. A cryptographically invalid assertion burns it immediately rather
than offering repeated verification attempts with a consumed phase token.

`GET /api/auth/passkey/browser?f=...&b=...` is self-contained Russian HTML. It
immediately removes the query from browser history before network/authenticator
work, uses one nonce-authorized inline script and no inline event handlers, has
no external resources, and serves no-store, no-referrer and nosniff headers. CSP:
`default-src 'none'; connect-src 'self'; script-src 'nonce-…'; style-src
'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'`.
The page calls same-origin `flows/{id}/options` and `flows/{id}/assert` with the
browser token, converts Base64URL credential bytes and uses
`navigator.credentials.create/get`. It displays accessible success, unsupported
browser, cancellation/timeout and generic failure text, never token values.
This constrains XSS, framing, cross-origin ceremony driving and URL disclosure;
ingress/access logs must also redact browser queries as described above.

Options and assertion endpoints accept at most 32 KiB of strict JSON; all browser
and redemption routes are default-off, disable CORS and return no-store. Options
save the exact original Fido2 options/challenge inside the bounded flow. Fido2
3.0.1 verifies the actual RP hash, origin, challenge, ES256 signature and **required
user verification**. Registration uses `none` attestation, an opaque stable user
handle and existing-credential exclusions. The pinned library exposes only the
old `requireResidentKey` boolean, so the browser-options adapter adds
`residentKey:"preferred"` without changing the verifier's stored challenge/options.

Credential mappings must belong to an active local user; supplied user handles
must match that credential's persisted handle. Registration and counter writes
await committed reducers, then the broker authenticates the flow. Counter checks
also run atomically in the module: both old/new zero are allowed for synced
passkeys; every other transition requires `new > old`. Clone/replay rejection
fails authentication and logs only credential fingerprints, not key material or
exception payloads. Removed/inactive credentials and accounts fail closed.

`POST flows/{id}/complete {flowSecret}` and `.../cancel` reuse the generic
exactly-once secret-bound protocol. Login rechecks the active account/credential
and invokes the same **primary-factor** semantics as QR device-key login
(`CompleteQrLoginAsync(actor, false)`). UV does not bypass local TOTP/WebAuthn 2FA.
Registration returns `registered:true` only after credential storage committed.
An uncertain/late/cancelled registration may already have committed a key; list
credentials to reconcile rather than retrying a mutation automatically.

### Storage, migration and credential management

`WebAuthnUserHandle` is an additive protected table, keyed by user identity, with
32 opaque bytes, creation time and the existing trusted-API partition/RLS filter.
`EnsureWebAuthnUserHandle(actingUser)` is trusted-API-only, requires an active
actor and creates the handle once, idempotently even under concurrency. It uses
the pinned module runtime's transaction-seeded `System.Random`: **pseudo-random,
not an OS CSPRNG**. Handles are public opaque identifiers, not authentication
secrets; they are never derived from usernames, identities or JWT/key material.

`RegisterWebAuthnCredential` rejects duplicate credential IDs, including revoked
IDs. Both counter reducers enforce the same atomic monotonic rule.
`RemoveOwnWebAuthnCredential(id, actingUser)` validates active ownership and
deactivates only the actor's key; removal of the last key disables WebAuthn in
the actor's settings in the same transaction. The legacy
`DeactivateWebAuthnCredential` now takes an optional acting user and delegates
to that ownership check; omitting it means the sender, **not arbitrary ownership**.
Its reducer schema changed, so publish with coordinated regenerated bindings.

The repaired shared `WebAuthnService` uses committed operations for every write,
including challenge storage/consumption, registration, counters and removal. It
decodes stored Base64/Base64URL challenge bytes correctly and requires UV in old
endpoints as well. Challenges are consumed before verification. Existing
nonresident keys without UV support must be re-enrolled/replaced; historical
identity-derived user handles are not adopted for new registrations. Existing
credential rows/keys are not rewritten or cleared by this migration.
Historical credentials returning identity-derived handles must be re-enrolled
after a full alternative login; no identity-handle fallback is introduced.
Ambiguous historical active credential-ID mappings fail closed rather than
choosing an arbitrary account. Reconcile such rows administratively.

The UI reuses `GET /api/auth/webauthn/credentials` and
`DELETE /api/auth/webauthn/credentials/{id}` with the full bearer; lists contain
only the caller's own active key IDs/creation times. The transport uses URL-safe
Base64 IDs for deletion to avoid encoded-slash route ambiguity; standard Base64
is still accepted by the service. The repaired reducers independently enforce
ownership. These endpoints retain their existing runtime feature flags, so enable
`WebAuthnCredentialsEndpointEnabled` and `WebAuthnCredentialDeleteEndpointEnabled`
when deploying management UI. They now disable browser CORS and are no-store.

Avalonia's capability-gated **Ключи доступа** window lists/removes own keys and
registers a new key through `ProviderLoginWindow` and the system browser. Unknown
registration/removal outcomes disable further mutations until an explicit list
recheck. Session changes cancel/close the windows and clear displayed key rows.
Login selection remains hidden until the capability is enabled. Accessibility
names and status announcements cover the added controls. No embedded credential
WebView and no unverified native MAUI browser implementation were added.

## Refresh checkpoint

V2 `auth.refresh {refreshToken}` consumes the shared connection authentication
budget, calls `IAuthOrchestrationService.RefreshTokenAsync`, validates the new
access token and active actor, replaces socket credentials, increments session
generation and invalidates every open lease with `session_changed`. Its reply
can carry the rotated refresh token and expiry. It can run after access expiry;
the refresh token, not a stale access token, is its credential. Framed-socket tests
use fake orchestration; production orchestration and real-module family reducers
are covered separately.

Production refresh now uses native-only opt-in (no Origin header), hash-only
single-use families and committed reuse revocation. Concurrent duplicate rotation
is intentionally strict: one successor is created, then the entire family is
revoked. The shared coordinator single-flights reads/subscriptions; native storage
conditionally applies the rotated pair and coalesces `SessionExpired`. Mutations
are never automatically retried. See [session synchronization](session-synchronization.md)
for lifetimes, storage/platform limits, revocation, failure modes and deployment.

## Validation, deviations and rollout

Real fake-IdP RSA/JWKS/JWT tests cover valid login/link, key rotation, unknown and
replayed/expired state, altered signature, wrong audience/issuer/tenant/nonce,
expired/future token, missing oid/tid, unsigned tokens, code/IdP errors,
redirect/oversized responses and strict configuration. Broker tests include
50 parallel redemptions and bounds. HTTP tests exercise JSON/body/CORS/headers,
disabled malformed requests and a capture sink. Shared-flow and Avalonia
headless tests cover URL safety, completion/2FA, cancellation, actual session
events, uncertain unlink and programmatic accessibility checks. Real database
tests cover Entra linking, own-only committed unlink and untrusted rejection.
Passkey tests build real ES256 keys, CBOR `none` attestations and DER signatures
over authenticatorData plus SHA256(clientData), verified by the pinned real Fido2
library. They reject wrong challenge/origin/RP hash, absent UV, forged signatures
and counter replay; permit synced zero counters; exercise register/login, legacy
challenge reconstruction and committed removal. Credential ownership, unknown
credentials/inactive users and hinted-account checks are tested at the actual
service/controller wrapper boundaries with fake database reads; the real module
integration separately verifies ownership, per-user lists, handle idempotency,
zero/monotonic counters, duplicate rejection, RLS and untrusted calls. HTTP tests
assert CSP nonce/header/history behavior, strict requests and Debug-level log
capture. Headless tests cover own-key UI, capability gating, cancellation,
uncertain-outcome blocking and programmatic interactive accessibility names.

Deviations: the module handle generator is explicitly pseudo-random;
`residentKey:"preferred"` is added at the browser-options adapter for Fido2 3.0.1;
invalid cryptographic assertions burn immediately; RP id is conservatively pinned
to the exact API host rather than permitting parent domains. Entra metadata/JWKS remain same-authority-host
pinned; own unlink removes all own mappings for the selected provider; custom
desktop authority allow-list configuration remains unfinished. Native refresh is
separate from provider/browser credentials; no weak storage fallback was introduced.

Publish the additive module reducer/bindings **before** this API. Enable Entra
only after tenant/callback validation; deploy capable clients after the API.
Rollback by setting `Authentication:Entra:Enabled=false` and restarting the API;
existing local credentials/Windows/QR links and database rows are unchanged.
In-flight broker flows are lost on restart. Enable passkeys only after publishing
the handle/removal/counter module and matching bindings and validating public RP/
origin settings. Roll back with `Authentication:Passkeys:Enabled=false`; the
browser routes return 404 and selectors hide, while stored credentials remain.

Not verified: a real Entra tenant/app/consent policy, real system browsers,
Windows Hello/platform authenticators, OS-level browser launching, desktop visual
layout and native MAUI mobile flows. Synthetic success is not platform sign-off.
