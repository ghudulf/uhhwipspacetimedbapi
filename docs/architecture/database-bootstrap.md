# Database bootstrap without manual password hashes

For an existing installation upgraded to the additive permission/index/reducer module, call owner-only `EnsureDefaultPermissions` once after publication. It is safe to repeat: missing default permissions and Administrator grants are inserted without removing custom grants or changing account/business IDs. Fresh initialization already performs this step. Use the deployment-owner connection, not an API or application-administrator credential.

Password failures remain rejected by `AuthenticateUserChecked`; the trusted API separately commits a best-effort `RecordAuthenticationFailure` audit after independently re-verifying the failed credentials. Valid credentials cannot be used to forge failures. No credential values belong in operational logs. The inactive `guest` record is only a demo seller, has no credential and cannot be claimed.

Publish the module first. Then run the bootstrap tool with the **database deployment owner's** credential. It creates the API identity through a real first connection, saves that identity's token, and asks the owner connection to provision it. The running API never needs the owner's token.

```bash
dotnet run --project tools/DatabaseBootstrap -- \
  --host http://localhost:3000 --database avtopark \
  --owner-token-file /private/path/owner-token \
  --credential-file /private/path/api-identity.json
```

The tool prompts twice for the initial `admin` password with input hidden. Passwords are hashed inside the existing module implementation; operators do not generate hashes or insert database rows. Use at least 12 characters. `--admin-login` selects an existing active Administrator account on a migrated installation. IDs, roles, relationships and existing passwords are preserved. Initial-password provisioning refuses to overwrite a password already present.

The owner token file may contain the raw deployment token or the `{ "token": "…" }` JSON returned by SpacetimeDB identity provisioning. On Linux both input files must be private (`chmod 600`). For unattended development setup, supply `--password-file /private/path/initial-password` instead of the prompt. Do not put secrets in command arguments, tracked configuration or logs. Remote provisioning requires HTTPS; loopback HTTP is supported for local development.

The tool reports committed provisioning and verifies actual password authentication through the new API connection. Run it with the same credential file again using `--trust-only` to provision/reconfirm API trust without changing any password:

```bash
dotnet run --project tools/DatabaseBootstrap -- \
  --host http://localhost:3000 --database avtopark \
  --owner-token-file /private/path/owner-token \
  --credential-file /private/path/api-identity.json --trust-only
```

## Start and restart the API

Configure `SpacetimeDB:Host`, `SpacetimeDB:DatabaseName` and `SpacetimeDB:CredentialFile` (environment equivalents use `__`) to match the bootstrap command. Run bootstrap as the API service account, or securely transfer the credential file to that account. Windows deployments must restrict its ACL to the service account and administrators. Linux files are created atomically with mode 600. The default credential path is under the current user's local application data, partitioned by server and database; explicit paths are easier for service deployments.

Starting the API before bootstrap also generates and persists its connection identity. The startup log prints its **public identity and credential path**, never the token. Stop it, run the bootstrap tool against that same path, then restart it. No guessed identity is needed. Reusing the file preserves the API identity across restart. Changing server/database requires a matching file; a mismatched file is rejected. Concurrent first connections cannot silently overwrite one another's identity.

`SpacetimeDB:Token` remains supported and takes precedence. Existing deployments using the old SDK-global `.spacetime_csharp_avtopark` storage can retain their token through this explicit configuration, or bootstrap a new separate API credential and revoke the old identity after cutover. The old file is not deleted. The new API storage does not automatically reuse an unscoped credential across servers.

## Owner authority and recovery

The initial database publisher is recorded as deployment owner. A normal application administrator or API identity cannot grant itself trust or provision initial passwords. Password provisioning and trust insertion are a single transaction; invalid provisioning grants neither. Repeated trust-only provisioning is safe. After a connection loss, retain the same API file and check whether the initial password now works before retrying password provisioning.

For databases published before deployment-authority tracking, follow the verified-owner migration in [migration instructions](migration-and-validation.md) once. Bootstrap does not adopt the first remote caller as owner. Never use database deletion as a bootstrap or migration step.

If a password already exists, use the application's password-change flow. Bootstrap is intentionally not a recurring password reset on startup. Keep new provider authentication disabled until separately configured.

## API-owned first connection and automatic development provisioning

You can skip the bootstrap executable entirely. The API's hosted coordinator connects without a token on first start and persists the token returned to **that API connection**. Enable this optional one-shot startup configuration:

```text
SpacetimeDB__Host=http://localhost:3000
SpacetimeDB__DatabaseName=avtopark
SpacetimeDB__CredentialFile=/private/path/api-identity.json
SpacetimeDB__Bootstrap__Enabled=true
SpacetimeDB__Bootstrap__OwnerTokenFile=/private/path/owner-token
SpacetimeDB__Bootstrap__InitialAdminPasswordFile=/private/path/initial-admin-password
```

The owner/password files contain plain secret inputs, not precomputed hashes. They must be private; use the OS secret mount mechanism in a deployment. For an existing admin password, omit `InitialAdminPasswordFile` so startup grants trust only. `Bootstrap:AdminLogin` defaults to `admin`.

After the API establishes its own identity, a temporary owner connection submits the owner-authorized provisioning reducer for that exact identity. The owner token is never used as the API connection token and is never copied into its credential file. Startup waits for the trust row on the API connection before readiness or OAuth-client initialization. Logs distinguish connecting, awaiting trust, provisioning, ready and failure. The registered `database_provisioning` readiness check remains unhealthy while trust is missing.

Remove/disable bootstrap configuration and unmount its owner/password inputs after successful initialization. Subsequent starts reuse the API's persisted identity. A revocation during a running instance is detected and **does not** trigger automatic re-provisioning. Provisioning failure is reported without repeatedly submitting a password mutation; correct configuration and restart or provision manually. If an API credential file is lost, provision its newly generated identity using trust-only mode; existing user passwords and accounts are preserved.
