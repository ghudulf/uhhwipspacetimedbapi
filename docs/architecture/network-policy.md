# Plaintext network policy

The pure, DNS-free `BRU.Avtopark.Contracts.NetworkPolicy` is shared by API and
clients. The default is **`lan`**, independent of Development/Production.

| Mode | HTTP / WS permitted for |
|---|---|
| `never` | Nothing, including loopback (HTTPS / WSS only) |
| `loopback` | Loopback addresses and `localhost` |
| `lan` (default) | Loopback plus any host that is LAN-scoped by address/name |
| `any` | Every host; explicit opt-in, including public networks |

LAN addresses are RFC1918 (`10/8`, `172.16/12`, `192.168/16`), IPv4 link-local
`169.254/16`, CGNAT `100.64/10` (including Tailscale), IPv6 link-local `fe80::/10`
and unique-local `fc00::/7`, including IPv4-mapped IPv6 forms. LAN names are
single-label names or names ending in `.local`, `.lan`, `.home.arpa`, `.internal`
or `.localdomain`. Name comparison ignores case and a trailing dot. This is a
scope convention, **not authentication or proof that a host is physically local**;
there is no DNS lookup. Public names/addresses require TLS except in `any`.
User-info and lookalikes such as `192.168.1.1.evil.com` are not private. Numeric
IPv4 spellings are classified using canonical `Uri` / `IPAddress` parsing.

## Configuration

- API: `Network:PlaintextPolicy`, or environment `Network__PlaintextPolicy`.
- Desktop/MAUI/shared clients and the bootstrap CLI: `BRU_PLAINTEXT_POLICY`.
- Missing/empty configuration means `lan`. Invalid values parse as `never` in
  the pure helper and fail configuration validation with an error. Use the same
  intended mode on the server and clients; neither silently negotiates a downgrade.

For example, `Network__PlaintextPolicy=lan` on the API and
`BRU_PLAINTEXT_POLICY=lan` on a client permit `http://192.168.1.20:5000/api/` and
its `ws://` realtime endpoint. Discovery probes HTTP port **5000 first**, then
HTTPS port **5001** in a second pass only if no plaintext candidate responds.
Each candidate must satisfy policy. `never` skips all HTTP probes.

## Server boundaries

HTTP returns **426** only when the peer address is outside the selected policy.
`/health` and `/alive` remain exempt even in `never`. The peer is
`HttpContext.Connection.RemoteIpAddress` **after trusted forwarded-header
processing**; a trusted forwarded HTTPS scheme counts as HTTPS. Configure
`ReverseProxy:KnownProxies` correctly; arbitrary forwarding headers are not trusted.
Cookies use `SameAsRequest`, except `never` uses `Always` (Secure).

CORS accepts explicit `Cors:AllowedOrigins` plus policy-scoped origins through
`SetIsOriginAllowed`; there is no wildcard-with-credentials policy. `never` adds
no automatic origins. Authentication ceremonies that disable CORS still do so.
WebSocket browser origins still require the exact `Realtime:AllowedOrigins`
allow-list; the plaintext policy does not relax origin checks. QR approver and
self-scan matching still compares the exact scheme: HTTP and HTTPS are different
servers, even for the same hostname and port.

## Protocol/platform constraints are not relaxed

- Entra's Microsoft authority and authorization URL must remain HTTPS. Redirects
  remain HTTPS, with the existing Development localhost exception; Microsoft
  rejects non-localhost HTTP redirect URIs. An HTTP LAN API can use a separately
  configured HTTPS callback, not an HTTP LAN callback.
- Passkey RP/origin configuration requires HTTPS or HTTP loopback. Browsers only
  expose WebAuthn in secure contexts: **`http://192.168.x.x` does NOT work;
  `http://localhost` does**. This restriction also applies under `any`.
- Windows Negotiate endpoints and the explicitly trusted native origin remain
  HTTPS-only. App Installer HTTPS update delivery is likewise unchanged.

## Threat model

**Plaintext on a LAN exposes bearer tokens, refresh tokens and login/QR secrets
to on-path observers.** LAN naming/addressing is not a trusted security boundary.
Use `never` or `loopback` on untrusted networks, or use a trusted VPN (for example
Tailscale) and control who can join it. HTTPS remains recommended when practical.
