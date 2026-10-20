# Application operation coverage

Baseline: PR #23 head `e35e483d6a14a1c855ee5aefcc65a69e8bfcdeb4`. This inventory is a work ledger; a listed endpoint is not a claim of v2 support. Existing HTTP, resource sockets and auth sockets stay available.

| Controller | Baseline line | HTTP attribute | Coverage |
|---|---:|---|---|
| AdminController | 37 | `[HttpGet("feature-flags-ui")]` | Pending parity verification |
| AdminController | 54 | `[HttpGet("feature-flags")]` | Pending parity verification |
| AdminController | 95 | `[HttpPut("feature-flags/{flagName}")]` | Pending parity verification |
| AdminController | 171 | `[HttpPost("feature-flags/bulk")]` | Pending parity verification |
| AdminController | 255 | `[HttpPost("feature-flags/reset")]` | Pending parity verification |
| AdminController | 306 | `[HttpGet("feature-flags/audit-log")]` | Pending parity verification |
| AuthController | 93 | `[HttpGet("login")]` | Pending parity verification |
| AuthController | 137 | `[HttpPost("login")]` | Pending parity verification |
| AuthController | 252 | `[HttpGet("register")]` | Pending parity verification |
| AuthController | 296 | `[HttpPost("register")]` | Pending parity verification |
| AuthController | 390 | `[HttpGet("totp/setup")]` | Pending parity verification |
| AuthController | 452 | `[HttpPost("totp/verify")]` | Pending parity verification |
| AuthController | 510 | `[HttpPost("totp/disable")]` | Pending parity verification |
| AuthController | 567 | `[HttpPost("totp/validate")]` | Pending parity verification |
| AuthController | 664 | `[HttpPost("webauthn/register/options")]` | Pending parity verification |
| AuthController | 721 | `[HttpPost("webauthn/register/complete")]` | Pending parity verification |
| AuthController | 780 | `[HttpPost("webauthn/login/options")]` | Pending parity verification |
| AuthController | 825 | `[HttpPost("webauthn/login/complete")]` | Pending parity verification |
| AuthController | 909 | `[HttpPost("webauthn/validate")]` | Pending parity verification |
| AuthController | 1002 | `[HttpGet("webauthn/credentials")]` | Pending parity verification |
| AuthController | 1062 | `[HttpDelete("webauthn/credentials/{id}")]` | Pending parity verification |
| AuthController | 1123 | `[HttpPost("magic-link/send")]` | Pending parity verification |
| AuthController | 1179 | `[HttpPost("validate-magic-link")]` | Pending parity verification |
| AuthController | 1272 | `[HttpGet("magic-link")]` | Pending parity verification |
| AuthController | 1305 | `[HttpGet("qr-login")]` | Pending parity verification |
| AuthController | 1335 | `[HttpPost("qr-login/generate")]` | Pending parity verification |
| AuthController | 1398 | `[HttpPost("qr-login/validate")]` | Pending parity verification |
| AuthController | 1465 | `[HttpPost("qr-login/direct")]` | Pending parity verification |
| AuthController | 1525 | `[HttpGet("qr-login/status")]` | Pending parity verification |
| AuthController | 1583 | `[HttpPost("qr-login/cancel")]` | Pending parity verification |
| AuthController | 1636 | `[HttpPost("qr-login/notify")]` | Pending parity verification |
| AuthController | 1703 | `[HttpGet("~/connect/authorize")]` | Pending parity verification |
| AuthController | 1704 | `[HttpPost("~/connect/authorize")]` | Pending parity verification |
| AuthController | 1877 | `[HttpPost("~/connect/token")]` | Pending parity verification |
| AuthController | 2078 | `[HttpGet("~/connect/userinfo")]` | Pending parity verification |
| AuthController | 2141 | `[HttpPost("oauth/authorize")]` | Pending parity verification |
| AuthController | 2334 | `[HttpPost("connect/clients")]` | Pending parity verification |
| AuthController | 2400 | `[HttpGet("connect/clients")]` | Pending parity verification |
| AuthController | 2467 | `[HttpGet("connect/clients/{id}")]` | Pending parity verification |
| AuthController | 2546 | `[HttpPut("connect/clients/{id}")]` | Pending parity verification |
| AuthController | 2607 | `[HttpDelete("connect/clients/{id}")]` | Pending parity verification |
| AuthController | 2648 | `[HttpGet("connect/scopes")]` | Pending parity verification |
| AuthController | 2690 | `[HttpPost("connect/clients/{id}/regenerate-secret")]` | Pending parity verification |
| AuthController | 2744 | `[HttpGet("connect/clients/new")]` | Pending parity verification |
| AuthController | 2776 | `[HttpGet("connect/clients/{id}/edit")]` | Pending parity verification |
| AuthController | 2835 | `[HttpGet("connect/scopes")]` | Pending parity verification |
| AuthController | 2887 | `[HttpGet("oauth/authorizations")]` | Pending parity verification |
| AuthController | 2920 | `[HttpGet("connect/tokens")]` | Pending parity verification |
| AuthController | 2953 | `[HttpGet("oauth/dashboard")]` | Pending parity verification |
| AuthController | 2986 | `[HttpGet("connect/settings")]` | Pending parity verification |
| AuthController | 3019 | `[HttpGet("connect/logs")]` | Pending parity verification |
| AuthController | 3052 | `[HttpGet("connect/help")]` | Pending parity verification |
| AuthController | 3085 | `[HttpGet("connect/test")]` | Pending parity verification |
| AuthController | 3118 | `[HttpGet("connect/callback")]` | Pending parity verification |
| AuthController | 3158 | `[HttpGet("profile")]` | Pending parity verification |
| AuthController | 3286 | `[HttpPut("profile")]` | Pending parity verification |
| AuthController | 3352 | `[HttpPost("change-password")]` | Pending parity verification |
| AuthController | 3413 | `[HttpGet("logout")]` | Pending parity verification |
| AuthController | 3447 | `[HttpPost("logout")]` | Pending parity verification |
| AuthController | 3500 | `[HttpPost("refresh")]` | Pending parity verification |
| AuthController | 3555 | `[HttpGet("settings")]` | Pending parity verification |
| AuthController | 3614 | `[HttpPut("settings")]` | Pending parity verification |
| AuthController | 3680 | `[HttpGet("status")]` | Pending parity verification |
| AuthController | 3740 | `[HttpGet("success")]` | Pending parity verification |
| AuthController | 3775 | `[HttpGet("error")]` | Pending parity verification |
| AuthController | 3811 | `[HttpGet("claim-account")]` | Pending parity verification |
| AuthController | 3845 | `[HttpGet("webauthn/register")]` | Pending parity verification |
| AuthController | 3898 | `[HttpGet("~/debug/tokentest")]` | Pending parity verification |
| AuthController | 3982 | `[HttpGet("~/connect/tokeninfo")]` | Pending parity verification |
| AuthController | 4061 | `[HttpPost("~/connect/authorize/callback")]` | Pending parity verification |
| AuthController | 4228 | `[HttpPost("connect/register-client")]` | Pending parity verification |
| AuthController | 4367 | `[HttpPost("connect/update-client/{clientId}")]` | Pending parity verification |
| AuthController | 4506 | `[HttpPost("connect/clients/{clientId}/delete")]` | Pending parity verification |
| AuthController | 4634 | `[HttpGet("ws")]` | Pending parity verification |
| AuthController | 4685 | `[HttpPost("oauth/consent")]` | Pending parity verification |
| BusesController | 58 | `[HttpGet("realtime/ws")]` | Pending parity verification |
| BusesController | 386 | `[HttpGet]` | Pending parity verification |
| BusesController | 424 | `[HttpGet("{id}")]` | Pending parity verification |
| BusesController | 470 | `[HttpPost]` | Pending parity verification |
| BusesController | 538 | `[HttpPut("{id}")]` | Pending parity verification |
| BusesController | 596 | `[HttpDelete("{id}")]` | Pending parity verification |
| BusesController | 659 | `[HttpGet("search")]` | Pending parity verification |
| BusesController | 713 | `[HttpPost("{id}/activate")]` | Pending parity verification |
| BusesController | 783 | `[HttpPost("{id}/deactivate")]` | Pending parity verification |
| DebugController | 38 | `[HttpGet("tables")]` | Pending parity verification |
| EmployeesController | 56 | `[HttpGet("realtime/ws")]` | Pending parity verification |
| EmployeesController | 406 | `[HttpGet]` | Pending parity verification |
| EmployeesController | 457 | `[HttpGet("{id}")]` | Pending parity verification |
| EmployeesController | 511 | `[HttpPost]` | Pending parity verification |
| EmployeesController | 578 | `[HttpPut("{id}")]` | Pending parity verification |
| EmployeesController | 657 | `[HttpDelete("{id}")]` | Pending parity verification |
| EmployeesController | 717 | `[HttpGet("by-job/{jobId}")]` | Pending parity verification |
| EmployeesController | 764 | `[HttpGet("drivers")]` | Pending parity verification |
| JobsController | 52 | `[HttpGet("realtime/ws")]` | Pending parity verification |
| JobsController | 311 | `[HttpGet]` | Pending parity verification |
| JobsController | 370 | `[HttpGet("{id}")]` | Pending parity verification |
| JobsController | 413 | `[HttpPost]` | Pending parity verification |
| JobsController | 461 | `[HttpPut("{id}")]` | Pending parity verification |
| JobsController | 499 | `[HttpDelete("{id}")]` | Pending parity verification |
| JobsController | 534 | `[HttpGet("search")]` | Pending parity verification |
| MaintenanceController | 59 | `[HttpGet("realtime/ws")]` | Pending parity verification |
| MaintenanceController | 361 | `[HttpGet]` | Pending parity verification |
| MaintenanceController | 397 | `[HttpGet("{id}")]` | Pending parity verification |
| MaintenanceController | 431 | `[HttpPost]` | Pending parity verification |
| MaintenanceController | 472 | `[HttpPut("{id}")]` | Pending parity verification |
| MaintenanceController | 503 | `[HttpDelete("{id}")]` | Pending parity verification |
| MaintenanceController | 525 | `[HttpGet("search")]` | Pending parity verification |
| MaintenanceController | 589 | `[HttpGet("due-maintenance")]` | Pending parity verification |
| PermissionsController | 56 | `[HttpGet("realtime/ws")]` | Pending parity verification |
| PermissionsController | 352 | `[HttpGet]` | Pending parity verification |
| PermissionsController | 385 | `[HttpGet("{id}")]` | Pending parity verification |
| PermissionsController | 423 | `[HttpPost]` | Pending parity verification |
| PermissionsController | 475 | `[HttpPut("{id}")]` | Pending parity verification |
| PermissionsController | 536 | `[HttpDelete("{id}")]` | Pending parity verification |
| PermissionsController | 598 | `[HttpGet("categories")]` | Pending parity verification |
| PermissionsController | 621 | `[HttpGet("category/{category}")]` | Pending parity verification |
| RealtimeController | 65 | `[HttpGet("events")]` | Pending parity verification |
| RealtimeController | 89 | `[HttpPost("broadcast-test")]` | Pending parity verification |
| RealtimeController | 132 | `[HttpGet("interactive")]` | Pending parity verification |
| RealtimeController | 633 | `[HttpGet("stream")]` | Pending parity verification |
| RolesController | 60 | `[HttpGet("realtime/ws")]` | Pending parity verification |
| RolesController | 324 | `[HttpGet]` | Pending parity verification |
| RolesController | 359 | `[HttpGet("{id}")]` | Pending parity verification |
| RolesController | 400 | `[HttpGet("{id}/permissions")]` | Pending parity verification |
| RolesController | 433 | `[HttpPost]` | Pending parity verification |
| RolesController | 487 | `[HttpPut("{id}")]` | Pending parity verification |
| RolesController | 555 | `[HttpDelete("{id}")]` | Pending parity verification |
| RouteSchedulesController | 55 | `[HttpGet("realtime/ws")]` | Pending parity verification |
| RouteSchedulesController | 486 | `[HttpGet]` | Pending parity verification |
| RouteSchedulesController | 548 | `[HttpGet("{id}")]` | Pending parity verification |
| RouteSchedulesController | 592 | `[HttpGet("search")]` | Pending parity verification |
| RouteSchedulesController | 883 | `[HttpPost]` | Pending parity verification |
| RouteSchedulesController | 939 | `[HttpPut("{id}")]` | Pending parity verification |
| RouteSchedulesController | 987 | `[HttpDelete("{id}")]` | Pending parity verification |
| RoutesController | 55 | `[HttpGet("realtime/ws")]` | Pending parity verification |
| RoutesController | 179 | `[HttpGet]` | Pending parity verification |
| RoutesController | 208 | `[HttpGet("{id}")]` | Pending parity verification |
| RoutesController | 242 | `[HttpPost]` | Pending parity verification |
| RoutesController | 282 | `[HttpPut("{id}")]` | Pending parity verification |
| RoutesController | 312 | `[HttpDelete("{id}")]` | Pending parity verification |
| RoutesController | 334 | `[HttpGet("search")]` | Pending parity verification |
| TicketSalesController | 69 | `[HttpGet("realtime/ws")]` | Pending parity verification |
| TicketSalesController | 284 | `[HttpGet]` | Pending parity verification |
| TicketSalesController | 336 | `[HttpGet("{id}")]` | Pending parity verification |
| TicketSalesController | 402 | `[HttpPost]` | Pending parity verification |
| TicketSalesController | 546 | `[HttpPut("{id}")]` | Pending parity verification |
| TicketSalesController | 594 | `[HttpDelete("{id}")]` | Pending parity verification |
| TicketSalesController | 642 | `[HttpGet("statistics/income")]` | Pending parity verification |
| TicketSalesController | 660 | `[HttpGet("statistics/top-transports")]` | Pending parity verification |
| TicketSalesController | 678 | `[HttpGet("search")]` | Pending parity verification |
| TicketsController | 62 | `[HttpGet("realtime/ws")]` | Pending parity verification |
| TicketsController | 385 | `[HttpGet]` | Pending parity verification |
| TicketsController | 429 | `[HttpGet("{id}")]` | Pending parity verification |
| TicketsController | 479 | `[HttpPost]` | Pending parity verification |
| TicketsController | 587 | `[HttpPut("{id}")]` | Pending parity verification |
| TicketsController | 687 | `[HttpDelete("{id}")]` | Pending parity verification |
| TicketsController | 758 | `[HttpGet("search")]` | Pending parity verification |
| TicketsController | 840 | `[HttpGet("route/{routeId}")]` | Pending parity verification |
| TicketsController | 895 | `[HttpGet("available")]` | Pending parity verification |
| UsersController | 58 | `[HttpGet("realtime/ws")]` | Pending parity verification |
| UsersController | 399 | `[HttpGet]` | Pending parity verification |
| UsersController | 417 | `[HttpGet("{id}")]` | Pending parity verification |
| UsersController | 468 | `[HttpPost]` | Pending parity verification |
| UsersController | 509 | `[HttpPut("{id}")]` | Pending parity verification |
| UsersController | 560 | `[HttpDelete("{id}")]` | Pending parity verification |
| UsersController | 606 | `[HttpGet("{id}/roles")]` | Pending parity verification |
| UsersController | 642 | `[HttpGet("{id}/permissions")]` | Pending parity verification |
| UsersController | 676 | `[HttpPost("{id}/roles")]` | Pending parity verification |
| UsersController | 698 | `[HttpDelete("{id}/roles/{roleId}")]` | Pending parity verification |
| UsersController | 720 | `[HttpGet("current")]` | Pending parity verification |

## V2 rollout checkpoint

See [protocol and client rollout](realtime-v2.md). `session.access`, paged `sales.list`, sale create/update/delete, actor-scoped receipt lookup, monthly income and date-range sales report projections are registered. Existing session-access and sale-mutation adapters share these application handlers. Password login, TOTP, token attach, socket logout, cancellation and capability/heartbeat operations are available. Their boundary tests pass; remaining rows above still require full parity verification. Avalonia access polling and both reporting screens use the shared transport. Other screen callers, provider ceremonies and unlisted mutation handlers remain outstanding. Query subscriptions are described in the protocol document.

## Fleet checkpoint

`buses.list`, `buses.create`, `buses.update`, `buses.delete`, and `buses.set-active` now have explicit shared contracts/handlers. Existing HTTP/resource-socket mutation adapters use those handlers, including effective actor, optional HTTP idempotency key, and optional update revision. The paged bus screen uses the shared transport for reads and mutations, including all previously ignored edit fields. Legacy unpaged reads remain compatible service adapters; v2 bus subscriptions now use committed query invalidations with snapshot watermarks and gap recovery. Fleet HTTP/WebSocket mutation parity, validation/permission rejection, actual reducer receipts/replay/revisions/reference protection and headless row/selection/lifecycle checks are covered. Other inventory rows retain their pending status.

## Catalog and subscription checkpoint

- Routes, employees, jobs and maintenance: typed paged v2 queries; create/update/delete receipts and revisions; route activation; shared committed services for existing HTTP/resource sockets. HTTP/v2 mutation parity covers the registered handlers and actor/operation-ID propagation. Existing resource-specific search/read projections still require full parity mapping before their inventory rows can be marked complete.
- V2 subscriptions: explicit query allowlist; pre-snapshot watermark; bounded committed-change journal; overflow/epoch recovery; idle permission rechecks; generation cleanup. API/shared-client tests cover a change during snapshot acquisition, anonymous denial, mutation-subscription rejection, overflow, reconnect and revocation. The bus screen consumes the shared stream and gates writes during resynchronization.
- Remaining catalog view models still require shared transport and lifecycle migration. Generic protocol support is not evidence that a screen uses it.
- GitHub security workflows are `disabled_inactivity`. Re-enable attempts were rejected with HTTP 403 by the repository integration. Branch security validation remains blocked until someone with Actions permission enables them.

### Jobs client and initial provisioning checkpoint

`jobs.get` provides an explicitly authorized current-row/revision read for conflict review. Jobs list/create/update/delete now use the shared native transport and lifecycle; existing styles and grid layout are retained with synchronization/paging feedback. Routes/employees/maintenance DTOs include permission-filtered related display data; their remaining native view-model migrations are still outstanding.

Owner-only initial provisioning is a deployment operation, not an anonymous application socket operation. The API generates its own database connection identity and may use explicitly configured owner credentials to provision that identity and the initial admin password. Normal password authentication confirms the checked reducer's committed result.
