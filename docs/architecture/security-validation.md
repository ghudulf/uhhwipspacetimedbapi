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

## Remaining checkpoints

DevSkim, CodeQL, package triage, reproducible runner and final validation results are
being recorded in subsequent tool-specific steps. Do not interpret this checkpoint
as completion of the full local security audit.
