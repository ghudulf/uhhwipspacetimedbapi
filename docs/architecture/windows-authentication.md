# Windows account authentication

This provider is disabled by default. The existing JWT, cookie and OpenIddict schemes remain in place. It adds `POST /api/auth/windows` using the explicit ASP.NET Core Negotiate scheme. The endpoint requires HTTPS and the native-client header, disables browser CORS, and consumes the authenticated platform principal, never a username supplied in the request body.

This trusted-HTTPS-origin requirement is a protocol/platform constraint, not
relaxed by the shared [network policy](network-policy.md), even under `lan` or `any`.

## Server provisioning

Set `Authentication:Windows:Enabled=true` and `Authentication:Windows:Realm` to a stable, explicit trusted-domain identifier. For administrator bootstrap, provision an external account link using the deployment owner's identity with `LinkExternalAccount("windows", realm, subject, existingUserIdentity)`. On Windows use the SID returned by the validated Windows identity. Linux uses the validated Kerberos principal when no SID is available. Do not assume these subject formats are interchangeable. Linking never creates an application user or changes their roles. Reassignment requires explicit `UnlinkExternalAccount` first. Links are restricted to trusted API subscriptions and the module owner.

IIS must enable Windows Authentication with the required application configuration; Kestrel requires Negotiate support and domain/service-account configuration. Use the correct SPNs, Kerberos keytab on Linux, service identity and reverse-proxy connection affinity according to the hosting platform. TLS termination must use configured trusted forwarded-header proxies. Avoid multiplexing NTLM connections across users. Preserve deployment-specific Kerberos/NTLM policy; do not enable weaker mechanisms to work around failed validation.

The provider reuses local active-account and second-factor checks. Temporary second-factor challenge creation and consumption must commit before the response/session is issued. Replay, expiry, account deactivation and uncertain commit outcome fail authentication. Token/credential values are omitted from provider and second-factor diagnostics.

## Client configuration

Explicitly set `BRU_TRUSTED_API_ORIGIN=https://your-api.example.org/` for the native desktop process. It must be an origin with no credentials, query, fragment or application path. This selects the configured server before LAN discovery and reveals the Windows/Kerberos option in the existing login selector. Requests carrying default credentials are restricted to that origin, with redirects and proxies disabled. An automatically discovered server is never trusted for integrated credentials.

The existing login wizard handles a successful local JWT or the required TOTP step. Microsoft/Entra system-browser callbacks, securely bound linking and browser passkey ceremonies have a separate [provider implementation checkpoint](provider-authentication.md). Windows Hello is available only through a compatible system browser's WebAuthn platform authenticator; native Hello APIs and real Windows/platform validation are not claimed. Automatic v2 session refresh remains unfinished.

## Required platform validation

Test IIS and Kestrel with domain Windows 10/11 clients, Linux Kerberos where enabled, unavailable domains/VPN, expired credentials, unlinked/disabled accounts, second-factor replay, cancellation and origin mismatch. This Linux sandbox does not provide a domain controller or Windows authentication session. Compilation and synthetic reducer integration do not establish platform authentication success.


## Self-service linking in Avalonia

Sign in to the existing local application account first, including its configured second factor. Open **Связать учётную запись Windows** from the navigation menu and explicitly choose **Связать**. The client sends the active application-session token in the HTTPS request body while Negotiate verifies the Windows identity. The server independently validates both identities and commits `LinkOwnExternalAccount` as the trusted API. It checks the target local account is active and rejects any existing external mapping; the client cannot choose another application identity in its payload. Session changes cancel and close the linking UI. Automatic retries are forbidden when the link outcome is uncertain.

The owner provisioning reducer remains available for managed deployments. Self-service linking does not depend on pre-linking in a browser or by an administrator. Microsoft/Entra linking uses the same dual-proof model with a separately verified OIDC ceremony; passkey registration binds the current full application bearer to a separately verified browser WebAuthn ceremony. Manage passkeys under **Ключи доступа** when the server capability is enabled.

The Avalonia linking dialog also supports an explicit status check through `POST /api/auth/windows/link/status`. It requires the same validated Negotiate principal and application bearer proof as linking, and reports only linked/unlinked/linked-elsewhere. It never returns the other account identity. This read helps reconcile interrupted requests. Native credential requests preserve the selected deployment path prefix after checking the explicitly configured trusted HTTPS origin.
