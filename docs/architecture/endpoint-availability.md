# Endpoint availability and legacy refactoring flags

`FeatureFlags:*EndpointEnabled` controls HTTP and v2 application endpoint availability. For example, `LoginEndpointEnabled=false` deliberately disables password login; `TotpValidateEndpointEnabled=false` disables TOTP validation. Omitted availability settings default to enabled. Runtime canonical overrides use the existing authorized, committed feature-flag service.

The older `Enable*Refactoring` keys are retained in configuration and the flag-management API, but no longer disable endpoints by default. Historically false selected the legacy implementation; it did not mean “turn off login.” The legacy controller was already removed before this work. Both selector values now use the shared implementation. The existing admin page displays actual availability keys without changing its styles/layout. Its previously misspelled/nonexistent selector names have been corrected.

## Compatibility migration

If an installation intentionally used the PR #23 interpretation of legacy keys as kill switches, set `FeatureFlags:LegacyAvailabilityCompatibility=true` during the coordinated rollout. Availability precedence is:

1. Committed `*EndpointEnabled` database override.
2. Explicit `*EndpointEnabled` configuration value.
3. Only in compatibility mode: legacy-key database override, then legacy configuration.
4. Enabled by default.

Copy each intended availability policy to its explicit canonical name, verify HTTP/v2 behavior, then turn compatibility mode off. Canonical database entries are additive; no legacy overrides are deleted. The compatibility setting is deployment configuration, not an administrator-toggleable runtime flag. Configured new values are nullable internally so an omitted value can be distinguished from an explicit true/false during this migration.

For rollback to an older binary that only understands `Enable*Refactoring`, retain/update the corresponding legacy values before rollback. New availability records do not affect old binaries. This transport/configuration rollback does not revert database integrity fixes.

Availability reads still fail closed on database unavailability. Permission checks and reducer authorization remain independent of these flags. Do not use feature flags to grant roles or trust to an API identity.
