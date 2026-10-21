# Secrets and credential migration

## Committed-secret inventory

All fingerprints below are the first eight hexadecimal characters of SHA-256;
they are identifiers, not usable secret material. Historical configuration values
were hashed from revision `7975e59`. Treat them as compromised, not as defaults.

| Tracked path | Key | Class / fingerprint | Disposition |
|---|---|---|---|
| `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/appsettings.json` | `JwtSettings:Secret` | signing secret `68835d90` | Removed; hash revoked; owner must rotate |
| same | `QRLogin:EncryptionKey` | legacy QR encryption secret `bc95736a` | Removed; hash revoked; owner must rotate |
| same | `SpacetimeDB:AdminToken` | owner JWT `28a4f03a` | Removed; hash revoked; owner must revoke/replace |
| `BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService/DataProtectionKeys/key-6c9f99f5-3174-4eba-b458-3b7875284042.xml` | `masterKey/value` | Data Protection key `f19ef4a8` | Removed; key directory ignored; owner must retire deployed copies |

Inventory covered all tracked files, including appsettings, launch settings,
register_client.json, spacetime configuration, scripts, docs, packaging, tests,
tools and `SAMPLE_CODE_FOR_REFERRING_TOHOWTODOSOMETHING/`. JWT-shaped values,
long secret/password/key/token assignments, PEM markers and XML key material
were checked. `register_client.json:client_secret` (`1e1ace94`) and
`BRU.Avtopark.TicketSalesAPP.Avalonia.Unity/OAUTH_IMPLEMENTATION.md:clientSecret`
(`a383d1eb`) are explicitly descriptive placeholders, not operational credentials.
Sample source hits were counter names, UI markup and captured example HTML;
test passwords are synthetic. No additional operational secret was identified.

## Configuration and startup

Precedence: **environment variables > mounted secret files > appsettings**.
Set `Secrets:Directory` or `BRU_SECRETS_DIR`; `/run/secrets` is used by default
only when it exists. One file per key: `JwtSettings__Secret` maps to
`JwtSettings:Secret`. Subdirectories are ignored. Files must be at most 8 KiB;
one final LF (or CRLF) is stripped. External symlinks and Unix group/world-readable
files are rejected with a filename-only error. Mounts must be stable and writable
only by administrators/service owners; do not change files during startup.

Generate a random secret without exposing its contents:

```sh
dotnet run --project tools/DatabaseBootstrap -- generate-secrets --out /var/lib/bru/secrets
```

Output contains only the filename and fingerprint. Existing files are never
overwritten without `--force`. Unix files/directories use 0600/0700. Windows
storage disables ACL inheritance and grants only the current user FullControl;
the Windows branch is compile-verified, not runtime-tested on Linux.

JWT secrets require at least 32 UTF-8 bytes, 12 distinct characters, and must not
match a revoked hash. Only Development with an absent secret generates a random
48-byte per-process secret and emits one warning: tokens do not survive restarts.
Missing, weak or revoked JWT secrets otherwise fail startup with
`JwtSettings:Secret is missing, weak or revoked` and generation instructions.
An explicitly weak/revoked legacy QR key fails startup; an absent QR key derives
from the effective JWT secret using HKDF-SHA256 context `bru-qr-legacy/1`.
OpenIddict uses a separate HKDF context `bru-openiddict/1` to derive its 256-bit key,
not truncation or padding. An invalid/revoked owner token is ignored with a
key-name-only warning; absent tokens disable log fetching. No SDK/client-token or
`SPACETIME_TOKEN` fallback bypasses this policy.

Data Protection keys use `DataProtection:KeysDirectory`, defaulting outside the
repository to the service user's local application data directory under
`BRU/DataProtectionKeys`. It is created owner-only. A persistent deployment must
configure a persistent, private key-ring volume. Never commit a key ring.

### Deployment examples

Docker: bind-mount a service-owned 0700 directory containing 0600 files at
`/run/secrets` (`--mount type=bind,src=/var/lib/bru/secrets,dst=/run/secrets,readonly`).
Run as the same UID that owns these files, not as an unrelated container UID.

Kubernetes (provision the Secret through your secret manager; do not commit it):

```yaml
volumes:
  - name: secrets
    secret:
      secretName: bru-api
      defaultMode: 0400
containers:
  - name: api
    volumeMounts:
      - name: secrets
        mountPath: /run/secrets
        readOnly: true
```

Kubernetes projected secret symlinks resolve within the mount. Ensure the pod's
UID owns/read-accesses the projected files; do not use group-readable permissions
to compensate. Alternatively copy from the secret volume into a service-owned
0700 directory using an init container, setting each copied file to 0600.

systemd:

```ini
[Service]
User=bru
UMask=0077
Environment=BRU_SECRETS_DIR=/var/lib/bru/secrets
Environment=DataProtection__KeysDirectory=/var/lib/bru/data-protection
```

## Mandatory rotation runbook

**Git history still contains all former values. Removing tracked literals is not
revocation. Rewriting history is the owner's decision; this migration does not
rewrite history.** Review deployment logs/backups for exposure without copying
secret contents into issue reports.

1. Take a database and key-ring backup into restricted storage. Plan a maintenance
   window and tell users they will need to sign in again.
2. Generate a new JWT secret outside the repository, distribute it consistently to
   every API instance, and restart them together. All previously issued JWTs and
   JWT-derived OpenIddict material become invalid. The new OpenIddict derivation
   also invalidates historical OpenIddict material; no legacy truncation remains.
3. Remove or rotate the legacy QR encryption key on all instances. Already-issued
   legacy bearer QR payloads become unreadable. New QR sessions use the separate
   one-time protocol and do not restore old legacy payloads.
4. Retire the compromised Data Protection key from every deployed key ring; start
   a fresh ring in the private external directory and restart all replicas.
   Cookies, antiforgery tokens and any other DataProtection-protected payloads
   become invalid. Do not copy the compromised key into the new ring.
5. Revoke/rotate the leaked SpacetimeDB owner credential through the deployment's
   SpacetimeDB CLI/identity provider (`spacetime login --help`, `spacetime logout
   --help`, `spacetime login show --help` for the installed version). Logging out
   locally alone is NOT server-side JWT revocation: coordinate issuer-side
   revocation/key rotation with the operator. Verify the old credential is denied
   by the host. Do not display tokens in shared terminals. Keep the runtime
   AdminToken absent unless owner-log fetching is required; a new independent
   API connection credential remains separate from the owner credential.
6. Verify fresh login, refresh/OpenIddict, QR, cookies and log-fetch behavior;
   confirm all replicas reject old tokens. Do not restore compromised secrets
   as rollback. Keep incident and backup retention under the owner's policy.
