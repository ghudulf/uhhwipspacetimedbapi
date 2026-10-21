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

## Remaining checkpoints

CodeQL, package triage, reproducible runner and final validation results are being
recorded in subsequent tool-specific steps. This is not yet the full audit signoff.
