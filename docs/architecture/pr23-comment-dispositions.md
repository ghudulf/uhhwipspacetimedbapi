# PR #23 comment dispositions (work ledger)

Original review: https://github.com/ghudulf/uhhwipspacetimedbapi/pull/23. This ledger does not resolve remote threads or claim all feedback is addressed. Source indexes refer to the inspected review-thread export.

| Thread index | File | Disposition |
|---|---|---|
| 0 | AuthControllerRefactored.cs | Original refactored controller file is absent in current head; active login delegates to orchestration. Current security workflow rerun outstanding. |
| 1 | AuthControllerRefactored.cs | Original refactored controller file is absent in current head; active login delegates to orchestration. Current security workflow rerun outstanding. |
| 2 | HttpContextAuthWebSocketTokenValidator.cs | Removed exception messages/raw exception objects from token-validation rejection logs; structured failure types remain. Security workflow rerun outstanding. |
| 3 | appsettings.json | Already corrected in starting PR head. |
| 4 | Program.cs | Wildcard removed in starting PR head; configured origins retained. |
| 5 | WebSocketDebugViewModel.cs | Trailing API segment normalization fixed in client. |
| 6 | WebSocketDebugViewModel.cs | Debug and production client now share endpoint validation: HTTPS for remote hosts; HTTP only for loopback development. Endpoint tests selected. |
| 7 | Program.cs | See configured origin handling; wildcard absent. |
| 8 | appsettings.json | Already corrected in starting PR head. |
| 9 | WebSocketDebugViewModel.cs | Debug and production client share the same secure endpoint validation; no silent HTTP LAN downgrade. |
| 10 | appsettings.json | Already corrected in starting PR head. |
| 13 | AuthWebSocketService.cs | String extraction validates JSON kinds in auth socket. |
| 17 | Register.cshtml | Corrected Driver/Conductor/Dispatcher numeric values and added the existing Cashier role choice; form styling/layout retained. |
| 18 | Register.cshtml | Page/style change deferred to preserve requested existing page code and appearance. |
| 20 | Program.cs | Wildcard absent; configured origins retained. |
| 21 | MauiAuthService.cs | Made the singleton constructor private; MAUI DI resolves MauiAuthService.Instance. |
| 22 | MauiAuthService.cs | Removed unused embedded client secret; public-client PKCE retained. |
| 33 | MainWindow.axaml.cs | Shared ClientEndpoints.ServerRoot handles HTTPS, proxy prefixes and exact API path segments; desktop and MAUI callers reuse it. Endpoint tests pass. |
| 34 | RouteManagementToolWindow.axaml | Count-based empty state fixed. |
| 35 | SplashScreen.axaml.cs | Already corrected in the starting PR head: non-success probes continue to later endpoints. Responses now dispose promptly. |
| 36 | appsettings.json | Wildcard absent; configured origins retained. |
| 37 | IAuthWebSocketService.cs | Removed the unused concurrent collections import. |
| 38 | RoutesPage.xaml.cs | MAUI overlay follows actual route activation and IsBusy/HasError; retry invokes the real load command. Removed the fixed readiness timer; native host validation remains outstanding. |
| 39 | OAuthLoginControl.axaml.cs | Callback diagnostics strip query/userinfo/fragment and state values. |
| 42 | RouteManagementViewModel.cs | Dialog capability properties are now instance properties for normal data-context bindings. |
| 43 | RouteManagementViewModel.cs | Route requests now use relative URIs against their authenticated client. Discovery completes before activation; account changes recreate the client and clear prior data. |
| 45 | WebSocketDebugViewModel.cs | Auth debugger permits anonymous handshake and attaches bearer proof only when supplied. |
| 46 | appsettings.json | Configured UPnP behavior retained per plan; ownership/lease/cleanup corrected. Default enablement recommendation requires reconciliation with deployment configuration. |
| 49 | ClaimAccount.cshtml | Outstanding: inspect current code and validate before disposition. |
| 54 | UPnPService.cs | Configured UPnP behavior retained per plan; ownership/lease/cleanup corrected. Default enablement recommendation requires reconciliation with deployment configuration. |
| 55 | UPnPService.cs | Lease duration, renewal and owned-mapping cleanup implemented. |
| 56 | ApiClientService.cs | Bearer token previews removed. |
| 61 | tasks.md | Current source has one authoritative Phase 7–11 task section; no repeated task block remains. Timeline summaries are retained. |
| 62 | Logout.cshtml | Page/style change deferred to preserve requested existing page code and appearance. |
| 63 | Register.cshtml | Added an explicit POST method so a native fallback cannot put passwords in the query string. |
| 66 | OpenIddictSpacetimeScope.g.cs | Bindings regenerated from production WASM with pinned 2.1.0. |
| 72 | securitycodescan.yml | Installed the declared SDK 8/10/11 RC2 set and split SecurityCodeScan builds by project working directory, including module and MAUI workloads; Windows workflow execution outstanding. |
| 75 | tasks.md | Documentation now uses the actual HTML route /api/admin/feature-flags-ui; JSON management routes retain /api/admin/feature-flags. |
| 76 | tasks.md | Auth action gates now consult runtime IFeatureFlagService; committed overrides replace optimistic static caching. HTTP 503 shape retained; v2 password/TOTP gates use the same service. |
| 77 | bru-design-system.css | Page/style change deferred to preserve requested existing page code and appearance. |
| 78 | AuthWebSocketService.cs | Refresh validates replacement token and replaces connection claims; session transitions cancel captured QR pollers. |
| 80 | ClientDetails.cshtml | Page/style change deferred to preserve requested existing page code and appearance. |
| 81 | ClientsList.cshtml | Page/style change deferred to preserve requested existing page code and appearance. |
| 82 | CashierDay.g.cs | Bindings regenerated with pinned 2.1.0. |
| 84 | OpenIdConnect.g.cs | Bindings regenerated with pinned 2.1.0. |
| 85 | PassengerCount.g.cs | Bindings regenerated with pinned 2.1.0. |
| 87 | Sale.g.cs | Generated compatibility preserved; sale metadata exposed through explicit projection. |
| 88 | SeatConfiguration.g.cs | Bindings regenerated with pinned 2.1.0. |
| 90 | OAuthLoginPage.xaml.cs | MAUI OAuth page reads the callback from the OAuthService used to construct the request; desktop/MAUI creation shares the existing registration constant. |
| 91 | WebSocketDebugViewModel.cs | Trailing API segment normalization fixed in client. |
| 92 | OAuthLoginWindow.axaml.cs | Preserved configured authorization/callback URIs and removed scheme downgrades; callback configuration is shared. Actual OAuth provider roundtrip validation remains outstanding. |
| 93 | design.md | Added explicit EndpointEnabled availability flags; legacy refactoring selectors remain readable and do not disable endpoints by default. An opt-in compatibility mode preserves intentional old kill switches; documented migration/rollback and HTTP/v2 checks cover precedence. |
| 95 | header-scroll.js | Page/style change deferred to preserve requested existing page code and appearance. |
| 96 | HttpContextAuthWebSocketTokenValidator.cs | Validator propagates OperationCanceledException. |
| 97 | HttpContextAuthWebSocketTokenValidator.cs | Request-host HTTP token validation replaced with in-process OpenIddict validation. |
| 98 | Register.cshtml | Page/style change deferred to preserve requested existing page code and appearance. |
| 99 | ControllerLoggingMiddleware.cs | Routing breadcrumb includes both controller names. |
