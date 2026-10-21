# Debug traffic and DataGrid diagnostics

Traffic logging defaults to **`redacted` in every build/environment**, not only
Development. Business data remains visible; earlier identifier-free promises no
longer describe this facility.

| Level | Behavior |
|---|---|
| `off` | Disable traffic/frame/row capture; ordinary operational logs remain |
| `redacted` (default) | Raw JSON/text with only credential values masked |
| `full` | No masking, including credentials; explicit unsafe opt-in |

Full mode emits **`unsafe debug traffic logging enabled`** at startup and once per
opened rolling log file. All body/frame output is capped even in full mode,
default **64 KiB**, with `…[truncated N bytes]` and UTF-8-safe boundaries. For
unknown-length HTTP streams, N describes the observed omitted prefix; the logger
does not drain an entire stream just to calculate its length.

## Configuration

API keys (environment replaces `:` with `__`):

| Key | Default |
|---|---|
| `Diagnostics:Traffic:Level` | `redacted` |
| `Diagnostics:Traffic:MaxBodyBytes` | `65536` |
| `Diagnostics:Traffic:Websocket` | `true` |
| `Diagnostics:Traffic:Rows` | `false` |
| `Diagnostics:Traffic:MaxRows` | `20` |

For example `Diagnostics__Traffic__Level=full` or
`Diagnostics__Traffic__Rows=true`. Rows enables SpacetimeDB snapshot samples;
full mode enables those samples too. Clients use `BRU_DEBUG_TRAFFIC` and
`BRU_DEBUG_TRAFFIC_MAX_BYTES`; bound display-item dumps sample 20 rows by default.
Invalid levels/limits fail validation, rather than enabling unsafe output.

## Masking contract

The pure `BRU.Avtopark.Contracts.DebugPayload` API uses a streaming JSON
reader/writer: duplicate keys, property order, numeric lexemes (including huge
numbers/exponents), booleans and nulls are preserved. Credential values become
`"***(length)"`, including containers. Names are case-insensitive with `_` and `-`
removed:

`password`, `currentpassword`, `newpassword`, `passwd`, `secret`, `clientsecret`,
`token`, `accesstoken`, `refreshtoken`, `idtoken`, `temptoken`, `authorization`,
`cookie`, `setcookie`, `flowsecret`, `pollsecret`, `approvalsecret`, `browsertoken`,
`verificationcode`, `totpcode`, `otp`, `pin`, `signature`, `assertion`, `privatekey`,
`apikey`, plus names ending in `token`, `secret` or `password`.
Credential hashes are protected too, including token reducers' `PresentedHash`
and `NewHash`. Auth-specific previews additionally protect generic `code`,
`credential`, `qrPng`, `qrPayload`; business `code` fields are not globally removed.
`SafeDiagnostics.JsonShape` remains available for intentionally stricter paths.

Malformed JSON/forms/text retain bounded text with recognizable credential
assignments masked. Binary is `[binary N bytes]`. This is not a universal secret
detector for arbitrary text. Header `Authorization`, `Cookie`, `Set-Cookie` and
`X-Api-Key` values keep scheme/name plus a stable eight-hex SHA-256 fingerprint.
URL query `access_token`, `token`, `code`, `state`, `id_token`, `refresh_token`,
`client_secret` are masked. Full bypasses all masking.

**Usernames, IDs, names, emails, phone numbers, buses, employees and sales are not
masked at redacted.** Logs contain confidential business data: restrict access,
retention and exports; review/redact before sharing.

## Output and locations

Readable Console/Debug/`.log` records include timestamp, level, SourceContext,
trace ID, rendered structured properties and exception. For example:

```text
2026-10-21 12:00:00.123 +00:00 [Debug] TrafficLoggingMiddleware TraceId=abc HTTP response GET /api/buses Status=200 ElapsedMs=4 Body="[{\"model\":\"MAZ\",\"capacity\":null}]"
2026-10-21 12:00:00.124 +00:00 [Debug] - TraceId=- DataGrid Screen=BusManagementViewModel Page=1 Total=1 RowCount=1 BoundRows="[{\"Model\":\"MAZ\",\"Capacity\":null}]"
```

CR/LF and terminal escapes are escaped, including exceptions, to prevent forged
physical records. `.jsonl` sinks retain one valid JSON object per line. Files
roll daily and at 10 MiB, retaining at most 14 files per sink.

- API: `logs/app-*.log`, `logs/app-*.jsonl`, relative to process working directory.
- Clients: `LocalApplicationData/BRU.Avtopark.TicketSalesApp/logs/`, `app-*`
  desktop or `maui-*` MAUI. The directory is printed at startup and shown in About
  and system information.

Application detail is Debug, frameworks Information, with OpenIddict detail
retained. `Microsoft.AspNetCore.Hosting.Diagnostics` stays Warning because its
request-start messages contain OIDC query strings. The post-trace traffic
middleware replaces that detail with method/URL/selected headers/body/status/
duration and trace ID. HTTP failures also log at Warning. Health/alive/metrics
are excluded. Streaming/SSE, upgrades, files and HTML are skipped by default;
passkey/Entra HTML is never captured, even in full. HTTP previews replay identical
bytes to endpoints/callers; response teeing stores only a capped prefix.

## Debug a blank DataGrid

1. Use `redacted`, reproduce one load, note trace/request/operation ID and page.
2. Compare HTTP `Body`, WebSocket `RECEIVED RAW`/`SENDING`, operation `Result`:
   **did the server send the field**, and was it null/zero or named differently?
3. Compare `CollectionPayload RawNode/ParsedCollection` and parser
   `MissingOrDefaultPaths`, `RawNode`, `ParsedModel` (bus/job/sale). Paths such as
   `$.model` and `$.jobTitle` identify missing/default source fields.
4. Compare `DataGrid BoundRows`: public properties/fields of the actual bound
   display items, including null/default values. Raw-correct/bound-wrong points
   to parsing/mapping; bound-correct/grid-blank points to binding/template or UI
   lifecycle problems.

For SpacetimeDB, correlate reducer `Arguments`, submitted/committed/rejected
status/duration, connection phases, per-table applied counts, `ReadAsync snapshot`
summaries and subscription epoch/watermark invalidations. Temporarily enable
`Rows=true` for first-20 samples. Empty snapshots can indicate RLS/subscription/
trust/readiness, rather than a DataGrid fault.

Production: keep `redacted`, restrict log access and retention, collect only the
needed interval, and never assume logs are safe to publish. Full can expose
reusable login/refresh proofs: disable it after debugging and rotate credentials
captured in exported logs.
