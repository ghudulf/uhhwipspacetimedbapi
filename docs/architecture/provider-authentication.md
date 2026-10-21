# System-browser provider authentication — implementation checkpoint

**Delivered:** bounded provider broker; Microsoft Entra ID code+PKCE server,
shared client and Avalonia login/link/status/unlink UI; own-account unlink reducer;
v2 refresh transport adapter. **Not delivered:** browser passkey ceremonies,
software-authenticator tests, passkey management UI, session-refresh coordinator
and token-storage integration. The capability endpoint always reports
`passkeys:false`; existing WebAuthn is not advertised as Windows Hello.

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

## Refresh checkpoint and remaining passkey work

V2 `auth.refresh {refreshToken}` consumes the shared connection authentication
budget, calls `IAuthOrchestrationService.RefreshTokenAsync`, validates the new
access token and active actor, replaces socket credentials, increments session
generation and invalidates every open lease with `session_changed`. Its reply
can carry the rotated refresh token and expiry. It can run after access expiry;
the refresh token, not a stale access token, is its credential. Tests use a fake
orchestrator and real framed sockets.

**Production refresh is not functional:** the existing orchestration method is
still a `Not implemented` rejection. No refresh-token issuance/security model
was invented here. The planned `ISessionRefresher`, single-flight coordinator,
stored-token implementation and UI `SessionExpired` recovery remain unfinished;
mutations are never automatically retried by current code.

Passkey implementation must first repair/extend existing WebAuthn storage: add
a random per-user handle persisted with credentials, use committed credential
registration/removal and monotonic counter compare/update, and preserve the
actual decoded challenge rather than UTF-8 bytes of its Base64 representation.
The current service also logs usernames and uses preferred rather than required
user verification. Reusing that path unchanged would violate this handoff's
security requirements. No new passkey endpoints, Fido2 startup origin/RP validator,
software-authenticator harness or credential-management UI are delivered here.

The intended browser flow must split browser and redemption secrets, consume
browser tokens by phase, enforce RP/origin/challenge/signature/UV/counter/active
account checks, burn after three failures, and use a nonce-protected script CSP
with history scrubbing. Discoverable and unknown hinted options must have the
same non-enumerating shape. Windows Hello is reached through a system browser's
WebAuthn platform authenticator, not native Hello APIs. Required UV counts only
as the primary factor; local TOTP/WebAuthn second-factor policy must still apply.

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

Deviations: passkeys and client refresh integration are explicitly deferred for
the security/storage dependencies above; Entra metadata/JWKS are conservatively
same-authority-host pinned; own unlink removes all own mappings for the selected
provider; custom desktop authority allow-list configuration remains unfinished.
No new token model or weak fallback was introduced.

Publish the additive module reducer/bindings **before** this API. Enable Entra
only after tenant/callback validation; deploy capable clients after the API.
Rollback by setting `Authentication:Entra:Enabled=false` and restarting the API;
existing local credentials/Windows/QR links and database rows are unchanged.
In-flight broker flows are lost on restart. Leave passkeys unadvertised.

Not verified: a real Entra tenant/app/consent policy, real system browsers,
Windows Hello/platform authenticators, OS-level browser launching, desktop visual
layout and native MAUI mobile flows. Synthetic success is not platform sign-off.
