# Headless OAuth Architecture

This document describes the two OAuth 2.0 authorization flows supported by this API: the standard browser redirect flow and the headless (backchannel) flow for non-browser clients.

---

## 1. Browser OAuth Flow (Standard)

The canonical OAuth 2.0 authorization code flow for browser-based clients. This flow is unchanged and spec-compliant.

```
Browser Client                    Authorization Server                  Resource Server
     |                                     |                                  |
     |-- GET ~/connect/authorize --------->|                                  |
     |   (redirect with client_id, etc.)   |                                  |
     |                                     |-- User not authenticated?        |
     |<-- 200 HTML login form -------------|                                  |
     |                                     |                                  |
     |-- POST ~/connect/authorize/callback |                                  |
     |   (username, password, requestId)   |                                  |
     |                                     |-- Authenticate user              |
     |                                     |-- Set cookie                     |
     |<-- 302 Redirect to ~/connect/authorize                                 |
     |                                     |                                  |
     |-- GET ~/connect/authorize --------->|                                  |
     |   (with session cookie)             |-- Build claims identity          |
     |                                     |-- Generate authorization code    |
     |<-- 302 Redirect to redirect_uri?code=... & state=...                  |
     |                                     |                                  |
     |-- POST ~/connect/token ------------>|                                  |
     |   (code, code_verifier, client_id)  |-- Validate code + PKCE          |
     |                                     |-- Issue tokens                   |
     |<-- 200 { access_token, ... } -------|                                  |
     |                                     |                                  |
     |-- GET /api/resource --------------->|---------------->                 |
     |   Authorization: Bearer <token>     |                 |-- Validate     |
     |<-- 200 { data } ----------------------------------------<             |
```

**Key endpoints:**
- `GET ~/connect/authorize` — initiates the flow; returns HTML login form for browsers
- `POST ~/connect/authorize/callback` — processes the login form submission
- `POST ~/connect/token` — exchanges the authorization code for tokens (JSON)

---

## 2. Headless OAuth Flow (Backchannel)

For non-browser clients (desktop apps, mobile apps, CLI tools, Avalonia client) that cannot follow browser redirects. The client drives the entire flow programmatically via JSON APIs.

```
Headless Client                   Authorization Server                  Resource Server
     |                                     |                                  |
     |-- GET ~/connect/authorize --------->|                                  |
     |   Accept: application/json          |-- Validate client                |
     |   (client_id, redirect_uri, etc.)   |-- User not authenticated         |
     |                                     |                                  |
     |<-- 200 { requestId, clientName,     |                                  |
     |          scopes, redirectUri,       |                                  |
     |          state }                    |                                  |
     |                                     |                                  |
     |   [Client renders its own login UI and collects credentials]           |
     |                                     |                                  |
     |-- POST /api/auth/oauth/authorize -->|                                  |
     |   { clientId, redirectUri, scope,   |-- Validate client (confidential) |
     |     state, code_challenge,          |-- Authenticate user              |
     |     code_challenge_method, nonce,   |-- Build claims identity          |
     |     username, password }            |-- Set session cookie             |
     |   OR { ..., token: "<jwt>" }        |-- Redirect to ~/connect/authorize|
     |                                     |-- Generate authorization code    |
     |<-- 200 { redirectUri, requestId,    |                                  |
     |          state }                    |                                  |
     |                                     |                                  |
     |   [Client extracts code from redirectUri query string]                 |
     |                                     |                                  |
     |-- POST ~/connect/token ------------>|                                  |
     |   { grant_type=authorization_code,  |-- Validate code + PKCE          |
     |     code, code_verifier,            |-- Issue tokens                   |
     |     client_id, redirect_uri }       |                                  |
     |<-- 200 { access_token,              |                                  |
     |          token_type, expires_in,    |                                  |
     |          refresh_token, id_token }  |                                  |
     |                                     |                                  |
     |-- GET /api/resource --------------->|---------------->                 |
     |   Authorization: Bearer <token>     |                 |-- Validate     |
     |<-- 200 { data } ----------------------------------------<             |
```

**Key endpoints:**
- `GET ~/connect/authorize` with `Accept: application/json` — returns `{ requestId, clientName, scopes, redirectUri, state }` instead of HTML
- `POST /api/auth/oauth/authorize` — backchannel authorize; authenticates user and returns `{ redirectUri, requestId, state }` where `redirectUri` contains the authorization code
- `POST ~/connect/token` — standard token exchange (JSON, unchanged)

---

## 3. Allowed Client Types for the Backchannel Endpoint

`POST /api/auth/oauth/authorize` is **only available to confidential and native clients**.

**Allowed:**
- `confidential` client type — server-side apps that can keep a secret
- `native` client type — desktop/mobile apps (Avalonia, etc.)

**Not allowed:**
- `public` client type — browser-based SPAs must use the standard `~/connect/authorize` redirect flow

The endpoint enforces this by checking the client type via OpenIddict's application manager. Public clients receive a `400 unauthorized_client` error with a message directing them to the browser redirect flow.

---

## 4. 2FA Handling in the Headless Flow

If the user has TOTP or WebAuthn two-factor authentication enabled, `POST /api/auth/oauth/authorize` returns an intermediate response instead of the authorization code:

```json
{
  "requiresTwoFactor": true,
  "tempToken": "<temporary-token>",
  "twoFactorType": "totp" | "webauthn"
}
```

The client then completes 2FA using the existing endpoints:

- **TOTP**: `POST /api/auth/totp/validate` with `{ tempToken, code }`
- **WebAuthn**: `POST /api/auth/webauthn/validate` with `{ tempToken, assertionResponse }`

After successful 2FA validation, the client retries `POST /api/auth/oauth/authorize` with the validated token (using the `token` field instead of `username`/`password`).

---

## 5. OAuth Consent in the Headless Flow

If the OAuth client requires user consent, the headless client uses `POST /api/auth/oauth/consent`:

```json
// Request
{ "requestId": "<requestId from step 1>", "grant": true }

// Response (grant=true)
{ "redirectUri": "/connect/authorize?..." }

// Response (grant=false)
{ "redirectUri": "<redirect_uri>?error=access_denied&..." }
```

The `requestId` is obtained from the initial `GET ~/connect/authorize` JSON response.

---

## 6. `~/connect/authorize` — Canonical Browser Redirect Endpoint

`GET ~/connect/authorize` and `POST ~/connect/authorize` remain **unchanged** as the canonical OAuth 2.0 browser redirect endpoints. They are spec-compliant and handle:

- Browser clients (returns HTML login form)
- Headless clients with `Accept: application/json` (returns JSON with `requestId` for the backchannel flow)

The backchannel endpoint (`POST /api/auth/oauth/authorize`) is a **separate, additive endpoint** that does not replace or modify the standard authorize endpoint in any way.

---

## 7. Token Endpoint

`POST ~/connect/token` is a standard OAuth 2.0 token endpoint. It always returns JSON per the OAuth 2.0 specification:

```json
{
  "access_token": "...",
  "token_type": "Bearer",
  "expires_in": 3600,
  "refresh_token": "...",
  "id_token": "..."
}
```

This endpoint is used identically by both browser and headless flows. No changes are needed for headless support.
