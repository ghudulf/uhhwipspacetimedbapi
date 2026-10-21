# QR login (`bru-qr/1`)

QR is a transport for a short-lived challenge, not a credential. A username alone cannot authenticate a user. Two flows share one API session:

* **A — approval:** an already fully signed-in mobile device scans the desktop QR, shows the confirmation details, asks the user to type the desktop's six-digit code, and approves with its bearer token. The desktop redeems a full login; the approver already satisfied the account's second-factor policy.
* **B — enrolled device:** while signed in, a mobile device enrolls an ECDSA P-256 public key. Later it enters a username, creates a bound challenge and self-scans the returned QR. Its private key signs the challenge. This is a primary factor only: redemption runs the existing TOTP/WebAuthn policy and may return `RequiresTwoFactor` plus a temporary token instead of a full login.

Native camera scanning, confirmation UI and platform private-key keystores are client work, not implemented by this server change. Private keys never belong in API requests or logs.

## HTTP protocol

All new routes use `/api/auth/qr`, JSON bodies (unknown request members rejected), a 4096-byte request limit, no-store responses and disabled CORS. HTTPS is mandatory, except loopback in Development. Generic errors are `{ "code": "snake_case" }`; verification failures additionally report `attemptsRemaining`. Bearer-required endpoints use the existing direct token validator, require the `identity` claim and recheck current active-user state.

| Method and relative route | Request | Result |
| --- | --- | --- |
| POST `sessions` | `deviceName`, `platform`, optional `username`, `deviceId` | 201: `sessionId`, `pollSecret`, `approvalSecret`, `qrPayload`, base64 PNG `qrPng`, `verificationCode`, `expiresAt`, `pollIntervalMs` |
| POST `sessions/{id}/status` | `pollSecret` | `state`; approved sessions atomically return `state: completed` and the existing `LoginResult` as `login`, once |
| POST `sessions/{id}/cancel` | `pollSecret` | 204, idempotent |
| POST `sessions/{id}/peek` | `approvalSecret` | device/platform, times, masked source IP, origin, bound username and attempts; never code or secrets |
| POST `sessions/{id}/approve` | `approvalSecret`, `verificationCode`; full bearer required | 204; bound username must match the approving account |
| POST `sessions/{id}/deny` | `approvalSecret` | 204 |
| POST `sessions/{id}/complete` | `approvalSecret`, `verificationCode`, `deviceId`, base64url `signature` | 204 for a valid enrolled-device proof; all proof failures are 401 `qr_login_failed` |
| POST `devices` | `deviceName`, `platform`, base64 SPKI `publicKey`; full bearer required | 201 `deviceId`; only P-256, no trailing DER; duplicate/limit conflicts are 409 |
| GET `devices` | full bearer required | caller's non-revoked devices, names/platform, creation and last-use times; no public-key material |
| DELETE `devices/{deviceId}` | full bearer required | 204; only caller's device, otherwise 404 |

The origin is computed from the API request's scheme, host and path base, after configured trusted forwarded-header processing. Configure allowed hosts and trusted proxy forwarding correctly; do not accept arbitrary forwarded headers. Clients must not supply their own origin.

Payload: `bruqr://v1/session?s=<16-byte-base64url-id>&k=<32-byte-base64url-approval-secret>&o=<escaped-origin>`.
`QrLoginProtocol` is the shared source of truth: strict scheme/host/path/query/charset/length parsing, HTTP(S)-only absolute origins without user-info/query/fragment, device IDs, masking and signing bytes.

The signed UTF-8 message has newline separators and **no trailing newline**:

```text
bru-qr/1
<sessionId>
<approvalSecret>
<origin>
<lowercase-username>
<deviceId>
<verificationCode>
```

Use ECDSA P-256 with SHA-256 and IEEE-P1363 fixed-width `(r || s)` (64 bytes), base64url without padding. Device ID is base64url of the first 16 SHA-256 bytes of canonical SPKI DER. The API verifies a decoy key when no usable enrollment exists; unknown/inactive/revoked accounts, bad keys/signatures/codes/secrets all have the same proof-failure response and burn one attempt.

## State and secrets

`Pending -> Scanned` (peek), then `Approved -> Redeemed`. Pending or scanned sessions can instead become Denied or Cancelled; live sessions expire. Polling reports `pending|scanned|denied|expired|cancelled|consumed`, or exactly one `completed` result. Cancellation can also cancel an approved-but-unredeemed session. Approval and completion are forbidden from terminal states. Three wrong codes/proofs deny the session.

The singleton broker uses a single lock for transitions, injected `TimeProvider` for clocks and cryptographic randomness for IDs, two independent 32-byte secrets and uniformly generated zero-padded six-digit codes. Only SHA-256 secret hashes are retained, compared with `FixedTimeEquals`; no raw poll/approval secret is retained. Redemption burns the session **before** issuing a token; issuance failure cannot be retried with that session. A new flow is required.

## Threat model

* **QR phishing/relay:** the approver MUST compare payload origin `o` with the API origin it is signed in to/enrolled for **before sending any credential**. Confirm the peek origin, display-device name and masked IP; have the user type the display's verification code. Server enforcement cannot replace this client confirmation.
* **QR theft:** QR grants peek/deny/proof submission access, not poll access. Bearer approval still requires full authentication and the separately displayed code. Short TTL and three attempts constrain guessing. Protect screenshots and QR display channels.
* **Enumeration:** bound creation returns the same response shape for nonexistent users/devices; decoy sessions cannot be approved/redeemed. Completion uses uniform 401 bodies and decoy crypto work; this is not a guarantee of mathematically identical wall-clock latency across database/network conditions.
* **Replay:** unique challenge, signed origin/account/device/code, atomic one-time redemption and terminal-state checks prevent signature/token replay. Revoked or inactive-user keys are not returned by the store.
* **Diagnostics:** the QR path does not log request secrets, codes, tokens, usernames or IPs. Any future correlating diagnostics must use `SafeDiagnostics.Fingerprint`, never raw values.

## Configuration and deployment

Use **`Authentication:QrLogin`**, not top-level `QrLogin`: .NET configuration is case-insensitive and top-level `QrLogin` would collide with legacy `QRLogin`.

| Key | Default | Bounds |
| --- | --- | --- |
| `Authentication:QrLogin:SessionSeconds` | 120 | clamped 30–300 |
| `Authentication:QrLogin:MaxSessions` | 2000 | clamped 1–20000 |
| `Authentication:QrLogin:MaxSessionsPerAddress` | 5 | clamped 1–100 |

Sliding in-memory limits: 10 creations/minute/source IP; 5 username-bound creations/minute/username (case-insensitive); 90 status polls/minute/session. Live capacity and per-address limits also apply. Mutations sweep old windows. Terminal tombstones last until expiry plus one minute; bounded tombstone/window dictionaries fail closed at capacity rather than grow indefinitely.

Process restart loses sessions: clients must restart the flow. Multiple API instances require sticky routing or a single instance; this broker is not distributed. Persistent device registrations are separate from process-local sessions.

Publish the additive module table `QrLoginDevice` and reducers `RegisterQrLoginDevice`, `RevokeQrLoginDevice`, `TouchQrLoginDevice` **before** the API. Regenerate pinned 2.1.0 bindings and reducer-completion observers together. Trusted API identities alone can read keys/mutate enrollment; max five active devices per account. Backup and rehearse migrations; never delete production data.

Availability flags reuse `QRLoginGenerateEndpointEnabled` (create), `QRLoginStatusEndpointEnabled` (status), `QRLoginCancelEndpointEnabled` (cancel), `QRLoginValidateEndpointEnabled` (peek/approve/deny), and `QRLoginDirectEndpointEnabled` (complete/device management). Disable these to stop rollout/roll back access without removing the additive table. Keep the existing legacy-selection/availability compatibility policy.

Legacy `/api/auth/qr-login/direct|status|cancel|notify` return 410 `qr_login_moved` with the new replacement path, after their availability gates (disabled remains 503). Legacy page/generate/validate remain unchanged. The legacy WebSocket status orchestration truthfully fails `unsupported` so its poller sends `auth:qr-error`; cancel/notify stub methods throw rather than fabricate success. `AuthenticateDirectQRAsync` remains disabled.

The committed default legacy `QRLogin:EncryptionKey` is a deployment smell: override it through deployment secrets; this change does not rotate or alter it. The new protocol does not use that encryption key.

## Clients

The shared `BRU.Avtopark.Client` library implements the bounded HTTP adapter, origin comparisons, canonical device signing, display/self-scan state machine and approver logic without Avalonia or logging dependencies. The Avalonia Unity library supplies the same windows and view models to desktop and MAUI hosts.

### Using QR login

* **Desktop display (Flow A):** choose **«Вход по QR с телефона»**, optionally restrict the request to a username, and show the QR plus six-digit code. On the signed-in phone choose **«Подтвердить вход по QR»**, scan/paste, compare the displayed device, masked address and server, then type the six digits and approve. The desktop polls until completion and opens the existing authentication wizard. **«Новый QR»** cancels the old session before creating another; close/cancel stops polling.
* **Enrollment:** after signing in, choose **«QR-вход на этом устройстве»** in navigation, name the device and enable QR login. The current username is resolved using the existing authenticated JSON profile endpoint (`GET api/auth/profile`, bearer header, never a token query). Enrollment requires that endpoint to be available. A fresh P-256 key is registered with the current full-session bearer; local storage happens only after a 201 response and matching derived device ID. Existing local enrollment is not silently replaced. Lost/failed enrollment responses or failed local saves trigger deterministic-ID server revocation and local deletion. If rollback cannot be confirmed, the UI explicitly asks the user to inspect/revoke the server registration.
* **Passwordless self-scan (Flow B):** choose **«QR-вход на этом устройстве»** in the login selector, enter the enrolled username and press **«Войти по QR»**. Without enrollment no API request is sent. Otherwise the app creates a username/device-bound challenge, validates and locally renders its QR, checks the origin, signs the canonical message using the returned code and submits the proof. No camera is needed. TOTP/WebAuthn requirements pass unchanged to the existing wizard; the device key is not a second-factor bypass.
* **Approving another account:** bearer approval is the default. For username-bound sessions the current account is resolved only after QR origin validation; if it differs and a matching local enrollment exists, the approver uses that account's device proof instead. The server's verification-attempt counter is shown after wrong-code responses.
* **Revocation:** the device window lists the current account's server registrations. Revoking the local registration also deletes its local private key. All authenticated QR windows cancel and close when the application session changes. Navigation entries require connected, currently active access.

### Origin and transport rules

Both approver and self-scan paths call **`QrOrigin.IsSameServer` before sending any QR secret, signature or bearer approval**. It compares scheme, case-insensitive host, effective port and exact case-sensitive deployment path (trailing slashes trimmed; the configured API base's trailing `/api` segment is removed). User-info, query/fragment origins, lookalike hosts and HTTPS downgrades are rejected. A mismatched scanned origin causes zero network calls. A mismatched create response causes no complete, poll or cancel secret to be sent. The displayed image is generated locally with QRCoder from the validated payload, never decoded from the untrusted server `qrPng` field.

`QrLoginClient` requires HTTPS except HTTP loopback, caps requests at 4096 bytes and responses at 64 KiB, uses a 15-second per-call timeout, disposes every response and rejects redirects. **When supplying an HttpClient, configure `HttpClientHandler.AllowAutoRedirect = false`**; a client cannot undo a handler's already-followed redirect. The UI uses its own redirect-disabled handler and sends bearer headers only on authenticated operations. Never use a handler with credentials/default bearer headers for anonymous QR calls. Polling uses the server interval clamped to 500–5000 ms, steady cadence, local expiry plus two seconds of grace and at most three consecutive transient failures with bounded backoff. Every terminal state and disposal stops polling; a completed JSON result is surfaced once.

### Private keys and platform plug-ins

`DeviceKeyStore.Current` is an `IDeviceKeyStore` host extension point keyed by normalized server root and lowercase username. On **Windows**, the default store atomically writes only **CurrentUser DPAPI-protected** bytes; entropy and hashed filenames bind the key to origin/account. On **other platforms**, the default is **process memory only**, with a one-time warning that enrollment ends at restart. No private-key plaintext, adjacent encryption key or unprotected key file is ever written. DPAPI execution still requires Windows validation. Load returns an owned buffer; callers zero it after use. Platform stores must return detached private buffers, honor cancellation and support deletion. A mobile host may register a verified platform-keystore implementation before opening QR UI; this change does not pretend memory fallback is durable enrollment.

`QrScanner.Current` is an `IQrScanner` plug-in with `Task<string?> ScanAsync(CancellationToken)`. Return raw QR text or null for user cancellation; honor the cancellation token and never log/submit QR text. The working default **ManualQrScanner** is an accessible paste/type dialog with Enter/Escape support, not a camera implementation. Any camera plug-in must pass its result through `QrApproverFlow`, never submit credentials directly from scanned data.

New controls have Russian accessible names, live polite status text, keyboard default/cancel actions and textual states/countdowns. Native camera scanning, Android/iOS platform keystores, real MAUI mobile window/embedding behaviour, accessibility screen-reader and camera visual validation remain platform validation work. The generic MAUI build attempt and toolchain result are recorded in the handoff verification report; a successful generic build alone does not validate mobile targets or justify enabling unverified keystore code. No MAUI-only scanner or keystore code is added by this change.

The generic MAUI `net11.0` / `EnableMobileTargets=false` build passed on Linux after installing the project's pinned .NET 11 RC2 SDK and restoring its workloads. Android/iOS builds and device interaction were not exercised.
