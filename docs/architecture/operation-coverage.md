# Application operation coverage

## Checkpoint — administrative operation parity

This is a disposition ledger, not a blanket parity claim. Every reflected API
controller method with an HTTP attribute is listed with its verb and combined
controller/action route template. `legacy:` means that endpoint retains its
existing service/projection path; an equivalent v2 query does not make it a
shared-handler adapter. `HTTP-only:` identifies protocol, ceremony, page,
upgrade or diagnostic boundaries rather than inventing a CRUD equivalent.

Tickets, schedules, users, roles and permissions mutations now use the shared
ApplicationOperations boundary through HTTP and resource/generic v1 socket
adapters. IDs are normalized GUIDs; HTTP echoes `Operation-Id`; v1 requires a
stable operation/request ID. Revisions and actor-scoped receipts are enforced
by the module transaction. Legacy read response envelopes remain unchanged.

QR display, approval, device enrollment and self-scan routes below retain their
existing one-time protocol and tested client adapters. They are deliberately
not recast as administrative CRUD operations. See [QR login](qr-login.md).

The Api.Contracts drift test compares the complete endpoint set and checks
every named v2 operation against runtime descriptors. After intentionally
changing routes, regenerate with `dotnet run --project tests/Api.Contracts --
--write-operation-ledger`, review each disposition, and commit the ledger.

| Controller | Method | HTTP | Route | Disposition |
|---|---|---|---|---|
| `BRU_AVTOPARK_AspireAPI.ApiService.Authentication.EntraAuthenticationController` | `Callback` | GET | `/api/auth/entra/callback` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Authentication.EntraAuthenticationController` | `Cancel` | POST | `/api/auth/entra/flows/{flowId}/cancel` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Authentication.EntraAuthenticationController` | `Complete` | POST | `/api/auth/entra/flows/{flowId}/complete` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Authentication.EntraAuthenticationController` | `LinkStatus` | POST | `/api/auth/entra/link/status` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Authentication.EntraAuthenticationController` | `Start` | POST | `/api/auth/entra/start` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Authentication.EntraAuthenticationController` | `Unlink` | POST | `/api/auth/entra/unlink` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Authentication.QrLoginController` | `List` | GET | `/api/auth/qr/devices` | HTTP-only: QR ceremony; full bearer, own-device enrollment/list/revocation |
| `BRU_AVTOPARK_AspireAPI.ApiService.Authentication.QrLoginController` | `Enroll` | POST | `/api/auth/qr/devices` | HTTP-only: QR ceremony; full bearer, own-device enrollment/list/revocation |
| `BRU_AVTOPARK_AspireAPI.ApiService.Authentication.QrLoginController` | `Revoke` | DELETE | `/api/auth/qr/devices/{deviceId}` | HTTP-only: QR ceremony; full bearer, own-device enrollment/list/revocation |
| `BRU_AVTOPARK_AspireAPI.ApiService.Authentication.QrLoginController` | `Create` | POST | `/api/auth/qr/sessions` | HTTP-only: QR ceremony; anonymous QR challenge/PNG and bound-account decoys |
| `BRU_AVTOPARK_AspireAPI.ApiService.Authentication.QrLoginController` | `Approve` | POST | `/api/auth/qr/sessions/{id}/approve` | HTTP-only: QR ceremony; full bearer, active account and verification code |
| `BRU_AVTOPARK_AspireAPI.ApiService.Authentication.QrLoginController` | `Cancel` | POST | `/api/auth/qr/sessions/{id}/cancel` | HTTP-only: QR ceremony; secret-bound one-time QR confirmation/cancellation/denial |
| `BRU_AVTOPARK_AspireAPI.ApiService.Authentication.QrLoginController` | `Complete` | POST | `/api/auth/qr/sessions/{id}/complete` | HTTP-only: QR ceremony; enrolled P-256 proof and primary-factor policy |
| `BRU_AVTOPARK_AspireAPI.ApiService.Authentication.QrLoginController` | `Deny` | POST | `/api/auth/qr/sessions/{id}/deny` | HTTP-only: QR ceremony; secret-bound one-time QR confirmation/cancellation/denial |
| `BRU_AVTOPARK_AspireAPI.ApiService.Authentication.QrLoginController` | `Peek` | POST | `/api/auth/qr/sessions/{id}/peek` | HTTP-only: QR ceremony; secret-bound one-time QR confirmation/cancellation/denial |
| `BRU_AVTOPARK_AspireAPI.ApiService.Authentication.QrLoginController` | `Status` | POST | `/api/auth/qr/sessions/{id}/status` | HTTP-only: QR ceremony; secret-bound exactly-once redemption |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `ChangePassword` | POST | `/api/Auth/change-password` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `ClaimAccountPage` | GET | `/api/Auth/claim-account` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `OAuthCallbackPage` | GET | `/api/Auth/connect/callback` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `OAuthClientList` | GET | `/api/Auth/connect/clients` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `OAuthClientRegister` | POST | `/api/Auth/connect/clients` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `OAuthClientNewPage` | GET | `/api/Auth/connect/clients/new` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `DeleteClientSubmit` | POST | `/api/Auth/connect/clients/{clientId}/delete` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `OAuthClientDelete` | DELETE | `/api/Auth/connect/clients/{id}` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `OAuthClientDetails` | GET | `/api/Auth/connect/clients/{id}` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `OAuthClientUpdate` | PUT | `/api/Auth/connect/clients/{id}` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `OAuthClientEditPage` | GET | `/api/Auth/connect/clients/{id}/edit` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `OAuthClientRegenerateSecret` | POST | `/api/Auth/connect/clients/{id}/regenerate-secret` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `OAuthHelpPage` | GET | `/api/Auth/connect/help` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `OAuthLogsPage` | GET | `/api/Auth/connect/logs` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `RegisterClientSubmit` | POST | `/api/Auth/connect/register-client` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `OAuthScopes` | GET | `/api/Auth/connect/scopes` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `OAuthSettingsPage` | GET | `/api/Auth/connect/settings` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `OAuthTestPage` | GET | `/api/Auth/connect/test` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `OAuthTokensPage` | GET | `/api/Auth/connect/tokens` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `UpdateClientSubmit` | POST | `/api/Auth/connect/update-client/{clientId}` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `ErrorPage` | GET | `/api/Auth/error` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `LoginPage` | GET | `/api/Auth/login` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `Login` | POST | `/api/Auth/login` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `LogoutPage` | GET | `/api/Auth/logout` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `Logout` | POST | `/api/Auth/logout` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `MagicLinkPage` | GET | `/api/Auth/magic-link` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `MagicLinkSend` | POST | `/api/Auth/magic-link/send` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `OAuthAuthorizationsPage` | GET | `/api/Auth/oauth/authorizations` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `OAuthBackchannelAuthorize` | POST | `/api/Auth/oauth/authorize` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `OAuthConsent` | POST | `/api/Auth/oauth/consent` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `OAuthDashboardPage` | GET | `/api/Auth/oauth/dashboard` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `Profile` | GET | `/api/Auth/profile` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `ProfileUpdate` | PUT | `/api/Auth/profile` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `QRLoginPage` | GET | `/api/Auth/qr-login` | HTTP-only: legacy QR HTML page; one-time QR clients use the dedicated ceremony routes |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `QRLoginCancel` | POST | `/api/Auth/qr-login/cancel` | legacy: retired bearer-QR entry returns 410 with one-time QR replacement; feature-disabled remains 503 |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `QRLoginDirect` | POST | `/api/Auth/qr-login/direct` | legacy: retired bearer-QR entry returns 410 with one-time QR replacement; feature-disabled remains 503 |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `QRLoginGenerate` | POST | `/api/Auth/qr-login/generate` | legacy: encrypted bearer-QR compatibility endpoint; separate from the one-time QR protocol |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `QRLoginNotify` | POST | `/api/Auth/qr-login/notify` | legacy: retired bearer-QR entry returns 410 with one-time QR replacement; feature-disabled remains 503 |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `QRLoginStatus` | GET | `/api/Auth/qr-login/status` | legacy: retired bearer-QR entry returns 410 with one-time QR replacement; feature-disabled remains 503 |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `QRLoginValidate` | POST | `/api/Auth/qr-login/validate` | legacy: encrypted bearer-QR compatibility endpoint; separate from the one-time QR protocol |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `RefreshToken` | POST | `/api/Auth/refresh` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `RegisterPage` | GET | `/api/Auth/register` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `Register` | POST | `/api/Auth/register` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `Settings` | GET | `/api/Auth/settings` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `SettingsUpdate` | PUT | `/api/Auth/settings` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `Status` | GET | `/api/Auth/status` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `SuccessPage` | GET | `/api/Auth/success` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `TotpDisable` | POST | `/api/Auth/totp/disable` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `TotpSetup` | GET | `/api/Auth/totp/setup` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `TotpValidate` | POST | `/api/Auth/totp/validate` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `TotpVerify` | POST | `/api/Auth/totp/verify` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `MagicLinkValidate` | POST | `/api/Auth/validate-magic-link` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `WebAuthnCredentials` | GET | `/api/Auth/webauthn/credentials` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `WebAuthnCredentialDelete` | DELETE | `/api/Auth/webauthn/credentials/{id}` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `WebAuthnLoginComplete` | POST | `/api/Auth/webauthn/login/complete` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `WebAuthnLoginOptions` | POST | `/api/Auth/webauthn/login/options` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `WebAuthnRegisterPage` | GET | `/api/Auth/webauthn/register` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `WebAuthnRegisterComplete` | POST | `/api/Auth/webauthn/register/complete` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `WebAuthnRegisterOptions` | POST | `/api/Auth/webauthn/register/options` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `WebAuthnValidate` | POST | `/api/Auth/webauthn/validate` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `AuthWebSocket` | GET | `/api/Auth/ws` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `Authorize` | GET | `/connect/authorize` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `Authorize` | POST | `/connect/authorize` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `AuthorizeCallback` | POST | `/connect/authorize/callback` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `Exchange` | POST | `/connect/token` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `TokenInfo` | GET | `/connect/tokeninfo` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `OAuthUserInfo` | GET | `/connect/userinfo` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController` | `TokenTest` | GET | `/debug/tokentest` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.DebugController` | `DebugTablesPage` | GET | `/api/Debug/tables` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.RealtimeController` | `BroadcastTest` | POST | `/api/Realtime/broadcast-test` | HTTP-only: realtime transport upgrade, event stream or connection diagnostics |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.RealtimeController` | `GetRecentEvents` | GET | `/api/Realtime/events` | HTTP-only: realtime transport upgrade, event stream or connection diagnostics |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.RealtimeController` | `InteractiveWebSocket` | GET | `/api/Realtime/interactive` | HTTP-only: realtime transport upgrade, event stream or connection diagnostics |
| `BRU_AVTOPARK_AspireAPI.ApiService.Controllers.RealtimeController` | `StreamEvents` | GET | `/api/Realtime/stream` | HTTP-only: realtime transport upgrade, event stream or connection diagnostics |
| `TicketSalesApp.AdminServer.Controllers.AdminController` | `GetFeatureFlags` | GET | `/api/admin/feature-flags` | HTTP-only: operator feature configuration, HTML or audit diagnostics |
| `TicketSalesApp.AdminServer.Controllers.AdminController` | `FeatureFlagsUI` | GET | `/api/admin/feature-flags-ui` | HTTP-only: operator feature configuration, HTML or audit diagnostics |
| `TicketSalesApp.AdminServer.Controllers.AdminController` | `GetFeatureFlagAuditLog` | GET | `/api/admin/feature-flags/audit-log` | HTTP-only: operator feature configuration, HTML or audit diagnostics |
| `TicketSalesApp.AdminServer.Controllers.AdminController` | `BulkUpdateFeatureFlags` | POST | `/api/admin/feature-flags/bulk` | HTTP-only: operator feature configuration, HTML or audit diagnostics |
| `TicketSalesApp.AdminServer.Controllers.AdminController` | `ResetFeatureFlags` | POST | `/api/admin/feature-flags/reset` | HTTP-only: operator feature configuration, HTML or audit diagnostics |
| `TicketSalesApp.AdminServer.Controllers.AdminController` | `UpdateFeatureFlag` | PUT | `/api/admin/feature-flags/{flagName}` | HTTP-only: operator feature configuration, HTML or audit diagnostics |
| `TicketSalesApp.AdminServer.Controllers.ApplicationOperationsController` | `Execute` | POST | `/api/operations/{operation}` | HTTP-only: generic shared-operation dispatch transport; registered descriptors are listed below |
| `TicketSalesApp.AdminServer.Controllers.BusesController` | `GetBuses` | GET | `/api/Buses` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.BusesController` | `CreateBus` | POST | `/api/Buses` | adapter→buses.create |
| `TicketSalesApp.AdminServer.Controllers.BusesController` | `StreamRealtimeBusEvents` | GET | `/api/Buses/realtime/ws` | HTTP-only: authenticated v1 WebSocket upgrade; commands delegate to shared handlers |
| `TicketSalesApp.AdminServer.Controllers.BusesController` | `SearchBuses` | GET | `/api/Buses/search` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.BusesController` | `DeleteBus` | DELETE | `/api/Buses/{id}` | adapter→buses.delete |
| `TicketSalesApp.AdminServer.Controllers.BusesController` | `GetBus` | GET | `/api/Buses/{id}` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.BusesController` | `UpdateBus` | PUT | `/api/Buses/{id}` | adapter→buses.update |
| `TicketSalesApp.AdminServer.Controllers.BusesController` | `ActivateBus` | POST | `/api/Buses/{id}/activate` | adapter→buses.set-active |
| `TicketSalesApp.AdminServer.Controllers.BusesController` | `DeactivateBus` | POST | `/api/Buses/{id}/deactivate` | adapter→buses.set-active |
| `TicketSalesApp.AdminServer.Controllers.EmployeesController` | `GetEmployees` | GET | `/api/Employees` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.EmployeesController` | `CreateEmployee` | POST | `/api/Employees` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.EmployeesController` | `GetEmployeesByJob` | GET | `/api/Employees/by-job/{jobId}` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.EmployeesController` | `GetDrivers` | GET | `/api/Employees/drivers` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.EmployeesController` | `StreamRealtimeEvents` | GET | `/api/Employees/realtime/ws` | HTTP-only: realtime transport upgrade, event stream or connection diagnostics |
| `TicketSalesApp.AdminServer.Controllers.EmployeesController` | `DeleteEmployee` | DELETE | `/api/Employees/{id}` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.EmployeesController` | `GetEmployee` | GET | `/api/Employees/{id}` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.EmployeesController` | `UpdateEmployee` | PUT | `/api/Employees/{id}` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.JobsController` | `GetJobs` | GET | `/api/Jobs` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.JobsController` | `CreateJob` | POST | `/api/Jobs` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.JobsController` | `StreamRealtimeEvents` | GET | `/api/Jobs/realtime/ws` | HTTP-only: realtime transport upgrade, event stream or connection diagnostics |
| `TicketSalesApp.AdminServer.Controllers.JobsController` | `SearchJobs` | GET | `/api/Jobs/search` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.JobsController` | `DeleteJob` | DELETE | `/api/Jobs/{id}` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.JobsController` | `GetJob` | GET | `/api/Jobs/{id}` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.JobsController` | `UpdateJob` | PUT | `/api/Jobs/{id}` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.MaintenanceController` | `GetMaintenanceRecords` | GET | `/api/Maintenance` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.MaintenanceController` | `CreateMaintenanceRecord` | POST | `/api/Maintenance` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.MaintenanceController` | `GetDueMaintenanceRecords` | GET | `/api/Maintenance/due-maintenance` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.MaintenanceController` | `StreamRealtimeEvents` | GET | `/api/Maintenance/realtime/ws` | HTTP-only: realtime transport upgrade, event stream or connection diagnostics |
| `TicketSalesApp.AdminServer.Controllers.MaintenanceController` | `SearchMaintenanceRecords` | GET | `/api/Maintenance/search` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.MaintenanceController` | `DeleteMaintenanceRecord` | DELETE | `/api/Maintenance/{id}` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.MaintenanceController` | `GetMaintenanceRecord` | GET | `/api/Maintenance/{id}` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.MaintenanceController` | `UpdateMaintenanceRecord` | PUT | `/api/Maintenance/{id}` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.PermissionsController` | `GetPermissions` | GET | `/api/Permissions` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.PermissionsController` | `CreatePermission` | POST | `/api/Permissions` | adapter→permissions.create |
| `TicketSalesApp.AdminServer.Controllers.PermissionsController` | `GetCategories` | GET | `/api/Permissions/categories` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.PermissionsController` | `GetPermissionsByCategory` | GET | `/api/Permissions/category/{category}` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.PermissionsController` | `StreamRealtimeEvents` | GET | `/api/Permissions/realtime/ws` | HTTP-only: authenticated v1 WebSocket upgrade; commands delegate to shared handlers |
| `TicketSalesApp.AdminServer.Controllers.PermissionsController` | `DeletePermission` | DELETE | `/api/Permissions/{id}` | adapter→permissions.delete |
| `TicketSalesApp.AdminServer.Controllers.PermissionsController` | `GetPermission` | GET | `/api/Permissions/{id}` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.PermissionsController` | `UpdatePermission` | PUT | `/api/Permissions/{id}` | adapter→permissions.update |
| `TicketSalesApp.AdminServer.Controllers.RolesController` | `GetRoles` | GET | `/api/Roles` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.RolesController` | `CreateRole` | POST | `/api/Roles` | adapter→roles.create |
| `TicketSalesApp.AdminServer.Controllers.RolesController` | `StreamRealtimeEvents` | GET | `/api/Roles/realtime/ws` | HTTP-only: authenticated v1 WebSocket upgrade; commands delegate to shared handlers |
| `TicketSalesApp.AdminServer.Controllers.RolesController` | `DeleteRole` | DELETE | `/api/Roles/{id}` | adapter→roles.delete |
| `TicketSalesApp.AdminServer.Controllers.RolesController` | `GetRole` | GET | `/api/Roles/{id}` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.RolesController` | `UpdateRole` | PUT | `/api/Roles/{id}` | adapter→roles.update |
| `TicketSalesApp.AdminServer.Controllers.RolesController` | `GetRolePermissions` | GET | `/api/Roles/{id}/permissions` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.RouteSchedulesController` | `GetRouteSchedules` | GET | `/api/RouteSchedules` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.RouteSchedulesController` | `CreateRouteSchedule` | POST | `/api/RouteSchedules` | adapter→schedules.create |
| `TicketSalesApp.AdminServer.Controllers.RouteSchedulesController` | `StreamRealtimeEvents` | GET | `/api/RouteSchedules/realtime/ws` | HTTP-only: authenticated v1 WebSocket upgrade; commands delegate to shared handlers |
| `TicketSalesApp.AdminServer.Controllers.RouteSchedulesController` | `SearchRouteSchedules` | GET | `/api/RouteSchedules/search` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.RouteSchedulesController` | `DeleteRouteSchedule` | DELETE | `/api/RouteSchedules/{id}` | adapter→schedules.delete |
| `TicketSalesApp.AdminServer.Controllers.RouteSchedulesController` | `GetRouteSchedule` | GET | `/api/RouteSchedules/{id}` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.RouteSchedulesController` | `UpdateRouteSchedule` | PUT | `/api/RouteSchedules/{id}` | adapter→schedules.update; active-only payload adapter→schedules.set-active |
| `TicketSalesApp.AdminServer.Controllers.RoutesController` | `GetRoutes` | GET | `/api/Routes` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.RoutesController` | `CreateRoute` | POST | `/api/Routes` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.RoutesController` | `StreamRealtimeEvents` | GET | `/api/Routes/realtime/ws` | HTTP-only: realtime transport upgrade, event stream or connection diagnostics |
| `TicketSalesApp.AdminServer.Controllers.RoutesController` | `SearchRoutes` | GET | `/api/Routes/search` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.RoutesController` | `DeleteRoute` | DELETE | `/api/Routes/{id}` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.RoutesController` | `GetRoute` | GET | `/api/Routes/{id}` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.RoutesController` | `UpdateRoute` | PUT | `/api/Routes/{id}` | legacy: controller/service endpoint retained; no shared-handler parity claim |
| `TicketSalesApp.AdminServer.Controllers.SessionController` | `Access` | GET | `/api/session/access` | adapter→session.access |
| `TicketSalesApp.AdminServer.Controllers.TicketSalesController` | `GetTicketSales` | GET | `/api/TicketSales` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.TicketSalesController` | `CreateTicketSale` | POST | `/api/TicketSales` | adapter→sales.create |
| `TicketSalesApp.AdminServer.Controllers.TicketSalesController` | `StreamRealtimeEvents` | GET | `/api/TicketSales/realtime/ws` | HTTP-only: authenticated v1 WebSocket upgrade; commands delegate to shared handlers |
| `TicketSalesApp.AdminServer.Controllers.TicketSalesController` | `SearchSales` | GET | `/api/TicketSales/search` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.TicketSalesController` | `GetTotalIncome` | GET | `/api/TicketSales/statistics/income` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.TicketSalesController` | `GetIncomeSummary` | GET | `/api/TicketSales/statistics/income-summary` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.TicketSalesController` | `GetTopTransports` | GET | `/api/TicketSales/statistics/top-transports` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.TicketSalesController` | `DeleteTicketSale` | DELETE | `/api/TicketSales/{id}` | adapter→sales.delete |
| `TicketSalesApp.AdminServer.Controllers.TicketSalesController` | `GetTicketSale` | GET | `/api/TicketSales/{id}` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.TicketSalesController` | `UpdateTicketSale` | PUT | `/api/TicketSales/{id}` | adapter→sales.update |
| `TicketSalesApp.AdminServer.Controllers.TicketsController` | `GetTickets` | GET | `/api/Tickets` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.TicketsController` | `CreateTicket` | POST | `/api/Tickets` | adapter→tickets.create |
| `TicketSalesApp.AdminServer.Controllers.TicketsController` | `GetAvailableTickets` | GET | `/api/Tickets/available` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.TicketsController` | `StreamRealtimeEvents` | GET | `/api/Tickets/realtime/ws` | HTTP-only: authenticated v1 WebSocket upgrade; commands delegate to shared handlers |
| `TicketSalesApp.AdminServer.Controllers.TicketsController` | `GetTicketsByRoute` | GET | `/api/Tickets/route/{routeId}` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.TicketsController` | `SearchTickets` | GET | `/api/Tickets/search` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.TicketsController` | `DeleteTicket` | DELETE | `/api/Tickets/{id}` | adapter→tickets.delete |
| `TicketSalesApp.AdminServer.Controllers.TicketsController` | `GetTicket` | GET | `/api/Tickets/{id}` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.TicketsController` | `UpdateTicket` | PUT | `/api/Tickets/{id}` | adapter→tickets.update |
| `TicketSalesApp.AdminServer.Controllers.UsersController` | `GetUsers` | GET | `/api/Users` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.UsersController` | `CreateUser` | POST | `/api/Users` | adapter→users.create |
| `TicketSalesApp.AdminServer.Controllers.UsersController` | `GetCurrentUser` | GET | `/api/Users/current` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.UsersController` | `StreamRealtimeEvents` | GET | `/api/Users/realtime/ws` | HTTP-only: authenticated v1 WebSocket upgrade; commands delegate to shared handlers |
| `TicketSalesApp.AdminServer.Controllers.UsersController` | `DeleteUser` | DELETE | `/api/Users/{id}` | adapter→users.delete |
| `TicketSalesApp.AdminServer.Controllers.UsersController` | `GetUser` | GET | `/api/Users/{id}` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.UsersController` | `UpdateUser` | PUT | `/api/Users/{id}` | adapter→users.update; active-only payload adapter→users.set-active |
| `TicketSalesApp.AdminServer.Controllers.UsersController` | `GetUserPermissions` | GET | `/api/Users/{id}/permissions` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.UsersController` | `GetUserRoles` | GET | `/api/Users/{id}/roles` | legacy: service-backed HTTP read/projection retained; new paged v2 query is a separate contract |
| `TicketSalesApp.AdminServer.Controllers.UsersController` | `AssignRoleToUser` | POST | `/api/Users/{id}/roles` | adapter→users.assign-role |
| `TicketSalesApp.AdminServer.Controllers.UsersController` | `RemoveRoleFromUser` | DELETE | `/api/Users/{id}/roles/{roleId}` | adapter→users.remove-role |
| `TicketSalesApp.AdminServer.Controllers.WindowsAuthenticationController` | `Login` | POST | `/api/auth/windows` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `TicketSalesApp.AdminServer.Controllers.WindowsAuthenticationController` | `Link` | POST | `/api/auth/windows/link` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |
| `TicketSalesApp.AdminServer.Controllers.WindowsAuthenticationController` | `LinkStatus` | POST | `/api/auth/windows/link/status` | HTTP-only: authentication/provider/OAuth-OIDC or account-security ceremony; preserve protocol/HTML response |

## Registered shared v2 operations

These descriptors are shared by the generic HTTP operation transport and v2 socket.
Resource-specific HTTP reads marked legacy above are not falsely claimed as adapters.

| Operation | Permission | Mutation |
|---|---|---|
| v2 buses.create | `buses.create` | true |
| v2 buses.delete | `buses.delete` | true |
| v2 buses.list | `buses.view` | false |
| v2 buses.set-active | `buses.edit` | true |
| v2 buses.update | `buses.edit` | true |
| v2 employees.create | `employees.create` | true |
| v2 employees.delete | `employees.delete` | true |
| v2 employees.list | `employees.view` | false |
| v2 employees.update | `employees.edit` | true |
| v2 jobs.create | `jobs.create` | true |
| v2 jobs.delete | `jobs.delete` | true |
| v2 jobs.get | `jobs.view` | false |
| v2 jobs.list | `jobs.view` | false |
| v2 jobs.update | `jobs.edit` | true |
| v2 maintenance.create | `maintenance.create` | true |
| v2 maintenance.delete | `maintenance.delete` | true |
| v2 maintenance.list | `maintenance.view` | false |
| v2 maintenance.update | `maintenance.edit` | true |
| v2 mutations.receipt | `authenticated` | false |
| v2 permissions.create | `permissions.create` | true |
| v2 permissions.delete | `permissions.delete` | true |
| v2 permissions.list | `permissions.view` | false |
| v2 permissions.update | `permissions.edit` | true |
| v2 reports.income | `sales.view` | false |
| v2 reports.sales | `sales.view` | false |
| v2 roles.create | `roles.create` | true |
| v2 roles.delete | `roles.delete` | true |
| v2 roles.grant-permission | `grant_permissions` | true |
| v2 roles.list | `roles.view` | false |
| v2 roles.revoke-permission | `grant_permissions` | true |
| v2 roles.update | `roles.edit` | true |
| v2 routes.create | `routes.create` | true |
| v2 routes.delete | `routes.delete` | true |
| v2 routes.list | `routes.view` | false |
| v2 routes.set-active | `routes.edit` | true |
| v2 routes.update | `routes.edit` | true |
| v2 sales.create | `sales.create` | true |
| v2 sales.delete | `sales.delete` | true |
| v2 sales.list | `sales.view` | false |
| v2 sales.update | `sales.edit` | true |
| v2 schedules.create | `schedules.create` | true |
| v2 schedules.delete | `schedules.delete` | true |
| v2 schedules.list | `schedules.view` | false |
| v2 schedules.set-active | `schedules.edit` | true |
| v2 schedules.update | `schedules.edit` | true |
| v2 session.access | `authenticated` | false |
| v2 tickets.create | `tickets.create` | true |
| v2 tickets.delete | `tickets.delete` | true |
| v2 tickets.list | `tickets.view` | false |
| v2 tickets.update | `tickets.edit` | true |
| v2 users.assign-role | `assign_roles` | true |
| v2 users.create | `users.create` | true |
| v2 users.delete | `users.delete` | true |
| v2 users.get | `users.view` | false |
| v2 users.list | `users.view` | false |
| v2 users.remove-role | `assign_roles` | true |
| v2 users.set-active | `users.edit` | true |
| v2 users.update | `users.edit` | true |

## Remaining boundaries

Provider ceremonies, OAuth/OIDC and account-security endpoints remain protocol-specific.
Legacy controller reads and older fleet/catalog service adapters remain explicit in this ledger;
there is no unresolved parity-verification placeholder. Native screens are a later handoff.
See [realtime v2](realtime-v2.md) and [migration and validation](migration-and-validation.md).
