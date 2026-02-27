using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using TicketSalesApp.Services.Interfaces;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DebugController : ControllerBase
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly ILogger<DebugController> _logger;
        private readonly IWebHostEnvironment _environment;

        public DebugController(
            ISpacetimeDBService spacetimeService,
            ILogger<DebugController> logger,
            IWebHostEnvironment environment)
        {
            _spacetimeService = spacetimeService;
            _logger = logger;
            _environment = environment;
        }

        [HttpGet("tables")]
        [AllowAnonymous] // Access control is handled internally by IsDevelopment check
        public IActionResult DebugTablesPage([FromQuery] string tab = "UserProfile", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            // !!! CRITICAL SECURITY CHECK !!!
            if (!_environment.IsDevelopment())
            {
                _logger.LogWarning("Attempted access to debug page in non-development environment.");
                return NotFound(); // Or Forbid()
            }

            if (!IsBrowserRequest())
            {
                return BadRequest(new { Success = false, Message = "This endpoint is for browser access only." });
            }

            try
            {
                _logger.LogInformation("Accessing Debug Tables Page. Tab: {Tab}, Page: {Page}, PageSize: {PageSize}", tab, page, pageSize);

                var conn = _spacetimeService.GetConnection();
                if (conn == null)
                {
                    return Content(RenderErrorPage("SpacetimeDB connection is not available."), "text/html");
                }

                var tablesInfo = GetDebugTables();
                var selectedTableInfo = tablesInfo.FirstOrDefault(t => t.TableDbName.Equals(tab, StringComparison.OrdinalIgnoreCase));

                if (selectedTableInfo == null)
                {
                    selectedTableInfo = tablesInfo.First(); // Default to first table
                    tab = selectedTableInfo.TableDbName;
                }

                // Fetch data and paginate - This part requires explicit handling per table
                object pageItems; // Use object to hold different list types
                int totalItems;

                // Explicitly handle each table type due to SpacetimeDB client limitations
                // This switch statement fetches ALL items then paginates in memory - CAUTION with large tables!
                switch (selectedTableInfo.TableDbName)
                {
                    case "UserProfile":
                        var allUsers = conn.Db.UserProfile.Iter().ToList();
                        totalItems = allUsers.Count;
                        pageItems = allUsers.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        break;
                    case "UserSettings":
                        var allUserSettings = conn.Db.UserSettings.Iter().ToList();
                        totalItems = allUserSettings.Count;
                        pageItems = allUserSettings.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        break;
                    case "Role":
                        var allRoles = conn.Db.Role.Iter().ToList();
                        totalItems = allRoles.Count;
                        pageItems = allRoles.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        break;
                    case "Permission":
                        var allPermissions = conn.Db.Permission.Iter().ToList();
                        totalItems = allPermissions.Count;
                        pageItems = allPermissions.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        break;
                    case "UserRole":
                        var allUserRoles = conn.Db.UserRole.Iter().ToList();
                        totalItems = allUserRoles.Count;
                        pageItems = allUserRoles.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        break;
                    case "RolePermission":
                        var allRolePermissions = conn.Db.RolePermission.Iter().ToList();
                        totalItems = allRolePermissions.Count;
                        pageItems = allRolePermissions.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        break;
                    // --- Authentication Tables ---
                    case "TwoFactorToken":
                        var all2FATokens = conn.Db.TwoFactorToken.Iter().ToList();
                        totalItems = all2FATokens.Count;
                        pageItems = all2FATokens.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        break;
                    case "TotpSecret":
                        var allTotpSecrets = conn.Db.TotpSecret.Iter().ToList();
                        totalItems = allTotpSecrets.Count;
                        pageItems = allTotpSecrets.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        break;
                    case "WebAuthnCredential":
                        var allWebAuthnCreds = conn.Db.WebAuthnCredential.Iter().ToList();
                        totalItems = allWebAuthnCreds.Count;
                        pageItems = allWebAuthnCreds.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        break;
                    case "WebAuthnChallenge":
                        var allWebAuthnChalls = conn.Db.WebAuthnChallenge.Iter().ToList();
                        totalItems = allWebAuthnChalls.Count;
                        pageItems = allWebAuthnChalls.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        break;
                    case "MagicLinkToken":
                        var allMagicLinks = conn.Db.MagicLinkToken.Iter().ToList();
                        totalItems = allMagicLinks.Count;
                        pageItems = allMagicLinks.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        break;
                    case "QRSession":
                        var allQrSessions = conn.Db.QrSession.Iter().ToList();
                        totalItems = allQrSessions.Count;
                        pageItems = allQrSessions.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        break;
                    // --- OIDC Tables ---
                    case "OpenIdConnect":
                        var allOidcApps = conn.Db.OpenIdConnect.Iter().ToList();
                        totalItems = allOidcApps.Count;
                        pageItems = allOidcApps.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        break;
                    case "OpenIdConnectGrant":
                        var allOidcGrants = conn.Db.OpenIdConnectGrant.Iter().ToList();
                        totalItems = allOidcGrants.Count;
                        pageItems = allOidcGrants.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        break;
                    // --- App Specific Tables ---
                    case "Bus":
                        var allBuses = conn.Db.Bus.Iter().ToList();
                        totalItems = allBuses.Count;
                        pageItems = allBuses.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        break;
                    case "Maintenance":
                        var allMaintenance = conn.Db.Maintenance.Iter().ToList();
                        totalItems = allMaintenance.Count;
                        pageItems = allMaintenance.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        break;
                    case "Route":
                        var allRoutes = conn.Db.Route.Iter().ToList();
                        totalItems = allRoutes.Count;
                        pageItems = allRoutes.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        break;
                    case "RouteSchedule":
                        var allSchedules = conn.Db.RouteSchedule.Iter().ToList();
                        totalItems = allSchedules.Count;
                        pageItems = allSchedules.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        break;
                    case "Employee":
                        var allEmployees = conn.Db.Employee.Iter().ToList();
                        totalItems = allEmployees.Count;
                        pageItems = allEmployees.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        break;
                    case "Job":
                        var allJobs = conn.Db.Job.Iter().ToList();
                        totalItems = allJobs.Count;
                        pageItems = allJobs.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        break;
                    case "Ticket":
                        var allTickets = conn.Db.Ticket.Iter().ToList();
                        totalItems = allTickets.Count;
                        pageItems = allTickets.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        break;
                    case "Sale":
                        var allSales = conn.Db.Sale.Iter().ToList();
                        totalItems = allSales.Count;
                        pageItems = allSales.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        break;
                    default:
                        pageItems = new List<object>();
                        totalItems = 0;
                        break;
                }

                int totalPages = (int)Math.Ceiling((double)totalItems / pageSize);
                if (page > totalPages && totalPages > 0) page = totalPages; // Adjust page if out of bounds
                if (page < 1) page = 1;

                return Content(RenderDebugPage(tablesInfo, selectedTableInfo, pageItems, page, pageSize, totalPages), "text/html");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating debug tables page");
                // Use the existing error page renderer
                return Content(RenderErrorPage($"Error generating debug page: {ex.Message}"), "text/html");
            }
        }

        // --- Helper for Debug Page: Table Metadata ---
        private class DebugTableInfo
        {
            public required string TableDbName { get; set; } // e.g., "UserProfile"
            public required string DisplayName { get; set; } // e.g., "User Profiles"
            public required List<string> PropertiesToShow { get; set; } // Property names as strings
            public required Type EntityType { get; set; } // The actual C# type of the table entity
        }

        // --- Helper for Debug Page: Define Tables to Show ---
        private List<DebugTableInfo> GetDebugTables() => new List<DebugTableInfo> {
            // Auth/User Related
            new DebugTableInfo { TableDbName = "UserProfile", DisplayName = "User Profiles", EntityType = typeof(UserProfile),
                PropertiesToShow = new List<string> { "UserId", "LegacyUserId", "XUID", "Login", "Email", "PhoneNumber", "IsActive", "CreatedAt", "LastLoginAt" } },
            new DebugTableInfo { TableDbName = "UserSettings", DisplayName = "User Settings", EntityType = typeof(UserSettings),
                PropertiesToShow = new List<string> { "UserSettingId", "UserId", "TotpEnabled", "WebAuthnEnabled" } }, // Add more settings bools if needed
            new DebugTableInfo { TableDbName = "Role", DisplayName = "Roles", EntityType = typeof(Role),
                PropertiesToShow = new List<string> { "RoleId", "LegacyRoleId", "Name", "Description", "IsSystem", "Priority", "IsActive" } },
            new DebugTableInfo { TableDbName = "Permission", DisplayName = "Permissions", EntityType = typeof(Permission),
                PropertiesToShow = new List<string> { "PermissionId", "Name", "Description", "Category", "IsActive" } },
            new DebugTableInfo { TableDbName = "UserRole", DisplayName = "User Roles (Junction)", EntityType = typeof(UserRole),
                PropertiesToShow = new List<string> { "Id", "UserId", "RoleId", "AssignedAt", "AssignedBy" } },
            new DebugTableInfo { TableDbName = "RolePermission", DisplayName = "Role Permissions (Junction)", EntityType = typeof(RolePermission),
                PropertiesToShow = new List<string> { "Id", "RoleId", "PermissionId", "GrantedAt", "GrantedBy" } },
            // Security Tokens
            new DebugTableInfo { TableDbName = "TwoFactorToken", DisplayName = "2FA Tokens", EntityType = typeof(TwoFactorToken),
                PropertiesToShow = new List<string> { "Id", "UserId", "Token", "ExpiresAt", "IsUsed", "DeviceInfo", "IpAddress" } },
            new DebugTableInfo { TableDbName = "TotpSecret", DisplayName = "TOTP Secrets", EntityType = typeof(TotpSecret),
                PropertiesToShow = new List<string> { "Id", "UserId", "Secret", "CreatedAt", "IsActive" } },
            new DebugTableInfo { TableDbName = "WebAuthnCredential", DisplayName = "WebAuthn Credentials", EntityType = typeof(WebAuthnCredential),
                PropertiesToShow = new List<string> { "Id", "UserId", "CredentialId", "PublicKey", "Counter", "CreatedAt", "IsActive", "DeviceName" } },
            new DebugTableInfo { TableDbName = "WebAuthnChallenge", DisplayName = "WebAuthn Challenges", EntityType = typeof(WebAuthnChallenge),
                PropertiesToShow = new List<string> { "Id", "UserId", "Challenge", "ExpiresAt", "CreatedAt" } },
            new DebugTableInfo { TableDbName = "MagicLinkToken", DisplayName = "Magic Link Tokens", EntityType = typeof(MagicLinkToken),
                PropertiesToShow = new List<string> { "Token", "UserId", "ExpiresAt", "IsUsed", "DeviceInfo", "IpAddress" } },
            new DebugTableInfo { TableDbName = "QRSession", DisplayName = "QR Sessions", EntityType = typeof(QrSession),
                PropertiesToShow = new List<string> { "SessionId", "UserId", "ValidationCode", "ExpiryTime", "InitiatingDevice", "IsUsed" } },
            // OIDC Tables
            new DebugTableInfo { TableDbName = "OpenIdConnect", DisplayName = "OIDC Clients", EntityType = typeof(OpenIdConnect),
                PropertiesToShow = new List<string> { "ClientId", "DisplayName", "ClientType", "ConsentType", "IsActive", "RedirectUris", "AllowedScopes" } }, // Add more as needed
            new DebugTableInfo { TableDbName = "OpenIdConnectGrant", DisplayName = "OIDC Grants", EntityType = typeof(OpenIdConnectGrant),
                PropertiesToShow = new List<string> { "GrantId", "ClientId", "UserId", "Type", "Status", "Scopes", "ExpiresAt", "IsRevoked" } }, // Add more as needed

            // App Specific Tables
            new DebugTableInfo { TableDbName = "Bus", DisplayName = "Buses", EntityType = typeof(Bus),
                PropertiesToShow = new List<string> { "BusId", "Model", "RegistrationNumber", "IsActive", "BusType", "Capacity", "Year" } },
            new DebugTableInfo { TableDbName = "Maintenance", DisplayName = "Maintenance", EntityType = typeof(Maintenance),
                PropertiesToShow = new List<string> { "MaintenanceId", "BusId", "LastServiceDate", "NextServiceDate", "ServiceEngineer", "Roadworthiness", "MaintenanceType", "MaintenanceCost" } },
            new DebugTableInfo { TableDbName = "Route", DisplayName = "Routes", EntityType = typeof(SpacetimeDB.Types.Route),
                PropertiesToShow = new List<string> { "RouteId", "RouteNumber", "StartPoint", "EndPoint", "DriverId", "BusId", "IsActive" } },
            new DebugTableInfo { TableDbName = "RouteSchedule", DisplayName = "Schedules", EntityType = typeof(RouteSchedule),
                PropertiesToShow = new List<string> { "ScheduleId", "RouteId", "StartPoint", "EndPoint", "RouteStops", "DepartureTime", "ArrivalTime", "Price", "AvailableSeats", "DaysOfWeek", "IsActive" } },
            new DebugTableInfo { TableDbName = "Employee", DisplayName = "Employees", EntityType = typeof(Employee),
                PropertiesToShow = new List<string> { "EmployeeId", "Surname", "Name", "JobId", "ContactPhone", "CurrentStatus" } },
            new DebugTableInfo { TableDbName = "Job", DisplayName = "Jobs", EntityType = typeof(Job),
                PropertiesToShow = new List<string> { "JobId", "JobTitle", "Department", "BaseSalary" } },
            new DebugTableInfo { TableDbName = "Ticket", DisplayName = "Tickets", EntityType = typeof(Ticket),
                PropertiesToShow = new List<string> { "TicketId", "RouteId", "SeatNumber", "TicketPrice", "PaymentMethod", "IsActive", "PurchaseTime", "TicketType", "TicketStatus" } },
            new DebugTableInfo { TableDbName = "Sale", DisplayName = "Sales", EntityType = typeof(Sale),
                PropertiesToShow = new List<string> { "SaleId", "TicketId", "SaleDate", "TicketSoldToUser", "TicketSoldToUserPhone", "SellerId", "TotalAmount", "PaymentMethod", "PaymentStatus" } },
            // Add other tables like BusStop, BusLocation, etc. following the same pattern
        };

        // --- Helper for Debug Page: Render Table Data ---
        private string RenderTableData(DebugTableInfo tableInfo, object items, int page, int pageSize, int totalPages)
        {
            var headers = tableInfo.PropertiesToShow;
            var rowsHtml = new StringBuilder();

            // Cast items to IEnumerable to iterate
            var enumerableItems = items as System.Collections.IEnumerable;
            if (enumerableItems == null) return "<p>Error: Could not cast items to IEnumerable.</p>";

            int count = 0;
            foreach (var item in enumerableItems)
            {
                count++;
                rowsHtml.Append("<tr>");
                var itemType = item.GetType(); // Get the actual type of the item in the list

                // Check if itemType matches the expected EntityType
                if (itemType != tableInfo.EntityType && !itemType.IsSubclassOf(tableInfo.EntityType)) {
                    rowsHtml.Append($"<td colspan='{headers.Count}'>Error: Item type mismatch. Expected {tableInfo.EntityType.Name}, got {itemType.Name}</td>");
                    rowsHtml.Append("</tr>");
                    continue; // Skip this item
                }

                foreach (var propName in headers)
                {
                    string displayValue = "N/A";
                    try
                    {
                        var propInfo = tableInfo.EntityType.GetProperty(propName); // Use stored EntityType
                        if (propInfo != null)
                        {
                            object? value = propInfo.GetValue(item);
                            displayValue = FormatValue(value);
                        } 
                        else 
                        {
                            // Try to access as a field if property not found
                            var fieldInfo = tableInfo.EntityType.GetField(propName);
                            if (fieldInfo != null)
                            {
                                object? value = fieldInfo.GetValue(item);
                                displayValue = FormatValue(value);
                            }
                            else
                            {
                                _logger.LogWarning("Neither property nor field {PropertyName} found on type {TypeName}", propName, tableInfo.EntityType.Name);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error accessing property or field {PropertyName} on type {TypeName}", propName, tableInfo.EntityType.Name);
                        displayValue = $"<span style='color:var(--error-color);'>Error</span>";
                    }
                    rowsHtml.Append($"<td style='padding: 8px; vertical-align: top; max-width: 250px; overflow-wrap: break-word;'>{displayValue}</td>");
                }
                rowsHtml.Append("</tr>");
            }
            if (count == 0) {
                rowsHtml.Append($"<tr><td colspan='{headers.Count}' style='padding: 16px; text-align: center; color: var(--text-muted);'>No data found for this table.</td></tr>");
            }

            // Pagination Controls
            var paginationHtml = new StringBuilder("<div style='margin-top: 1rem; display: flex; justify-content: space-between; align-items: center;'>");
            paginationHtml.Append($"<span style='font-size: 0.9rem; color: var(--text-muted);'>Page {page} of {totalPages}</span>");
            paginationHtml.Append("<div>");
            if (page > 1)
            {
                paginationHtml.Append($"<a href='?tab={tableInfo.TableDbName}&page={page - 1}&pageSize={pageSize}' class='btn btn-secondary size-s_root__CoSn6' style='width: auto; margin-right: 5px;'>Previous</a>");
            }
            if (page < totalPages)
            {
                paginationHtml.Append($"<a href='?tab={tableInfo.TableDbName}&page={page + 1}&pageSize={pageSize}' class='btn btn-secondary size-s_root__CoSn6' style='width: auto;'>Next</a>");
            }
            paginationHtml.Append("</div></div>");

            return $@"
                <div style='overflow-x: auto;'>
                    <table style='width: 100%; border-collapse: collapse;'>
                        <thead>
                            <tr style='border-bottom: 2px solid var(--border-color);'>
                                {string.Join("", headers.Select(h => $"<th style='padding: 8px 12px; text-align: left; font-weight: 500; color: var(--text-muted);'>{h}</th>"))}
                            </tr>
                        </thead>
                        <tbody>
                            {rowsHtml}
                        </tbody>
                    </table>
                </div>
                {paginationHtml}
            ";
        }

        // --- Helper for Debug Page: Format Values ---
        private string FormatValue(object? value)
        {
            if (value == null) return "<span style='color: var(--text-muted); font-style: italic;'>null</span>";

            if (value is ulong timestamp)
            {
                try { return DateTimeOffset.FromUnixTimeMilliseconds((long)timestamp).ToString("yyyy-MM-dd HH:mm:ss"); }
                catch { return timestamp.ToString() + " (err)"; } // Fallback if timestamp is invalid
            }
            if (value is Identity id) return $"<code style='font-size: 0.85em;'>{id}</code>";
            if (value is byte[] bytes) return $"<span style='color: var(--text-muted); font-style: italic;'>byte[{bytes.Length}]</span>"; // Don't display full byte array
            if (value is string[] strArray) return $"<span style='color: var(--text-muted); font-style: italic;'>[{string.Join(", ", strArray.Select(s => $"\"{HttpUtility.HtmlEncode(s)}\""))}]</span>";
            if (value is double[] dblArray) return $"<span style='color: var(--text-muted); font-style: italic;'>[{string.Join(", ", dblArray)}]</span>";
            
            // Handle generic List<T> for various types
            if (value is System.Collections.Generic.List<string> stringList)
            {
                return $"<span style='color: var(--text-muted); font-style: italic;'>[{string.Join(", ", stringList.Select(s => $"\"{HttpUtility.HtmlEncode(s)}\""))}]</span>";
            }
            if (value is System.Collections.Generic.List<double> doubleList)
            {
                return $"<span style='color: var(--text-muted); font-style: italic;'>[{string.Join(", ", doubleList)}]</span>";
            }
            if (value is System.Collections.Generic.List<int> intList)
            {
                return $"<span style='color: var(--text-muted); font-style: italic;'>[{string.Join(", ", intList)}]</span>";
            }
            
            // Handle any other IEnumerable type
            if (value is System.Collections.IEnumerable enumerable && !(value is string))
            {
                var items = new List<string>();
                foreach (var item in enumerable)
                {
                    items.Add(item?.ToString() ?? "null");
                }
                return $"<span style='color: var(--text-muted); font-style: italic;'>[{string.Join(", ", items)}]</span>";
            }
            
            if (value is bool b) return b ? "<span style='color: var(--success-color);'>True</span>" : "<span style='color: var(--error-color);'>False</span>";
            if (value is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm:ss"); // Should not happen if using ulong, but just in case

            // Default: Encode and potentially truncate long strings
            var strValue = value.ToString() ?? "";
            var encodedValue = HttpUtility.HtmlEncode(strValue);
            if(encodedValue.Length > 100) {
                return encodedValue.Substring(0, 100) + "...";
            }
            return encodedValue;
        }

        // --- Helper for Debug Page: Render the full page ---
        private string RenderDebugPage(List<DebugTableInfo> tables, DebugTableInfo selectedTable, object items, int page, int pageSize, int totalPages)
        {
            var tabsHtml = new StringBuilder(@"<div class='tabs' style='margin-bottom: 1.5rem; border-bottom: 1px solid var(--border-color); display: flex; flex-wrap: wrap;'>");
            foreach (var table in tables)
            {
                var isActive = table.TableDbName.Equals(selectedTable.TableDbName, StringComparison.OrdinalIgnoreCase);
                tabsHtml.Append($@"
                    <a href='?tab={table.TableDbName}&page=1&pageSize={pageSize}'
                       style='padding: 0.75rem 1rem; text-decoration: none; color: {(isActive ? "var(--primary-color)" : "var(--text-muted)")}; border-bottom: 2px solid {(isActive ? "var(--primary-color)" : "transparent")}; margin-bottom: -1px; transition: color 0.2s, border-color 0.2s;'>
                        {table.DisplayName}
                    </a>");
            }
            tabsHtml.Append("</div>");

            var tableHtml = RenderTableData(selectedTable, items, page, pageSize, totalPages);

            var content = $@"
                <div class=""profile-container"" style=""max-width: 1400px;""> 
                    <h1 style=""font: var(--id-typography-heading-l); margin-bottom: 1.5rem;"">SpacetimeDB Debug View</h1>
                    {tabsHtml}
                    <div class=""tab-content"">
                        <h2 style=""font: var(--id-typography-heading-m); margin-bottom: 1rem;"">{selectedTable.DisplayName}</h2>
                        {tableHtml}
                    </div>
                </div>";

            // Use BaseHtmlTemplate without the auth-page-body class
            return string.Format(BaseHtmlTemplate, "SpacetimeDB Debug", content, "");
        }

        // --- Helper for Error Page ---
        private string RenderErrorPage(string errorMessage)
        {
            var content = $@"
                <div class=""profile-container"">
                    <h1 style=""font: var(--id-typography-heading-l); margin-bottom: 1.5rem; color: var(--error-color);"">Error</h1>
                    <div class=""error-message"" style=""padding: 1rem; background-color: var(--error-bg-color); border-radius: 8px; margin-bottom: 1.5rem;"">
                        <p style=""color: var(--error-color); margin: 0;"">{HttpUtility.HtmlEncode(errorMessage)}</p>
                    </div>
                    <a href=""?"" class=""btn btn-primary size-m_root__CoSn6"" style=""width: auto;"">Try Again</a>
                </div>";

            return string.Format(BaseHtmlTemplate, "Error - SpacetimeDB Debug", content, "");
        }

        // --- Helper to check if request is from a browser ---
        private bool IsBrowserRequest()
        {
            var userAgent = Request.Headers["User-Agent"].ToString().ToLower();
            return userAgent.Contains("mozilla") || userAgent.Contains("chrome") || 
                   userAgent.Contains("safari") || userAgent.Contains("edge") || 
                   userAgent.Contains("opera");
        }

        // --- Base HTML Template ---
        private const string BaseHtmlTemplate = @"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{0}</title>
    <style>
        :root {{
            --primary-color: #fc3f1d;
            --primary-dark: #d93412;
            --primary-light: #ff5c3e;
            --background-color: #f6f7f8;
            --card-color: #ffffff;
            --text-color: #21201f;
            --text-muted: #838383;
            --border-color: #e7e8ea;
            --error-color: #ef4444;
            --success-color: #10b981;
            --warning-color: #f59e0b;
            --shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
            
            /* Yandex ID specific variables */
            --id-color-surface-submerged: #f6f7f8;
            --id-color-surface-elevated-0: #ffffff;
            --id-color-line-normal: #e7e8ea;
            --id-color-default-bg-base: #f5f5f5;
            --id-color-status-negative: #ff3333;
            --id-card-border-radius: 12px;
            --id-typography-heading-l: 500 28px/32px 'YS Text', 'Helvetica Neue', Arial, sans-serif;
            --id-typography-heading-m: 500 20px/24px 'YS Text', 'Helvetica Neue', Arial, sans-serif;
            --id-typography-text-m: 400 16px/20px 'YS Text', 'Helvetica Neue', Arial, sans-serif;
            --id-typography-text-s: 400 14px/18px 'YS Text', 'Helvetica Neue', Arial, sans-serif;
            --id-typography-text-xs: 400 13px/16px 'YS Text', 'Helvetica Neue', Arial, sans-serif;
        }}
        
        [data-theme=""dark""] {{
            --primary-color: #fc3f1d;
            --primary-dark: #d93412;
            --primary-light: #ff5c3e;
            --background-color: #21201f;
            --card-color: #312f2f;
            --text-color: #ffffff;
            --text-muted: #b3b3b3;
            --border-color: #3b3a38;
            --shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
            
            /* Yandex ID dark mode colors */
            --id-color-surface-submerged: #21201f;
            --id-color-surface-elevated-0: #312f2f;
            --id-color-line-normal: #3b3a38;
        }}

        * {{
            margin: 0;
            padding: 0;
            box-sizing: border-box;
            font-family: 'YS Text', 'Helvetica Neue', Arial, sans-serif;
        }}

        body {{
            background-color: var(--background-color);
            color: var(--text-color);
            line-height: 1.5;
            min-height: 100vh;
            transition: all 0.3s ease;
            display: flex;
            flex-direction: column;
            height: 100vh;
        }}

        .auth-page-body {{
            background-color: var(--background-color);
            background-image: url('https://yastatic.net/s3/passport-auth/freezer/_/12l0Lb-3jyLI.jpg');
            background-size: cover;
            background-position: center;
            background-repeat: no-repeat;
        }}

        [data-theme=""dark""] .auth-page-body {{
            background-image: url('https://yastatic.net/s3/passport-auth/freezer/_/12l0Lb-3jyLI.jpg');
        }}

        .navbar {{
            display: flex;
            justify-content: space-between;
            align-items: center;
            padding: 1rem 2rem;
            background-color: var(--card-color);
            box-shadow: var(--shadow);
            position: relative;
            z-index: 10;
        }}

        .logo {{
            font-size: 1.5rem;
            font-weight: 500;
            color: var(--text-color);
            text-decoration: none;
            display: flex;
            align-items: center;
        }}

        .logo::before {{
            content: '';
            display: inline-block;
            width: 24px;
            height: 24px;
            background-color: var(--primary-color);
            border-radius: 4px;
            margin-right: 8px;
        }}

        .theme-toggle {{
            background: none;
            border: none;
            color: var(--text-color);
            cursor: pointer;
            font-size: 1.2rem;
            width: 2.5rem;
            height: 2.5rem;
            border-radius: 50%;
            display: flex;
            align-items: center;
            justify-content: center;
            transition: background-color 0.2s;
        }}

        .theme-toggle:hover {{
            background-color: var(--border-color);
        }}

        .container {{
            max-width: 400px;
            margin: 2rem auto;
            padding: 0 1rem;
            width: 100%;
            flex: 1;
            display: flex;
            flex-direction: column;
            justify-content: center;
        }}

        .login-container {{
            display: flex;
            flex-direction: column;
            justify-content: center;
            align-items: center;
            min-height: calc(100vh - 64px);
            padding: 1rem;
        }}

        .card {{
            background-color: var(--card-color);
            border-radius: 0.75rem;
            box-shadow: var(--shadow);
            overflow: hidden;
            transition: all 0.3s ease;
            width: 100%;
            max-width: 400px;
        }}

        .auth-card {{
            background-color: #21201f;
            color: white;
            border-radius: 1rem;
            box-shadow: 0 4px 20px rgba(0, 0, 0, 0.25);
            max-width: 360px;
        }}

        .card-header {{
            padding: 1.5rem;
            border-bottom: 1px solid var(--border-color);
        }}

        .card-body {{
            padding: 1.5rem;
        }}

        .card-footer {{
            padding: 1rem 1.5rem;
            border-top: 1px solid var(--border-color);
            display: flex;
            justify-content: space-between;
            align-items: center;
        }}

        h1, h2, h3, h4, h5, h6 {{
            color: var(--text-color);
            font-weight: 500;
            margin-bottom: 0.5rem;
        }}

        .auth-card h1, .auth-card h2, .auth-card h3, .auth-card label, .auth-card p {{
            color: white;
        }}

        h1 {{
            font-size: 1.75rem;
        }}

        p {{
            margin-bottom: 1rem;
            color: var(--text-muted);
        }}

        .form-group {{
            margin-bottom: 1.25rem;
        }}

        label {{
            display: block; 
            margin-bottom: 0.5rem;
            font-weight: 400;
        }}

        input, select, textarea {{
            width: 100%;
            padding: 0.75rem;
            border: 1px solid var(--border-color);
            border-radius: 0.5rem;
            font-size: 1rem;
            background-color: var(--card-color);
            color: var(--text-color);
            transition: border-color 0.15s ease-in-out, box-shadow 0.15s ease-in-out;
        }}

        .auth-card input {{
            background-color: rgba(255, 255, 255, 0.1);
            color: white;
            border: none;
        }}

        input:focus, select:focus, textarea:focus {{
            outline: none;
            border-color: var(--primary-color);
            box-shadow: 0 0 0 2px rgba(252, 63, 29, 0.1);
        }}

        button, .btn {{
            display: inline-block;
            width: 100%;
            padding: 0.75rem 1.5rem;
            background-color: var(--primary-color);
            color: white;
            border: none;
            border-radius: 0.5rem;
            font-size: 1rem;
            font-weight: 500;
            cursor: pointer;
            transition: background-color 0.15s ease-in-out, transform 0.1s ease;
            text-align: center;
            text-decoration: none;
        }}

        .auth-card button {{
            background-color: white;
            color: black;
        }}

        button:hover, .btn:hover {{
            background-color: var(--primary-dark);
        }}

        .auth-card button:hover {{
            background-color: #f0f0f0;
        }}

        button:active, .btn:active {{
            transform: translateY(1px);
        }}

        .btn-secondary {{
            background-color: transparent;
            color: var(--primary-color);
            border: 1px solid var(--primary-color);
        }}

        .btn-secondary:hover {{
            background-color: rgba(252, 63, 29, 0.1);
        }}

        .btn-block {{
            display: block;
            width: 100%;
        }}

        .error-message {{
            color: var(--error-color);
            background-color: rgba(239, 68, 68, 0.1);
            padding: 0.75rem;
            border-radius: 0.5rem;
            margin-bottom: 1.5rem;
            font-size: 0.875rem;
            display: flex;
            align-items: center;
        }}

        .success-message {{
            color: var(--success-color);
            background-color: rgba(16, 185, 129, 0.1);
            padding: 0.75rem;
            border-radius: 0.5rem;
            margin-bottom: 1.5rem;
            font-size: 0.875rem;
            display: flex;
            align-items: center;
        }}

        .qr-code {{
            display: flex;
            justify-content: center;
            margin: 2rem 0;
        }}

        .qr-code img {{
            max-width: 200px;
            height: auto;
            padding: 0.5rem;
            background-color: white;
            border-radius: 0.5rem;
        }}

        .code-display {{
            font-family: monospace;
            background-color: rgba(0, 0, 0, 0.05);
            padding: 0.5rem;
            border-radius: 0.25rem;
            word-break: break-all;
            margin: 0.5rem 0;
        }}

        [data-theme=""dark""] .code-display {{
            background-color: rgba(255, 255, 255, 0.05);
        }}

        .text-center {{
            text-align: center;
        }}

        .info-box {{
            background-color: rgba(252, 63, 29, 0.07);
            padding: 1rem;
            border-radius: 0.5rem;
            margin-bottom: 1.5rem;
        }}

        .link {{
            color: var(--primary-color);
            text-decoration: none;
            transition: color 0.15s ease;
        }}

        .auth-card .link {{
            color: #76a6f5;
        }}

        .link:hover {{
            color: var(--primary-dark);
            text-decoration: underline;
        }}

        .text-muted {{
            color: var(--text-muted);
        }}

        .flex {{
            display: flex;
        }}

        .flex-col {{
            flex-direction: column;
        }}

        .flex-wrap {{
            flex-wrap: wrap;
        }}

        .items-center {{
            align-items: center;
        }}

        .justify-center {{
            justify-content: center;
        }}

        .justify-between {{
            justify-content: space-between;
        }}

        .gap-2 {{
            gap: 0.5rem;
        }}

        .gap-4 {{
            gap: 1rem;
        }}

        .my-2 {{
            margin-top: 0.5rem;
            margin-bottom: 0.5rem;
        }}

        .my-4 {{
            margin-top: 1rem;
            margin-bottom: 1rem;
        }}

        .mt-4 {{
            margin-top: 1rem;
        }}

        .mt-8 {{
            margin-top: 2rem;
        }}

        /* Layout styles */
        .page-wrapper {{
            display: flex;
            min-height: 100vh;
        }}

        .sidebar {{
            width: 250px;
            background-color: var(--card-color);
            padding: 1.5rem 0;
            border-right: 1px solid var(--border-color);
        }}

        .sidebar-link {{
            display: flex;
            align-items: center;
            padding: 0.75rem 1.5rem;
            color: var(--text-color);
            text-decoration: none;
            transition: background-color 0.15s ease;
        }}

        .sidebar-link:hover {{
            background-color: var(--background-color);
        }}

        .sidebar-link.active {{
            border-left: 3px solid var(--primary-color);
            background-color: var(--background-color);
        }}

        .main-content {{
            flex: 1;
            padding: 1.5rem;
            overflow-y: auto;
        }}

        .profile-page-wrapper {{
            display: flex;
            min-height: 100vh;
            flex-direction: column;
        }}

        .profile-container {{
            max-width: 1200px;
            margin: 0 auto;
            padding: 1.5rem;
            width: 100%;
            display: flex;
            flex-direction: column;
            align-items: center;
        }}

        /* Yandex ID specific styles */
        .profile-content-wrapper {{
            display: flex;
            width: 100%;
            min-height: calc(100vh - 64px);
        }}

        .profile-main-content {{
            flex-grow: 1;
            padding: 24px;
            background-color: var(--id-color-surface-submerged);
        }}

        .Section_root__zl60G {{
            background: var(--id-color-surface-elevated-0);
            padding: 24px;
            margin-bottom: 6px;
            border-radius: var(--id-card-border-radius);
            width: 100%;
        }}

        .Section_inner__N7MeR {{
            max-width: 520px;
            margin: 0 auto;
        }}

        .Heading_root__P0ine {{
            margin-bottom: 16px;
        }}
        .Text_root__J8eOj {{
            display: block;
        }}

        .Text_root__J8eOj[data-variant=""heading-m""] {{
            font: var(--id-typography-heading-m);
        }}

        .Text_root__J8eOj[data-variant=""text-m""] {{
            font: var(--id-typography-text-m);
        }}

        .Text_root__J8eOj[data-variant=""text-s""] {{
            font: var(--id-typography-text-s);
        }}

        .Text_root__J8eOj[data-variant=""text-xs""] {{
            font: var(--id-typography-text-xs);
        }}

        .Text_root__J8eOj[data-color=""secondary""] {{
            color: var(--text-muted);
        }}

        .Text_root__J8eOj[data-color=""tertiary""] {{
            color: var(--text-muted-secondary, rgba(60, 60, 60, 0.7));
        }}

        [data-theme=""dark""] .Text_root__J8eOj[data-color=""tertiary""] {{
            color: var(--text-muted-secondary, rgba(200, 200, 200, 0.7));
        }}

        .UnstyledListItem_root__xsw4w {{
            padding: 12px 0;
        }}

        .UnstyledListItem_inner__Td3gb {{
            display: flex;
            justify-content: space-between;
            align-items: center;
        }}

        .Slot_root__jYlNI {{
            display: flex;
        }}

        .Slot_direction_vertical__I3MEt {{
            flex-direction: column;
        }}

        .Slot_direction_horizontal__aDFeG {{
            flex-direction: row;
        }}

        .Slot_content__XYDYF {{
            flex: 1;
        }}

        .alignment-center_root__ndulA {{
            align-items: center;
        }}

        .alignment-top_root____eiv {{
            align-items: flex-start;
        }}

        .Button_root__rneDS {{
            font-family: 'YS Text', 'Helvetica Neue', Arial, sans-serif;
            position: relative;
            display: inline-flex;
            align-items: center;
            justify-content: center;
            box-sizing: border-box;
            font-weight: 500;
            cursor: pointer;
            transition: 0.1s ease-out;
            text-decoration: none;
            border-radius: 8px;
        }}

        .text-button_root__doKoA {{
            background: transparent;
            color: var(--primary-color);
            border: none;
            padding: 0;
        }}

        .text-button_root__doKoA:hover {{
            color: var(--primary-dark);
            background: transparent;
        }}

        .size-m_root___r3aA {{
            font-size: 16px;
            line-height: 20px;
        }}

        .size-s_root__CoSn6 {{
            font-size: 14px;
            line-height: 18px;
        }}

        .variant-default_root__xWqkR {{
            background-color: var(--primary-color);
            color: white;
            border: none;
            padding: 13px 20px;
        }}

        .variant-default_root__xWqkR:hover {{
            background-color: var(--primary-dark);
        }}

        .size-l_root__PsIsm {{
            font-size: 18px;
            line-height: 22px;
            padding: 16px 24px;
        }}

        .user-avatar_root__CsKdB {{
            position: relative;
            display: inline-block;
            overflow: hidden;
            border-radius: 50%;
        }}

        .user-avatar_root_isBig__RozUb {{
            --id-avatar-size: 96px;
            width: var(--id-avatar-size);
            height: var(--id-avatar-size);
        }}

        .avatar_root__qDicj {{
            width: 100%;
            height: 100%;
            object-fit: cover;
        }}

        .profile-card_root__hJtgV {{
            display: flex;
            flex-direction: column;
            align-items: center;
            text-align: center;
        }}

        .profile-card_avatar__xb4bd {{
            margin-bottom: 8px;
        }}

        .profile-card_title__zZCrX {{
            font: var(--id-typography-heading-l);
            font-weight: 500;
            margin-bottom: 4px;
        }}

        .profile-card_description__nvlpy {{
            font: var(--id-typography-text-m);
        }}

        .bulleted-list_root__k0lgY {{
            padding: 0;
            margin: 0;
            list-style: none;
        }}

        .bulleted-list-item_root__1Y90C {{
            position: relative;
            padding-left: 0;
        }}

        .bulleted-list-item_root__1Y90C:not(:last-child)::after {{
            content: '•';
            margin: 0 6px;
            color: var(--text-muted);
        }}

        .bulleted-list-item_root__1Y90C:first-child {{
            padding-left: 0;
        }}

        .List_root__yESwN {{
            list-style: none;
            padding: 0;
            margin: 0;
        }}

        .unstyled-badge_root__1gOSr {{
            display: inline-flex;
            align-items: center;
            justify-content: center;
            padding: 4px 8px;
            border-radius: 4px;
            background-color: rgba(0, 0, 0, 0.05);
            margin: 2px;
        }}

        .sidebar-navigation_root__2HXQL {{
            padding: 16px 0;
        }}

        .sidebar-navigation_list__R_7Wh {{
            list-style: none;
            padding: 0;
            margin: 0;
        }}

        .sidebar-navigation_item__GvUUF {{
            margin-bottom: 4px;
        }}

        .base-item_root__Z_6ST {{
            display: flex;
            align-items: center;
            gap: 12px;
            padding: 12px 16px;
            color: var(--text-color);
            text-decoration: none;
            border-radius: 8px;
            margin: 0 8px;
        }}

        .base-item_root__Z_6ST:hover {{
            background-color: rgba(0, 0, 0, 0.05);
        }}

        .navigation-item-link_root_isActive__QZ9Ea {{
            background-color: rgba(252, 63, 29, 0.1);
            color: var(--primary-color);
        }}

        .svg-icon {{
            flex-shrink: 0;
        }}

        @media (max-width: 640px) {{
            .container {{
                margin: 1rem auto;
            }}
            
            .card {{
                border-radius: 0.5rem;
            }}
            
            .card-header, .card-body, .card-footer {{
                padding: 1rem;
            }}

            .sidebar {{
                width: 100%;
                border-right: none;
                border-bottom: 1px solid var(--border-color);
                padding: 0.75rem 0;
            }}

            .page-wrapper {{
                flex-direction: column;
            }}
            
            .profile-content-wrapper {{
                flex-direction: column;
            }}
            
            .profile-main-content {{
                padding: 16px;
            }}
            
            .Section_root__zl60G {{
                padding: 16px;
            }}
        }}

        .fade-in {{
            animation: fadeIn 0.3s ease-in-out;
        }}

        @keyframes fadeIn {{
            from {{ opacity: 0; transform: translateY(10px); }}
            to {{ opacity: 1; transform: translateY(0); }}
        }}

        .loader {{
            border: 2px solid rgba(252, 63, 29, 0.1);
            border-radius: 50%;
            border-top: 2px solid var(--primary-color);
            width: 24px;
            height: 24px;
            animation: spin 1s linear infinite;
            margin: 0 auto;
            display: inline-block;
        }}

        @keyframes spin {{
            0% {{ transform: rotate(0deg); }}
            100% {{ transform: rotate(360deg); }}
        }}

        /* Social login buttons */
        .social-buttons {{
            display: flex;
            justify-content: center;
            gap: 1rem;
            margin-top: 1.5rem;
        }}

        .social-button {{
            width: 40px;
            height: 40px;
            border-radius: 50%;
            display: flex;
            align-items: center;
            justify-content: center;
            background-color: rgba(255, 255, 255, 0.1);
            cursor: pointer;
            transition: background-color 0.15s ease;
        }}

        .social-button:hover {{
            background-color: rgba(255, 255, 255, 0.2);
        }}

        .divider {{
            display: flex;
            align-items: center;
            text-align: center;
            margin: 1.5rem 0;
        }}

        .divider::before,
        .divider::after {{
            content: '';
            flex: 1;
            border-bottom: 1px solid var(--border-color);
        }}

        .divider span {{
            padding: 0 0.75rem;
            color: var(--text-muted);
        }}

        /* Yandex specific elements */
        .yandex-id-header {{
            display: flex;
            align-items: center;
            justify-content: center;
            margin-bottom: 1.5rem;
        }}

        .yandex-id-header img {{
            height: 32px;
        }}

        .auth-footer {{
            text-align: center;
            margin-top: 1.5rem;
            font-size: 0.875rem;
            color: var(--text-muted);
        }}

        /* QR code login styling */
        .qr-login-container {{
            text-align: center;
        }}

        .qr-login-container .qr-code {{
            padding: 1rem;
            background-color: white;
            border-radius: 0.75rem;
            display: inline-flex;
        }}

        .secondary-option {{
            background-color: rgba(255, 255, 255, 0.1);
            color: white;
            border: none;
            border-radius: 0.5rem;
            padding: 0.75rem;
            margin-top: 1rem;
            cursor: pointer;
            transition: background-color 0.15s ease;
            width: 100%;
            text-align: center;
            display: flex;
            align-items: center;
            justify-content: center;
            gap: 0.5rem;
        }}

        .secondary-option:hover {{
            background-color: rgba(255, 255, 255, 0.15);
        }}
    </style>
</head>
<body class=""{2}"">
    <div class=""navbar"">
        <a href=""/"" class=""logo"">BRU AVTOPARK</a>
        <button class=""theme-toggle"" id=""themeToggle"" aria-label=""Toggle dark mode"">🌙</button>
    </div>
    {1}
    <script>
        // Theme toggling functionality
        const themeToggleBtn = document.getElementById('themeToggle');
        const prefersDarkScheme = window.matchMedia('(prefers-color-scheme: dark)');
        
        // Check for saved theme preference or use the system preference
        const currentTheme = localStorage.getItem('theme') || (prefersDarkScheme.matches ? 'dark' : 'light');
        
        // Set initial theme
        if (currentTheme === 'dark') {{
            document.body.setAttribute('data-theme', 'dark');
            themeToggleBtn.textContent = '☀️';
        }} else {{
            document.body.removeAttribute('data-theme');
            themeToggleBtn.textContent = '🌙';
        }}
        
        // Toggle theme when the button is clicked
        themeToggleBtn.addEventListener('click', function() {{
            let theme = 'light';
            
            if (!document.body.hasAttribute('data-theme')) {{
                document.body.setAttribute('data-theme', 'dark');
                themeToggleBtn.textContent = '☀️';
                theme = 'dark';
            }} else {{
                document.body.removeAttribute('data-theme');
                themeToggleBtn.textContent = '🌙';
            }}
            
            localStorage.setItem('theme', theme);
        }});
    </script>
</body>
</html>";

    }
}

