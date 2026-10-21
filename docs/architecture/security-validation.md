# Local security validation

This ledger records local checks, not a penetration test or production certification.
The three GitHub workflows remain unchanged and disabled; only the repository owner
can re-enable their runs. No analyzer suppression comments/pragmas were added.

## SecurityCodeScan checkpoint

SecurityCodeScan.VS2019 **5.6.7**, the latest stable version resolved by the pinned
workflow add-action, was injected using an external `DirectoryBuildPropsPath`.
The temporary props imports the repository's real props, adds the analyzer package,
sets `ErrorLog` to an external per-project SARIF file, and sets
`AdditionalFileItemNames=$(AdditionalFileItemNames);Content`, exactly as the add-action
does. No tracked project/props file contains the analyzer dependency.

From each project's directory, the scan uses:

```sh
dotnet build --no-incremental -p:UseSharedCompilation=false \
  -p:DirectoryBuildPropsPath=/absolute/external/scs.props
```

Targets: `server`, ApiService (including Services, Contracts and ServiceDefaults),
AppHost, Desktop (including the Avalonia library and Client), generic MAUI
(`-p:EnableMobileTargets=false`), Client explicitly and DatabaseBootstrap. This
extends the workflow's five entry projects to cover the shared client and tools.
The workflow's installed SDKs do not satisfy Desktop's project-local **10.0.201**
pin; that SDK was installed locally rather than changing any `global.json`.
The other declared SDKs are 8.0.419, 10.0.100 and 11.0.100-rc.2.26504.105.

Before: **71** SCS diagnostics (64 SCS0005, seven SCS0027).
After: **66** (63 SCS0005, three SCS0016). Duplicate compiler build-summary lines
are not counted; each SARIF result is counted once.

| Finding | Disposition / evidence |
| --- | --- |
| Native Services password-salt `System.Random` (one SCS0005) | Fixed: `RandomNumberGenerator.Fill`; database password formats were not rewritten. |
| Seven AuthController redirect alerts (SCS0027) | Relative, escaped destinations were not open redirects, but three successful form redirects unnecessarily carried bearer tokens. Removed those query tokens and used `LocalRedirect` for explicit local-only destinations. |
| OAuth client register/update/delete form authorization | Real issue found during redirect review: the actions allowed anonymous callers and decoded unsigned form JWT claims. Fixed with explicit Bearer/Administrator authorization and in-action principal checks before any service call. Regressions reject forged form tokens and authenticated nonadministrators. Legacy browser clients must now supply a verified Authorization header; a hidden/query token alone is not authorization. |
| Three resulting SCS0016 antiforgery alerts on those forms | Justified: authentication is explicitly the nonambient Bearer scheme, not the Cookies scheme. Cross-site cookie submission cannot authorize these actions. No blanket antiforgery exemption or source suppression was added. |
| 49 module SCS0005 results | Synthetic owner-only demo data in `INITreducers.cs` (47), public password salts in `Lib.cs` (one), and opaque **public** WebAuthn user handles (one). Transaction RNG is not claimed as an OS CSPRNG; none supplies authentication secrets. Preserve existing credentials/handles; do not change the additive migration or WASI runtime contract to silence this pattern. |
| 13 API SCS0005 results | Synthetic realtime example telemetry (11), frame-loop/retry timing (two). No authentication/cryptographic decision depends on these values. |
| One Avalonia SCS0005 result | Reconnect jitter in `ApplicationClientCoordinator`, not an authentication secret. |

`tests/Api.Contracts/SecurityScanRegressionTests.cs` covers the authorization change;
the existing operation-coverage drift test remains mandatory.

## DevSkim checkpoint

DevSkim **1.0.100+ea92e6f3cc**, default rules/severities/confidence, archives off,
no rule exclusions, ignore globs `**/.git/**,**/bin/**` (the workflow defaults):

```sh
devskim analyze -I /absolute/clean-source-snapshot -O /external/devskim.sarif \
  -g '**/.git/**,**/bin/**' --skip-excerpts
```

`--skip-excerpts` omits source excerpts from the artifact, not detections. The
workflow scans before building: use a clean tracked-source snapshot, not this
workspace's compiler-generated caches. A raw working-tree run reported **389**
alerts, including 108 generated-file results not present in a fresh checkout.
Comparable tracked-source runs reported **281 before / 281 after**. Equal totals
do not mean no fixes: test vectors add two intentionally rejected HTTP URLs, while
the production LAN HTTP literal and missing-link protection were removed.

| Rule / comparable after count | Triage |
| --- | --- |
| DS610000: 1 → 0 | Fixed Web layout's external `_blank` link with `rel="noopener noreferrer"`. |
| DS137138: 17 → 19 | Real remote-HTTP credential exposure fixed: shared API roots reject remote HTTP; legacy `CreateClient` applies the same boundary before attaching bearer credentials and disables redirects; LAN discovery probes HTTPS. Added loopback/HTTPS preservation and remote-HTTP rejection regressions. Remaining matches are protocol documentation, loopback development/registered OAuth callback, explicitly rejected URL test vectors and unshipped reference material. |
| DS162092: 113 → 112 | Loopback development defaults, synthetic tests and discovery/status strings; not a hardcoded remote credential. HTTPS is now enforced for remote API roots. |
| DS176209: 91 | TODO/incomplete-functionality markers, mostly third-party/reference code. Quality inventory, not proof of a vulnerability; does not certify unfinished sample features as production-ready. |
| DS172411: 28 | JavaScript timer callbacks use functions, not user-controlled code strings; includes vendored Bootstrap and unshipped examples. No eval/string-code sink was found at these alerts. |
| DS172412: 12 | Words such as unsafe in rejection/test descriptions; not C# unsafe blocks or pointer operations. |
| DS173237: 4 | Known-placeholder rejection literals in `SecretConfiguration` (three) and an empty mounted configuration slot (one), not stored credentials. |
| DS173238: 2 | Synthetic negative security-regression vectors, not live credentials. |
| DS117838: 1 | Copied Yandex HTML/telemetry/reference page in `SAMPLE_CODE_FOR_REFERRING_TOHOWTODOSOMETHING`, not a runtime provider key. Do not publish this reference directory as application content. |
| DS126858: 4 | SHA-1 required by legacy TOTP interoperability; MSIX `/sha1` selects a certificate thumbprint while `/fd SHA256` sets the signature digest; remaining matches are test/reference material. No password hashing or new signature uses SHA-1. |
| DS440100: 8 | Explicit NIST P-256 curve selection for QR proofs and software-authenticator tests is intentional, not custom curve parameters or weak cryptography. |

No DevSkim ignore/rules file or source suppression was used. Preserve raw findings
and these dispositions; do not call this a zero-alert scan.

## CodeQL checkpoint

CodeQL CLI **2.27.3**, `codeql/csharp-queries` **1.10.2**, suite
`csharp-security-and-quality`. The old database was not reused: comparing its
source archive against the checkout found six changed source/project files,
including Services files. Fresh before/after databases built ApiService and its
Services/Contracts dependencies with compiler tracing:

```sh
codeql database create /external/new-db --language=csharp --source-root="$PWD" \
  --command='dotnet build BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService --no-incremental -p:UseSharedCompilation=false' --threads=2
codeql database analyze /external/new-db \
  codeql/csharp-queries:codeql-suites/csharp-security-and-quality.qls \
  --format=sarifv2.1.0 --output=/external/codeql.sarif --threads=2 --ram=2200
```

Both runs completed: **192 seconds before, 185 seconds after**, below the
40-minute total CodeQL timebox. **687 before / 673 after** raw results. C# scope
only: this is not the workflows' Actions/JavaScript CodeQL matrix certification.

| Rule / after count | Triage |
| --- | --- |
| cs/exposure-of-sensitive-information: 16 → 0 | Fixed raw email/subject logging in EmailService, MagicLinkService, AuthOrchestrationService and InputSanitizationService. Delivery failures retain exception type, not provider exception text or recipient. Tests capture malicious private email/subject values and verify omission. |
| cs/log-forging: 172 → 163 | Fixed at the configured sinks: API Serilog console/debug/rolling-file sinks now use escaped JSON records; Microsoft console logging uses JSON too. A regression supplies CR/LF/terminal-control characters in template, property and exception text and verifies exactly one parseable physical JSON record. Remaining source-to-ILogger alerts do not model the configured formatter; they are retained, not suppressed. Consumers must parse JSON records, not render property values as independent log entries. |
| cs/user-controlled-bypass: 9 | TokenTest branches only choose how to display the supplied token, not grant authentication; Entra/passkey purpose=link/register branches require a separately verified actor; Windows request flags enforce native HTTPS and then verify Negotiate/application proofs; Admin flags reject empty dictionaries after administrator checks; native-refresh opt-in is followed by refresh-proof validation. Input-dependent branches do not bypass those mandatory checks. |
| cs/local-not-disposed: 2 → 0 | Fixed disposal of MailMessage and the realtime demo DataTable. |
| cs/path-combine: 5 | Constant installation/static-resource components; credential paths use a SHA-256 scope filename and random constant-prefix temporary filename. Root directories are trusted service configuration, not request path components. No request-controlled rooted path reaches these joins. |
| cs/dereferenced-value-may-be-null: 3 | Selector firstValidIndex is populated whenever validCount is incremented; validCount <= 1 returns before these uses. The analyzer does not connect the correlated locals. |
| cs/constant-condition: 10; cs/complex-condition: 5 | Non-security simplification opportunities; no authorization removed to silence them. |
| cs/empty-catch-block: 14; cs/catch-of-all-exceptions: 241 | Cleanup, cancellation, fail-closed authentication and legacy error-boundary quality inventory. These are not automatically vulnerabilities; uncertain commits are not retried. Existing lifecycle/authorization regression suites remain mandatory. |
| cs/useless-assignment-to-local: 66; cs/missed-readonly-modifier: 18; cs/missed-ternary-operator: 3; cs/missed-using-statement: 5 | Style/generated-code opportunities; retained as quality debt rather than mislabelled as fixed security flaws. |
| cs/nested-if-statements: 9; cs/simplifiable-boolean-expression: 2; cs/useless-cast-to-self: 2; cs/useless-upcast: 15 | Style opportunities; no runtime security impact at the reported locations. |
| cs/empty-block: 1; cs/misleading-indentation: 1 | Existing empty callback / indentation quality debt, not an authorization or validation bypass. |
| cs/linq/missed-select: 14; cs/linq/missed-where: 6; cs/string-concatenation-in-loop: 1; cs/inefficient-containskey: 64 | Performance/style inventory; not injection or untrusted-code execution. |
| cs/useless-gethashcode-call: 11; cs/useless-tostring-call: 5 | Generated BSATN / redundant conversion quality debt. |

Adjacent boundary review fixed production cookies (`Secure=Always`), enforced HTTPS
before API endpoints outside Development, and replaced wildcard CORS with an
explicit origin list (empty by default). Tests reject wildcard, credential-bearing,
path-bearing and plaintext remote CORS origins, reject production HTTP before the
endpoint, and accept configured HTTPS. No source query or rule was suppressed.

### Deployment implications

- Configure `Cors:AllowedOrigins` / `Cors__AllowedOrigins__0` only for actual browser
  frontends, as exact HTTPS origins; same-origin pages/native clients need no CORS
  grant. Wildcards and remote HTTP fail startup validation. HTTP loopback origins
  are accepted only in Development.
- Outside Development, non-HTTPS requests return **426** without executing endpoints.
  TLS termination must supply forwarding headers from explicitly configured
  `ReverseProxy:KnownProxies`; do not trust arbitrary forwarded headers or expose a
  plaintext backend to untrusted clients. Health checks must use the trusted TLS
  boundary too. Cookies are always Secure in these deployments.
- API log files/console/debug records are now JSON, including escaped exception
  text. Update log consumers accordingly; this does not permit logging credentials.
- These local checks do not establish real proxy/certificate/domain/browser security.

## Dependency checkpoint

Every one of the **17** tracked projects was restored without analyzer injection,
then audited from its project directory with its declared SDK:

```sh
dotnet restore PROJECT.csproj # generic MAUI adds -p:EnableMobileTargets=false
dotnet list PROJECT.csproj package --vulnerable --include-transitive --format json
dotnet list PROJECT.csproj package --deprecated --include-transitive --format json
```

NuGet source: `https://api.nuget.org/v3/index.json`. Counts below are package/version
entries per graph, not build warnings or distinct CVEs. Both before/after runs have
the same results: **one vulnerable package in AppHost only**, **48 distinct
deprecated package/version pairs** across the graphs. No package family was
upgraded or advisory suppressed under the guise of a safe patch.

| Project (directory/stem abbreviation) | Vulnerable before/after | Deprecated before/after |
| --- | --- | --- |
| ApiService | 0 / 0 | 28 / 28 |
| TicketSalesApp.Services | 0 / 0 | 14 / 14 |
| BRU.Avtopark.Contracts | 0 / 0 | 0 / 0 |
| BRU.Avtopark.Client | 0 / 0 | 0 / 0 |
| Avalonia library | 0 / 0 | 14 / 14 |
| Desktop | 0 / 0 | 14 / 14 |
| generic MAUI | 0 / 0 | 14 / 14 |
| DatabaseBootstrap | 0 / 0 | 14 / 14 |
| Api.Contracts | 0 / 0 | 26 / 26 |
| Architecture.Integration | 0 / 0 | 26 / 26 |
| Client.Infrastructure | 0 / 0 | 0 / 0 |
| Client.Presentation | 0 / 0 | 14 / 14 |
| DatabaseFixture | 0 / 0 | 0 / 0 |
| StdbModule | 0 / 0 | 0 / 0 |
| AppHost | 1 / 1 | 9 / 9 |
| ServiceDefaults | 0 / 0 | 2 / 2 |
| Web | 0 / 0 | 2 / 2 |

| Dependency finding | Disposition / required owner action |
| --- | --- |
| AppHost → KubernetesClient **15.0.1**, Moderate [GHSA-w7r3-mgwf-4mqq](https://github.com/advisories/GHSA-w7r3-mgwf-4mqq) | **Unresolved vulnerability, not a false positive.** Advisory affects versions below **17.0.14**; forcing 15.x → 17.x is a major dependency override, outside the authorized safe patch/minor scope. The owner must plan a compatible Aspire/Kubernetes dependency upgrade and validate the orchestrator. Do not use untrusted Kubernetes configuration. This finding prevents a clean security signoff even though the application/test graphs report zero vulnerable packages. |
| App.Metrics 4.3.0 family (15 pairs) | Legacy/unmaintained metrics packages; replacement requires a telemetry migration, not a patch. No vulnerability was reported for these versions by this NuGet feed snapshot. |
| Aspire Hosting/AppHost/Dashboard/Orchestration 9.0.0 and ServiceDiscovery 9.0.0 families (six pairs) | Deprecated orchestration/service-discovery generation; coordinate upgrade with the unresolved Kubernetes advisory and SDK/platform support. |
| IdentityModel/JWT families 6.25.1, 6.34.0, 7.1.2, 7.4.0 (16 pairs) | Deprecated authentication dependencies across different graphs. Preserve pinned OpenIddict 4.1.0 interoperability; moving families requires explicit authorization and authentication migration tests. Do not force one global major version through all graphs. |
| OpenIddict 4.1.0 family (seven pairs) | Deprecated upstream; intentionally retained compatibility boundary. A major OpenIddict upgrade is not a local patch/minor fix. |
| Microsoft.AspNetCore.Http.Abstractions/Features 2.2.0, Microsoft.NETCore.Platforms 5.0.0, System.Security.AccessControl 5.0.0 (four pairs) | Legacy transitive compatibility dependencies; migrate owning packages rather than overriding unrelated platform/abstraction majors. |

Package metadata can change without source changes. Re-run the commands at release
time; "deprecated" is not synonymous with "vulnerable", and absence of a NuGet
advisory is not proof that a package is maintained or safe.

## Reproduce locally

Install the declared SDKs/workloads (including Desktop SDK 10.0.201), DevSkim and,
optionally, the CodeQL CLI/C# query pack through trusted distribution channels.
The runner does not install tools or edit project/workflow files. Normal NuGet
restore still requires access to the configured package feeds.

```sh
tools/validate-security.sh --output /absolute/fresh/external-artifacts
# If tool shims are not on PATH:
DEVSKIM=/absolute/path/devskim CODEQL=/absolute/path/codeql \
  tools/validate-security.sh --codeql --output /absolute/fresh/with-codeql
```

Requires Bash, Python 3, Git and .NET. Output must be fresh and outside the
repository; the default is a private temporary directory. `summary.json` records
every exact command, working directory, exit code and artifact path. Keep these
artifacts private: analyzer/compiler/CodeQL artifacts can contain source context.
No source-based rule exclusions are applied.

The script restores/audits all tracked projects, scans a snapshot of **tracked
working-tree files** before compiler caches are created, builds the workflow's
SecurityCodeScan target set with external props, and optionally creates/analyzes
a fresh CodeQL database under a 40-minute create+analyze deadline. Commit new files
before running so they are included in the snapshot; tracked symlinks require
explicit review rather than following them outside the repository. Analyzer props
exist only in the external artifact directory and are imported solely by `-p:`.

Exit codes: **0** means no raw findings/deprecations; **1** means retained raw
findings/deprecations (including justified pattern/quality alerts); **2** means a
missing tool, failed build/scan or timeout. A documented false positive is not
silently filtered to make the runner green. The completed default run executed
**61 commands, zero execution errors**, and exited **1** for the retained findings.
The optional CodeQL commands were separately run successfully as recorded above.

| Check | Before | After |
| --- | --- | --- |
| SecurityCodeScan | 71 | 66 |
| DevSkim, comparable tracked source | 281 | 281 |
| CodeQL C# security-and-quality | 687 | 673 |
| NuGet vulnerable package entries | 1 (AppHost only) | 1 (AppHost only) |
| NuGet distinct deprecated package/version pairs | 48 | 48 |

## Final local verification

All passed on the changed application code with a fresh, isolated synthetic
SpacetimeDB fixture (never a production database):

- `dotnet run --project tests/Api.Contracts`, including the **198-endpoint** reflected
  operation-coverage drift check and new HTTP/log/authorization regressions.
- `dotnet run --project tests/Client.Presentation` and `tests/Client.Infrastructure`.
- `dotnet run --project tests/Architecture.Integration`, then `--catalog-only`,
  `--fleet-only`, `--bootstrap-only`, `--diagnostics-only` with `--no-build`.
- Nonincremental ApiService and Desktop builds; Release WASI production publish
  from `server`; generic MAUI build from its directory with
  `-p:EnableMobileTargets=false`; Release fixture publish before fresh installation.
- `git diff --check`; ShellCheck and Bash syntax validation of the new runner.
- Warning comparison: **189 baseline / 189 current unique path+code+message
  signatures, zero new signatures**, including NuGet/compiler/Avalonia diagnostics.

Local evidence is retained outside the repository under `/tmp/opencode/`:
`h11-security-codeql-{current,after}.sarif` and associated databases/logs,
`h11-security-runner-result/summary.json` (package/DevSkim/SCS artifacts),
`h11-security-full-verification.log`, `h10-security-final-*.log`, and
`h11-security-warning-comparison.log`. These are this workspace's evidence, not
files an owner must already possess to rerun the documented commands.

## Re-enable the three workflows (owner action only)

No `.github/` file or repository setting was changed here.

1. Confirm Actions and code scanning are permitted for this repository/plan. In
   **Settings → Actions → General**, allow the existing third-party/pinned actions
   under the organization's policy and the required SARIF `security-events: write`
   permission. Do not expose secrets to untrusted pull-request code.
2. Open **Actions**, select **SecurityCodeScan**, **DevSkim**, then **CodeQL Advanced**.
   For each disabled workflow, use **Enable workflow** (the banner or the workflow's
   `…` menu). Enabling execution is not the same as approving/reviewing findings.
3. These files have push/PR filters for `main` and schedules, but **no
   `workflow_dispatch` trigger**. Do not expect a Run workflow button: trigger an
   authorized push/PR to the configured branch or wait for the next schedule.
4. Before relying on SecurityCodeScan CI, the owner must reconcile its installed
   SDK list with Desktop's 10.0.201 pin and validate Windows workloads. Also review
   SARIF upload permissions. Local Linux coverage does not certify that Windows job.
5. CodeQL's existing Actions/C#/JavaScript matrix and default build-mode/query
   configuration are broader/different from this manual API/Services C# suite.
   Review those real CI runs separately; do not claim their success from this ledger.

## Remaining release gates

The AppHost KubernetesClient advisory and dependency deprecation migrations remain
open; the major upgrade decision belongs to the owner. Real proxy/TLS/certificate,
Windows/domain/browser/mobile validation, distributed deployment, and the actual
GitHub workflow runs remain unverified. Quality/pattern alerts are retained with
the dispositions above, not represented as zero findings or security certification.
