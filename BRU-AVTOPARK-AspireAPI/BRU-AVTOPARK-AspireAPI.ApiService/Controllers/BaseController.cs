using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using TicketSalesApp.AdminServer.Configuration;

namespace TicketSalesApp.AdminServer.Controllers
{
    /// <summary>
    /// Base controller that supports BOTH custom JWT authentication (manual parsing) 
    /// and ASP.NET Core authentication (OpenIddict/OAuth).
    /// 
    /// Controllers use [AllowAnonymous] to bypass ASP.NET auth middleware,
    /// then manually check authentication using this base class.
    /// </summary>
    public abstract class BaseController : ControllerBase
    {
        /// <summary>
        /// Cache key for storing validated OAuth claims in HttpContext.Items
        /// </summary>
        private const string ValidatedOAuthClaimsKey = "_validatedOAuthClaims";
        
        /// <summary>
        /// Cache key for storing failed OAuth validation sentinel in HttpContext.Items
        /// </summary>
        private const string ValidatedOAuthClaimsFailedKey = "_validatedOAuthClaimsFailed";
        
        /// <summary>
        /// Asynchronously validates if the current request is authenticated.
        /// Performs full validation including tokeninfo endpoint calls for encrypted tokens.
        /// </summary>
        /// <returns>True if authenticated with valid token; false otherwise.</returns>
        protected async Task<bool> IsAuthenticatedAsync()
        {
            // Check ASP.NET Core authentication first (OpenIddict)
            if (User?.Identity?.IsAuthenticated == true)
            {
                return true;
            }

            // Check if we have an Authorization header
            var authHeader = Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return false;
            }

            var token = authHeader.Substring("Bearer ".Length);
            
            // Validate token format and structure
            if (string.IsNullOrWhiteSpace(token) || token.Length < 20)
            {
                Log.Debug("IsAuthenticatedAsync - Token too short or empty");
                return false;
            }
            
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();

                // Check if it's a JWE token (encrypted OpenIddict token)
                if (IsJweToken(token))
                {
                    // For JWE tokens, validate via tokeninfo endpoint
                    Log.Debug("IsAuthenticatedAsync - Detected JWE token, validating via tokeninfo endpoint");

                    var claims = await ValidateOAuthTokenAsync();
                    if (claims == null || claims.Count == 0)
                    {
                        Log.Warning("IsAuthenticatedAsync - JWE token validation failed");
                        return false;
                    }

                    Log.Debug("IsAuthenticatedAsync - JWE token validated successfully with {ClaimCount} claims", claims.Count);
                    return true;
                }

                // For regular JWT tokens, perform full validation via ValidateOAuthTokenAsync
                // This ensures proper signature validation, issuer/audience checks, etc.
                Log.Debug("IsAuthenticatedAsync - Detected non-JWE token, performing full validation");
                var validatedClaims = await ValidateOAuthTokenAsync();
                if (validatedClaims == null || validatedClaims.Count == 0)
                {
                    Log.Warning("IsAuthenticatedAsync - Token validation failed");
                    return false;
                }

                // Validate token has required claims (sub or identity)
                var hasRequiredClaims = validatedClaims.ContainsKey("sub") ||
                                       validatedClaims.ContainsKey("identity") ||
                                       validatedClaims.ContainsKey(ClaimTypes.NameIdentifier);

                if (!hasRequiredClaims)
                {
                    Log.Warning("IsAuthenticatedAsync - Token missing required claims (sub/identity/NameIdentifier)");
                    return false;
                }

                Log.Debug("IsAuthenticatedAsync - Token validated successfully with required claims");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "IsAuthenticatedAsync - Error validating token: {Message}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Synchronously validates if the current request is authenticated.
        /// This is a backwards-compatible wrapper around IsAuthenticatedAsync.
        /// For new code, prefer using IsAuthenticatedAsync for better async/await patterns.
        /// </summary>
        /// <returns>True if authenticated with valid token; false otherwise.</returns>
        protected bool IsAuthenticated() => IsAuthenticatedAsync().GetAwaiter().GetResult();

        /// <summary>
        /// Determine whether the current request is associated with an administrator account.
        /// </summary>
        /// <remarks>
        /// Checks the ASP.NET Core authenticated principal's claims first. If not authenticated, inspects the Authorization Bearer token:
        /// - parses regular JWTs for admin-related claims, or
        /// - validates encrypted OpenIddict (JWE) tokens via the tokeninfo endpoint and inspects returned claims.
        /// Returns false on missing/invalid tokens or on any validation error.
        /// </remarks>
        /// <returns>`true` if the principal or bearer token indicates administrator privileges (via `primary_role`, `role`, or standard role claims); `false` otherwise.</returns>
        protected async Task<bool> IsAdminAsync()
        {
            try
            {
                // Try ASP.NET Core authentication first (OpenIddict)
                if (User?.Identity?.IsAuthenticated == true)
                {
                    // Log all claims for debugging
                    var allClaims = User.Claims.Select(c => $"{c.Type}={c.Value}").ToList();
                    var claimsStr = string.Join(", ", allClaims);
                    Log.Debug("IsAdmin check - User authenticated. Claims: {Claims}", claimsStr);

                    // Check primary role first
                    var primaryRole = User.FindFirst("primary_role");
                    Log.Debug("IsAdmin check - primary_role claim: {Value}", primaryRole?.Value ?? "NOT FOUND");

                    if (primaryRole?.Value == "1")
                    {
                        Log.Information("IsAdmin check - User is admin (primary_role=1)");
                        return true;
                    }

                    // Check role claims
                    var roleClaims = User.FindAll("role");
                    Log.Debug("IsAdmin check - role claims count: {Count}", roleClaims.Count());
                    foreach (var claim in roleClaims)
                    {
                        Log.Debug("IsAdmin check - role claim value: {Value}", claim.Value);
                    }

                    if (roleClaims.Any(c => c.Value == "1" || c.Value == "Administrator"))
                    {
                        Log.Information("IsAdmin check - User is admin (role claim)");
                        return true;
                    }

                    // Check standard role claims
                    var standardRoleClaims = User.FindAll(ClaimTypes.Role);
                    Log.Debug("IsAdmin check - standard role claims count: {Count}", standardRoleClaims.Count());
                    if (standardRoleClaims.Any(c => c.Value == "1" || c.Value == "Administrator"))
                    {
                        Log.Information("IsAdmin check - User is admin (standard role claim)");
                        return true;
                    }

                    Log.Warning("IsAdmin check - User authenticated but not admin");
                }
                else
                {
                    Log.Debug("IsAdmin check - User not authenticated via ASP.NET Core");
                }

                // Try to parse as custom JWT
                var authHeader = Request.Headers["Authorization"].ToString();
                if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                {
                    Log.Warning("IsAdmin check - Missing or invalid Authorization header");
                    return false;
                }

                var token = authHeader.Substring("Bearer ".Length);
                var tokenHandler = new JwtSecurityTokenHandler();

                // CRITICAL FIX: Check if it's an encrypted token (JWE) by examining the structure
                if (IsJweToken(token))
                {
                    // It's an encrypted JWE token from OpenIddict, validate via tokeninfo endpoint
                    Log.Debug("IsAdmin check - Token is encrypted (JWE), validating via tokeninfo endpoint");
                    var claims = await ValidateOAuthTokenAsync();
                    
                    if (claims == null)
                    {
                        Log.Warning("IsAdmin check - Token validation failed");
                        return false;
                    }
                    
                    Log.Debug("IsAdmin check (OAuth) - Retrieved {ClaimCount} claims from tokeninfo", claims.Count);
                    
                    // Check primary_role in claims
                    if (claims.TryGetValue("primary_role", out var primaryRoleObj) && primaryRoleObj?.ToString() == "1")
                    {
                        Log.Information("IsAdmin check (OAuth) - User is admin (primary_role=1)");
                        return true;
                    }
                    
                    // Check role claims
                    if (claims.TryGetValue("role", out var roleObj))
                    {
                        if (roleObj is List<string> roleList && (roleList.Contains("1") || roleList.Contains("Administrator")))
                        {
                            Log.Information("IsAdmin check (OAuth) - User is admin (role claim in list)");
                            return true;
                        }
                        else if (roleObj?.ToString() == "1" || roleObj?.ToString() == "Administrator")
                        {
                            Log.Information("IsAdmin check (OAuth) - User is admin (role claim)");
                            return true;
                        }
                    }
                    
                    // Check http://schemas.microsoft.com/ws/2008/06/identity/claims/role
                    if (claims.TryGetValue("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", out var standardRoleObj))
                    {
                        if (standardRoleObj is List<string> standardRoleList && (standardRoleList.Contains("1") || standardRoleList.Contains("Administrator")))
                        {
                            Log.Information("IsAdmin check (OAuth) - User is admin (standard role claim in list)");
                            return true;
                        }
                        else if (standardRoleObj?.ToString() == "1" || standardRoleObj?.ToString() == "Administrator")
                        {
                            Log.Information("IsAdmin check (OAuth) - User is admin (standard role claim)");
                            return true;
                        }
                    }
                    
                    Log.Warning("IsAdmin check (OAuth) - User not admin. Claims: {Claims}", string.Join(", ", claims.Keys));
                    return false;
                }
                
                // CRITICAL FIX: Validate JWT signature properly
                if (tokenHandler.CanReadToken(token))
                {
                    // It's a regular JWT - validate it properly
                    Log.Debug("IsAdmin check (JWT) - Validating JWT signature");
                    var claims = await ValidateOAuthTokenAsync();
                    
                    if (claims == null)
                    {
                        Log.Warning("IsAdmin check (JWT) - Token validation failed");
                        return false;
                    }
                    
                    // Check primary_role in validated claims
                    if (claims.TryGetValue("primary_role", out var primaryRoleObj) && primaryRoleObj?.ToString() == "1")
                    {
                        Log.Information("IsAdmin check (JWT) - User is admin (primary_role=1)");
                        return true;
                    }
                    
                    // Check role claims
                    if (claims.TryGetValue("role", out var roleObj))
                    {
                        if (roleObj is List<string> roleList && (roleList.Contains("1") || roleList.Contains("Administrator")))
                        {
                            Log.Information("IsAdmin check (JWT) - User is admin (role claim in list)");
                            return true;
                        }
                        else if (roleObj?.ToString() == "1" || roleObj?.ToString() == "Administrator")
                        {
                            Log.Information("IsAdmin check (JWT) - User is admin (role claim)");
                            return true;
                        }
                    }
                    
                    // Check http://schemas.microsoft.com/ws/2008/06/identity/claims/role
                    if (claims.TryGetValue("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", out var standardRoleObj))
                    {
                        if (standardRoleObj is List<string> standardRoleList && (standardRoleList.Contains("1") || standardRoleList.Contains("Administrator")))
                        {
                            Log.Information("IsAdmin check (JWT) - User is admin (standard role claim in list)");
                            return true;
                        }
                        else if (standardRoleObj?.ToString() == "1" || standardRoleObj?.ToString() == "Administrator")
                        {
                            Log.Information("IsAdmin check (JWT) - User is admin (standard role claim)");
                            return true;
                        }
                    }
                    
                    Log.Warning("IsAdmin check (JWT) - User not admin");
                    return false;
                }
                
                // If we get here, token format is unknown
                Log.Warning("IsAdmin check - Unknown token format");
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking admin status");
                return false;
            }
        }

        /// <summary>
        /// Checks if the current user has a specific permission.
        /// Supports both ASP.NET Core auth and custom JWT.
        /// </summary>
        protected bool HasPermission(string permissionName)
        {
            return HasPermissionAsync(permissionName).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Checks if the current user has a specific permission using pre-validated claims.
        /// This overload avoids re-validation when claims have already been validated.
        /// </summary>
        /// <param name="permissionName">The permission name to check (e.g., "buses.view").</param>
        /// <param name="validatedClaims">Pre-validated claims dictionary from ValidateOAuthTokenAsync.</param>
        /// <returns>True if the user has the specified permission; false otherwise.</returns>
        protected bool HasPermission(string permissionName, Dictionary<string, object>? validatedClaims)
        {
            try
            {
                // Try ASP.NET Core authentication first (OpenIddict)
                if (User?.Identity?.IsAuthenticated == true)
                {
                    var permissionClaims = User.FindAll("permission");
                    if (permissionClaims.Any(c => c.Value == permissionName))
                    {
                        return true;
                    }
                }

                // Use pre-validated claims if provided
                if (validatedClaims != null && validatedClaims.TryGetValue("permission", out var permissionObj))
                {
                    if (permissionObj is List<string> permissionList && permissionList.Contains(permissionName))
                    {
                        return true;
                    }
                    else if (permissionObj?.ToString() == permissionName)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking permission: {Permission}", permissionName);
                return false;
            }
        }

        /// <summary>
        /// Asynchronously checks if the current user has a specific permission by inspecting validated claims.
        /// This method properly handles both ASP.NET Core authenticated principals and OAuth tokens (including JWE).
        /// </summary>
        /// <param name="permissionName">The permission name to check (e.g., "buses.view").</param>
        /// <returns>True if the user has the specified permission; false otherwise.</returns>
        /// <summary>
        /// Asynchronously checks if the current user has a specific permission by inspecting validated claims.
        /// This method properly handles both ASP.NET Core authenticated principals and OAuth tokens (including JWE).
        /// </summary>
        /// <param name="permissionName">The permission name to check (e.g., "buses.view").</param>
        /// <returns>True if the user has the specified permission; false otherwise.</returns>
        protected async Task<bool> HasPermissionAsync(string permissionName)
        {
            try
            {
                // Try ASP.NET Core authentication first (OpenIddict)
                if (User?.Identity?.IsAuthenticated == true)
                {
                    var permissionClaims = User.FindAll("permission");
                    Log.Debug("HasPermission check for '{Permission}' - permission claims count: {Count}", permissionName, permissionClaims.Count());

                    if (permissionClaims.Any(c => c.Value == permissionName))
                    {
                        Log.Information("HasPermission check - User has permission '{Permission}'", permissionName);
                        return true;
                    }
                }

                // For tokens that aren't already authenticated, validate via OAuth
                // This handles both regular JWTs and encrypted JWE tokens
                var claims = await ValidateOAuthTokenAsync();

                // Delegate to the synchronous overload with validated claims
                return HasPermission(permissionName, claims);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking permission: {Permission}", permissionName);
                return false;
            }
        }


        /// <summary>
        /// Gets the current user's ID from claims.
        /// Supports both ASP.NET Core auth and custom JWT.
        /// </summary>
        protected string? GetUserId()
        {
            return GetUserIdAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Asynchronously gets the current user's ID from validated claims.
        /// This method properly handles both ASP.NET Core authenticated principals and OAuth tokens (including JWE).
        /// </summary>
        /// <returns>The user ID (sub claim) if present; otherwise null.</returns>
        protected async Task<string?> GetUserIdAsync()
        {
            try
            {
                // Try ASP.NET Core authentication first (OpenIddict)
                if (User?.Identity?.IsAuthenticated == true)
                {
                    var subClaim = User.FindFirst("sub") ?? User.FindFirst(ClaimTypes.NameIdentifier);
                    if (subClaim != null)
                    {
                        return subClaim.Value;
                    }
                }

                // For tokens that aren't already authenticated, validate via OAuth
                // This handles both regular JWTs and encrypted JWE tokens
                var claims = await ValidateOAuthTokenAsync();
                if (claims == null)
                {
                    Log.Warning("GetUserId - Token validation failed");
                    return null;
                }

                // Try to extract user ID from validated claims
                if (claims.TryGetValue("sub", out var subObj))
                {
                    return subObj?.ToString();
                }

                if (claims.TryGetValue(ClaimTypes.NameIdentifier, out var nameIdObj))
                {
                    return nameIdObj?.ToString();
                }

                Log.Warning("GetUserId - No user ID claim found in validated token");
                return null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting user ID from token");
                return null;
            }
        }

        /// <summary>
        /// Gets the SpacetimeDB identity from claims using validated OAuth tokens.
        /// Supports both ASP.NET Core auth and custom JWT/JWE tokens.
        /// </summary>
        /// <returns>The SpacetimeDB identity string if present; otherwise null.</returns>
        protected async Task<string?> GetSpacetimeIdentityAsync()
        {
            try
            {
                // Try ASP.NET Core authentication first (OpenIddict)
                if (User?.Identity?.IsAuthenticated == true)
                {
                    var identityClaim = User.FindFirst("identity") ?? User.FindFirst("spacetime_identity");
                    if (identityClaim != null)
                    {
                        return identityClaim.Value;
                    }
                }

                // For tokens that aren't already authenticated, validate via OAuth
                // This handles both regular JWTs and encrypted JWE tokens
                var claims = await ValidateOAuthTokenAsync();
                if (claims != null)
                {
                    if (claims.TryGetValue("identity", out var identityObj))
                    {
                        return identityObj?.ToString();
                    }
                    if (claims.TryGetValue("spacetime_identity", out var spacetimeIdentityObj))
                    {
                        return spacetimeIdentityObj?.ToString();
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting SpacetimeDB Identity from token");
                return null;
            }
        }

        /// <summary>
        /// Synchronous wrapper for GetSpacetimeIdentityAsync - for backward compatibility.
        /// Prefer using GetSpacetimeIdentityAsync when possible.
        /// </summary>
        /// <returns>The SpacetimeDB identity string if present; otherwise null.</returns>
        protected string? GetSpacetimeIdentity() => GetSpacetimeIdentityAsync().GetAwaiter().GetResult();

        /// <summary>
        /// CRITICAL FIX: Validates encrypted OpenIddict tokens by calling the tokeninfo endpoint
        /// Returns claims dictionary if valid, null if invalid.
        /// Implements per-request caching to avoid redundant validation calls.
        /// </summary>
        protected async Task<Dictionary<string, object>?> ValidateOAuthTokenAsync()
        {
            try
            {
                // Check per-request cache first
                if (HttpContext.Items.ContainsKey(ValidatedOAuthClaimsKey))
                {
                    Log.Debug("ValidateOAuthTokenAsync - Returning cached claims");
                    return HttpContext.Items[ValidatedOAuthClaimsKey] as Dictionary<string, object>;
                }
                
                if (HttpContext.Items.ContainsKey(ValidatedOAuthClaimsFailedKey))
                {
                    Log.Debug("ValidateOAuthTokenAsync - Returning cached validation failure");
                    return null;
                }
                
                var authHeader = Request.Headers["Authorization"].ToString();
                if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                {
                    // Fallback to query parameter for SignalR/WebSocket connections
                    var accessToken = Request.Query["access_token"].ToString();
                    if (string.IsNullOrEmpty(accessToken))
                    {
                        Log.Debug("ValidateOAuthTokenAsync - No Bearer token found in header or query parameter");
                        HttpContext.Items[ValidatedOAuthClaimsFailedKey] = true;
                        return null;
                    }
                    
                    Log.Debug("ValidateOAuthTokenAsync - Using access_token from query parameter");
                    authHeader = $"Bearer {accessToken}";
                }

                var token = authHeader.Substring("Bearer ".Length);
                
                // CRITICAL: Route based on token structure (dot-separated segment count)
                // 5 segments = JWE (encrypted token) -> call tokeninfo endpoint
                // 3 segments = JWT (signed token) -> validate locally
                // Other = invalid token format
                var tokenParts = token.Split('.');
                
                if (tokenParts.Length == 5)
                {
                    // It's a JWE (encrypted token), call tokeninfo endpoint to validate and get claims
                    Log.Information("ValidateOAuthTokenAsync - Token has 5 segments (JWE encrypted), calling tokeninfo endpoint");
                    
                    // Use IHttpClientFactory to avoid socket exhaustion
                    var httpClientFactory = HttpContext.RequestServices.GetService<IHttpClientFactory>();
                    if (httpClientFactory == null)
                    {
                        Log.Error("ValidateOAuthTokenAsync - IHttpClientFactory not available");
                        HttpContext.Items[ValidatedOAuthClaimsFailedKey] = true;
                        return null;
                    }
                    
                    var httpClient = httpClientFactory.CreateClient("TokenInfo");
                    httpClient.DefaultRequestHeaders.Authorization = 
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    
                    // Use the same base URL as the current request (preserve reverse-proxy prefix)
                    var baseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase.ToUriComponent()}";
                    var tokeninfoUrl = $"{baseUrl}/connect/tokeninfo";
                    
                    Log.Debug("ValidateOAuthTokenAsync - Calling {Url}", tokeninfoUrl);
                    
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, timeoutCts.Token);

                    string content;
                    int statusCode;
                    try
                    {
                        using var response = await httpClient.GetAsync(tokeninfoUrl, linkedCts.Token);
                        statusCode = (int)response.StatusCode;
                        if (!response.IsSuccessStatusCode)
                        {
                            var errorContent = await response.Content.ReadAsStringAsync(linkedCts.Token);
                            Log.Warning("ValidateOAuthTokenAsync - Token validation failed with status {StatusCode}: {Error}",
                                response.StatusCode, errorContent);
                            HttpContext.Items[ValidatedOAuthClaimsFailedKey] = true;
                            return null;
                        }
                        content = await response.Content.ReadAsStringAsync(linkedCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        Log.Warning("ValidateOAuthTokenAsync - Request cancelled or timed out calling tokeninfo");
                        HttpContext.Items[ValidatedOAuthClaimsFailedKey] = true;
                        return null;
                    }

                    Log.Debug("ValidateOAuthTokenAsync - Tokeninfo response: {Content}", content);

                    System.Text.Json.JsonElement tokenInfo;
                    try
                    {
                        tokenInfo = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(content);
                    }
                    catch (System.Text.Json.JsonException ex)
                    {
                        Log.Warning(ex, "ValidateOAuthTokenAsync - Failed to parse tokeninfo JSON response");
                        HttpContext.Items[ValidatedOAuthClaimsFailedKey] = true;
                        return null;
                    }

                    if (tokenInfo.TryGetProperty("claims", out var claimsElement))
                    {
                        var claims = ExtractClaimsFromJsonElement(claimsElement);

                        // Defence-in-depth: verify exp even though tokeninfo should already reject expired tokens.
                        if (claims.TryGetValue("exp", out var expObj) &&
                            long.TryParse(expObj?.ToString(), out var expUnix))
                        {
                            var expiry = DateTimeOffset.FromUnixTimeSeconds(expUnix);
                            if (expiry < DateTimeOffset.UtcNow)
                            {
                                Log.Warning("ValidateOAuthTokenAsync - JWE exp claim indicates expiry at {Expiry} (now {Now})",
                                    expiry, DateTimeOffset.UtcNow);
                                HttpContext.Items[ValidatedOAuthClaimsFailedKey] = true;
                                return null;
                            }
                        }

                        Log.Information("ValidateOAuthTokenAsync - Successfully extracted {ClaimCount} claims from tokeninfo", claims.Count);
                        HttpContext.Items[ValidatedOAuthClaimsKey] = claims;
                        return claims;
                    }
                    
                    Log.Warning("ValidateOAuthTokenAsync - No claims property found in tokeninfo response; keys present: {Keys}",
                        string.Join(", ", tokenInfo.EnumerateObject().Select(p => p.Name)));
                    HttpContext.Items[ValidatedOAuthClaimsFailedKey] = true;
                    return null;
                }
                
                // Check if it's a 3-segment JWT we can validate locally
                if (tokenParts.Length == 3)
                {
                    var tokenHandler = new JwtSecurityTokenHandler();
                    if (tokenHandler.CanReadToken(token))
                    {
                        Log.Debug("ValidateOAuthTokenAsync - Token has 3 segments (JWT), performing local signature validation");

                        // Resolve settings: prefer strongly-typed options, fall back to raw config.
                        var jwtOptions = HttpContext.RequestServices.GetService<IOptions<JwtSettings>>()?.Value;
                        var cfg        = HttpContext.RequestServices.GetService<IConfiguration>();

                        var jwtSecret = jwtOptions?.Secret ?? cfg?["JwtSettings:Secret"];
                        if (string.IsNullOrEmpty(jwtSecret))
                        {
                            Log.Error("ValidateOAuthTokenAsync - JWT secret not configured");
                            HttpContext.Items[ValidatedOAuthClaimsFailedKey] = true;
                            return null;
                        }

                        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtSecret));

                        var validateIssuer   = jwtOptions?.ValidateIssuer   ?? false;
                        var validateAudience = jwtOptions?.ValidateAudience ?? false;
                        var requireExp       = jwtOptions?.RequireExpiration ?? true;
                        var validateNbf      = jwtOptions?.ValidateNbf       ?? true;
                        var clockSkew        = TimeSpan.FromMinutes(jwtOptions?.ClockSkewMinutes ?? 5);
                        var validIssuer      = jwtOptions?.Issuer   ?? cfg?["JwtSettings:Issuer"];
                        var validAudience    = jwtOptions?.Audience ?? cfg?["JwtSettings:Audience"];

                        var validationParameters = new TokenValidationParameters
                        {
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey         = key,

                            ValidateIssuer   = validateIssuer && !string.IsNullOrEmpty(validIssuer),
                            ValidIssuer      = validIssuer,

                            ValidateAudience = validateAudience && !string.IsNullOrEmpty(validAudience),
                            ValidAudience    = validAudience,

                            ValidateLifetime      = true,
                            RequireExpirationTime = requireExp,
                            RequireSignedTokens   = true,
                            ClockSkew             = clockSkew
                        };

                        try
                        {
                            var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

                            // Type guard – ensures we have a proper JWT, not some other token type.
                            if (validatedToken is not JwtSecurityToken jwt)
                            {
                                Log.Warning("ValidateOAuthTokenAsync - Validated token is not a JwtSecurityToken");
                                HttpContext.Items[ValidatedOAuthClaimsFailedKey] = true;
                                return null;
                            }

                            // Explicit nbf check when the toggle is on (defence-in-depth).
                            if (validateNbf && jwt.ValidFrom > DateTimeOffset.UtcNow.Add(clockSkew))
                            {
                                Log.Warning("ValidateOAuthTokenAsync - Token not yet valid (nbf={Nbf}, now={Now})",
                                    jwt.ValidFrom, DateTimeOffset.UtcNow);
                                HttpContext.Items[ValidatedOAuthClaimsFailedKey] = true;
                                return null;
                            }

                            // Extract claims, merging duplicates into List<string>.
                            var claims = new Dictionary<string, object>();
                            foreach (var claim in principal.Claims)
                            {
                                if (claims.TryGetValue(claim.Type, out var existing))
                                {
                                    if (existing is List<string> list)
                                        list.Add(claim.Value);
                                    else
                                        claims[claim.Type] = new List<string> { existing.ToString()!, claim.Value };
                                }
                                else
                                {
                                    claims[claim.Type] = claim.Value;
                                }
                            }

                            Log.Debug("ValidateOAuthTokenAsync - Successfully validated JWT with {ClaimCount} claims (alg={Alg}, validateIssuer={VI}, validateAudience={VA}, requireExp={RE}, validateNbf={VN})",
                                claims.Count, jwt.Header.Alg, validateIssuer, validateAudience, requireExp, validateNbf);
                            HttpContext.Items[ValidatedOAuthClaimsKey] = claims;
                            return claims;
                        }
                        catch (SecurityTokenExpiredException ex)
                        {
                            Log.Warning("ValidateOAuthTokenAsync - Token expired at {Expiry}: {Message}", ex.Expires, ex.Message);
                            HttpContext.Items[ValidatedOAuthClaimsFailedKey] = true;
                            return null;
                        }
                        catch (SecurityTokenNotYetValidException ex)
                        {
                            Log.Warning("ValidateOAuthTokenAsync - Token not yet valid (nbf={Nbf}): {Message}", ex.NotBefore, ex.Message);
                            HttpContext.Items[ValidatedOAuthClaimsFailedKey] = true;
                            return null;
                        }
                        catch (SecurityTokenInvalidSignatureException ex)
                        {
                            Log.Warning("ValidateOAuthTokenAsync - Invalid signature: {Message}", ex.Message);
                            HttpContext.Items[ValidatedOAuthClaimsFailedKey] = true;
                            return null;
                        }
                        catch (SecurityTokenException ex)
                        {
                            Log.Warning(ex, "ValidateOAuthTokenAsync - JWT validation failed ({ExType}): {Message}",
                                ex.GetType().Name, ex.Message);
                            HttpContext.Items[ValidatedOAuthClaimsFailedKey] = true;
                            return null;
                        }
                    }
                }
                
                // Unknown or invalid token format
                Log.Warning("ValidateOAuthTokenAsync - Invalid token format (expected 3 or 5 segments, got {Count})", tokenParts.Length);
                HttpContext.Items[ValidatedOAuthClaimsFailedKey] = true;
                return null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ValidateOAuthTokenAsync - Error validating OAuth token: {Message}", ex.Message);
                HttpContext.Items[ValidatedOAuthClaimsFailedKey] = true;
                return null;
            }
        }

        /// <summary>
        /// Checks if a token is a JWE (encrypted) token by examining its structure.
        ///
        /// Detection strategy (in order):
        ///   1. Segment count: 5 segments → JWE, 3 segments → JWT, anything else → invalid.
        ///   2. Header inspection: for ambiguous 3-segment tokens, decode the JOSE header and
        ///      look for the "enc" field which is mandatory in JWE but absent in JWT.
        ///
        /// The base64url decode uses the correct padding formula: pad to the next multiple of 4.
        /// Any decode/parse failure is treated as "not a JWE" (returns false) so callers fall
        /// through to the JWT path and fail there with a proper error.
        /// </summary>
        private static bool IsJweToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            try
            {
                // JWE: header.encrypted_key.iv.ciphertext.tag  (5 segments)
                // JWT: header.payload.signature                 (3 segments)
                var parts = token.Split('.');

                if (parts.Length == 5)
                    return true;

                if (parts.Length != 3)
                    return false;

                // Decode the JOSE header (first segment) using correct base64url padding.
                // Padding = (4 - len % 4) % 4  →  0, 3, 2, 1 extra '=' chars.
                var header = parts[0];
                var paddingNeeded = (4 - header.Length % 4) % 4;
                var base64 = header.Replace('-', '+').Replace('_', '/') + new string('=', paddingNeeded);
                var headerBytes = Convert.FromBase64String(base64);
                var headerJson = System.Text.Encoding.UTF8.GetString(headerBytes);

                // JWE JOSE headers MUST contain "enc" (content-encryption algorithm).
                // JWT JOSE headers only contain "alg" and optionally "typ"/"kid".
                using var doc = System.Text.Json.JsonDocument.Parse(headerJson);
                return doc.RootElement.TryGetProperty("enc", out _);
            }
            catch
            {
                // Any malformed input → treat as non-JWE; the caller will handle the error.
                return false;
            }
        }

        /// <summary>
        /// Retrieve the XUID claim for the current request's user from ASP.NET Core claims or from validated OAuth token.
        /// </summary>
        /// <returns>The XUID string if present; otherwise null.</returns>
        protected async Task<string?> GetXuidAsync()
        {
            try
            {
                // Try ASP.NET Core authentication first (OpenIddict)
                if (User?.Identity?.IsAuthenticated == true)
                {
                    var xuidClaim = User.FindFirst("xuid");
                    if (xuidClaim != null)
                    {
                        return xuidClaim.Value;
                    }
                }

                // For tokens that aren't already authenticated, validate via OAuth
                // This handles both regular JWTs and encrypted JWE tokens
                var claims = await ValidateOAuthTokenAsync();
                if (claims != null)
                {
                    if (claims.TryGetValue("xuid", out var xuidObj))
                    {
                        return xuidObj?.ToString();
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting XUID from token");
                return null;
            }
        }

        /// <summary>
        /// Synchronous wrapper for GetXuidAsync - for backward compatibility.
        /// Prefer using GetXuidAsync when possible.
        /// </summary>
        /// <returns>The XUID string if present; otherwise null.</returns>
        protected string? GetXuid() => GetXuidAsync().GetAwaiter().GetResult();

        /// <summary>
        /// Retrieve the current user's username from ASP.NET Core claims or from validated OAuth token claims.
        /// </summary>
        /// <returns>The username if present in claims (`"name"`, `"preferred_username"`, or `ClaimTypes.Name`); otherwise <c>null</c>.</returns>
        protected async Task<string?> GetUserNameAsync()
        {
            try
            {
                // Try ASP.NET Core authentication first (OpenIddict)
                if (User?.Identity?.IsAuthenticated == true)
                {
                    var nameClaim = User.FindFirst("name") ?? User.FindFirst(ClaimTypes.Name) ?? User.FindFirst("preferred_username");
                    if (nameClaim != null)
                    {
                        return nameClaim.Value;
                    }
                }

                // For tokens that aren't already authenticated, validate via OAuth
                // This handles encrypted JWE tokens that can't be parsed directly
                var claims = await ValidateOAuthTokenAsync();
                if (claims != null)
                {
                    // Try to extract username from validated claims
                    if (claims.TryGetValue("name", out var nameObj))
                    {
                        return nameObj?.ToString();
                    }
                    if (claims.TryGetValue("preferred_username", out var preferredUsernameObj))
                    {
                        return preferredUsernameObj?.ToString();
                    }
                    if (claims.TryGetValue(ClaimTypes.Name, out var claimTypesNameObj))
                    {
                        return claimTypesNameObj?.ToString();
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting user name from token");
                return null;
            }
        }

        /// <summary>
        /// Retrieve the current user's username from claims, or null if none is available.
        /// Synchronous wrapper for GetUserNameAsync - for backward compatibility. Prefer using GetUserNameAsync when possible.
        /// </summary>
        /// <returns>The username extracted from the user's claims (for example `name` or `preferred_username`), or `null` if not found.</returns>
        protected string? GetUserName() => GetUserNameAsync().GetAwaiter().GetResult();

        /// <summary>
        /// Synchronously determines whether the current user has administrator privileges.
        /// Synchronous wrapper for IsAdminAsync - for backward compatibility. Prefer using IsAdminAsync when possible.
        /// </summary>
        /// <returns>`true` if the current user is an administrator, `false` otherwise.</returns>
        protected bool IsAdmin() => IsAdminAsync().GetAwaiter().GetResult();

        /// <summary>
        /// Validates a token supplied directly as a string, supporting both JWE (encrypted,
        /// 5-segment) and JWT (signed, 3-segment) formats.
        ///
        /// Unlike <see cref="ValidateOAuthTokenAsync"/>, this method does not read from or
        /// write to the per-request <see cref="HttpContext.Items"/> cache, and does not
        /// extract the token from the request headers or query string.  It is intended for
        /// inline token validation inside WebSocket message handlers where the token arrives
        /// in the message payload rather than in the HTTP upgrade request.
        ///
        /// JWE tokens are validated by forwarding the token to the <c>/connect/tokeninfo</c>
        /// endpoint on the same host (the same path used by <see cref="ValidateOAuthTokenAsync"/>).
        /// JWT tokens are validated locally using the symmetric signing key from configuration,
        /// with full lifetime, issuer, and audience checks where configuration is available.
        ///
        /// Robustness guarantees:
        ///   - Minimum token length check (prevents trivially invalid inputs).
        ///   - HTTP call to tokeninfo is bounded by a configurable timeout (default 10 s).
        ///   - Cancellation token is forwarded to all async operations.
        ///   - JWT path enforces <c>nbf</c> (not-before) in addition to <c>exp</c>.
        ///   - Issuer and audience are validated when present in configuration.
        ///   - All failure paths emit structured log entries with a <c>reason</c> field.
        ///   - Unexpected exceptions are caught and logged; null is returned (never throws).
        /// </summary>
        /// <param name="token">The raw token string to validate.</param>
        /// <param name="cancellationToken">
        /// Optional cancellation token forwarded to HTTP and async operations.
        /// Defaults to <see cref="CancellationToken.None"/>.
        /// </param>
        /// <returns>
        /// A claims dictionary on success, or <c>null</c> if the token is invalid, expired,
        /// or cannot be validated.
        /// </returns>
        protected async Task<Dictionary<string, object>?> ValidateTokenDirectAsync(
            string token,
            CancellationToken cancellationToken = default)
        {
            // ── Guard: minimum viable token length ──────────────────────────────────
            if (string.IsNullOrWhiteSpace(token) || token.Length < 20)
            {
                Log.Warning("ValidateTokenDirectAsync - Token too short or empty, rejecting");
                return null;
            }

            try
            {
                var tokenParts = token.Split('.');

                // ── JWE path (5 segments) ────────────────────────────────────────────
                if (tokenParts.Length == 5)
                {
                    Log.Information("ValidateTokenDirectAsync - Token has 5 segments (JWE), calling tokeninfo endpoint");
                    return await ValidateJweViaTokenInfoAsync(token, cancellationToken);
                }

                // ── JWT path (3 segments) ────────────────────────────────────────────
                if (tokenParts.Length == 3)
                {
                    Log.Debug("ValidateTokenDirectAsync - Token has 3 segments (JWT), performing local validation");
                    return await ValidateJwtLocalAsync(token, cancellationToken);
                }

                Log.Warning("ValidateTokenDirectAsync - Invalid token format: expected 3 or 5 segments, got {Count}", tokenParts.Length);
                return null;
            }
            catch (OperationCanceledException)
            {
                Log.Warning("ValidateTokenDirectAsync - Validation cancelled");
                return null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ValidateTokenDirectAsync - Unexpected error: {Message}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Validates a JWE token by calling the local <c>/connect/tokeninfo</c> endpoint.
        /// The HTTP call is bounded by a 10-second timeout and respects the caller's
        /// cancellation token.  The response is parsed defensively: missing or malformed
        /// claim shapes are handled without throwing.
        /// </summary>
        private async Task<Dictionary<string, object>?> ValidateJweViaTokenInfoAsync(
            string token,
            CancellationToken cancellationToken)
        {
            var httpClientFactory = HttpContext.RequestServices.GetService<IHttpClientFactory>();
            if (httpClientFactory == null)
            {
                Log.Error("ValidateJweViaTokenInfoAsync - IHttpClientFactory not registered; cannot validate JWE token");
                return null;
            }

            var httpClient = httpClientFactory.CreateClient("TokenInfo");

            // Enforce a hard timeout so a slow/hung tokeninfo call cannot block the WS loop.
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var baseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase.ToUriComponent()}";
            var tokeninfoUrl = $"{baseUrl}/connect/tokeninfo";

            Log.Debug("ValidateJweViaTokenInfoAsync - GET {Url}", tokeninfoUrl);

            System.Net.Http.HttpResponseMessage response;
            try
            {
                response = await httpClient.GetAsync(tokeninfoUrl, linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                Log.Warning("ValidateJweViaTokenInfoAsync - tokeninfo call timed out after 10 s");
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(linkedCts.Token);
                Log.Warning("ValidateJweViaTokenInfoAsync - tokeninfo returned {StatusCode}: {Body}",
                    (int)response.StatusCode, errorBody);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(linkedCts.Token);

            System.Text.Json.JsonElement tokenInfo;
            try
            {
                tokenInfo = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(content);
            }
            catch (System.Text.Json.JsonException ex)
            {
                Log.Warning(ex, "ValidateJweViaTokenInfoAsync - Failed to parse tokeninfo JSON response");
                return null;
            }

            // The tokeninfo endpoint wraps claims under a "claims" property.
            if (!tokenInfo.TryGetProperty("claims", out var claimsElement))
            {
                Log.Warning("ValidateJweViaTokenInfoAsync - Response has no 'claims' property; keys present: {Keys}",
                    string.Join(", ", tokenInfo.EnumerateObject().Select(p => p.Name)));
                return null;
            }

            var claims = ExtractClaimsFromJsonElement(claimsElement);

            // Post-validation: ensure the token is not expired according to the returned claims.
            // The tokeninfo endpoint should already reject expired tokens, but we double-check
            // here as a defence-in-depth measure.
            if (claims.TryGetValue("exp", out var expObj) &&
                long.TryParse(expObj?.ToString(), out var expUnix))
            {
                var expiry = DateTimeOffset.FromUnixTimeSeconds(expUnix);
                if (expiry < DateTimeOffset.UtcNow)
                {
                    Log.Warning("ValidateJweViaTokenInfoAsync - Token exp claim indicates expiry at {Expiry} (now {Now})",
                        expiry, DateTimeOffset.UtcNow);
                    return null;
                }
            }

            Log.Information("ValidateJweViaTokenInfoAsync - Successfully extracted {ClaimCount} claims from JWE tokeninfo",
                claims.Count);
            return claims;
        }

        /// <summary>
        /// Validates a plain JWT locally using the symmetric signing key from configuration.
        /// Enforces signature, lifetime (exp + nbf), and optionally issuer/audience when
        /// the corresponding configuration keys are present.
        /// All validation toggles are driven by <see cref="JwtSettings"/> so they can be
        /// changed in appsettings.json without recompiling.
        /// </summary>
        private Task<Dictionary<string, object>?> ValidateJwtLocalAsync(
            string token,
            CancellationToken cancellationToken)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            if (!tokenHandler.CanReadToken(token))
            {
                Log.Warning("ValidateJwtLocalAsync - JwtSecurityTokenHandler cannot read token (malformed)");
                return Task.FromResult<Dictionary<string, object>?>(null);
            }

            // Prefer the strongly-typed options; fall back to raw config for backward compat.
            var jwtOptions = HttpContext.RequestServices.GetService<IOptions<JwtSettings>>()?.Value;
            var config     = HttpContext.RequestServices.GetService<IConfiguration>();

            var secret = jwtOptions?.Secret
                ?? config?["JwtSettings:Secret"];

            if (string.IsNullOrEmpty(secret))
            {
                Log.Error("ValidateJwtLocalAsync - JwtSettings:Secret not configured; cannot validate JWT");
                return Task.FromResult<Dictionary<string, object>?>(null);
            }

            var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(secret));

            // Resolve validation toggles from options (with safe defaults when options absent).
            var validateIssuer   = jwtOptions?.ValidateIssuer   ?? !string.IsNullOrEmpty(jwtOptions?.Issuer   ?? config?["JwtSettings:Issuer"]);
            var validateAudience = jwtOptions?.ValidateAudience ?? !string.IsNullOrEmpty(jwtOptions?.Audience ?? config?["JwtSettings:Audience"]);
            var requireExp       = jwtOptions?.RequireExpiration ?? true;
            var validateNbf      = jwtOptions?.ValidateNbf       ?? true;
            var clockSkew        = TimeSpan.FromMinutes(jwtOptions?.ClockSkewMinutes ?? 5);
            var validIssuer      = jwtOptions?.Issuer   ?? config?["JwtSettings:Issuer"];
            var validAudience    = jwtOptions?.Audience ?? config?["JwtSettings:Audience"];

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey         = key,

                ValidateIssuer   = validateIssuer && !string.IsNullOrEmpty(validIssuer),
                ValidIssuer      = validIssuer,

                ValidateAudience = validateAudience && !string.IsNullOrEmpty(validAudience),
                ValidAudience    = validAudience,

                ValidateLifetime      = true,
                RequireExpirationTime = requireExp,
                RequireSignedTokens   = true,
                ClockSkew             = clockSkew
            };

            try
            {
                var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

                if (validatedToken is not JwtSecurityToken jwt)
                {
                    Log.Warning("ValidateJwtLocalAsync - Validated token is not a JwtSecurityToken");
                    return Task.FromResult<Dictionary<string, object>?>(null);
                }

                // Explicit nbf check when the toggle is on (defence-in-depth; the handler
                // also checks nbf when ValidateLifetime is true, but we log it explicitly).
                if (validateNbf && jwt.ValidFrom > DateTimeOffset.UtcNow.Add(clockSkew))
                {
                    Log.Warning("ValidateJwtLocalAsync - Token not yet valid (nbf={Nbf}, now={Now})",
                        jwt.ValidFrom, DateTimeOffset.UtcNow);
                    return Task.FromResult<Dictionary<string, object>?>(null);
                }

                var claims = new Dictionary<string, object>();
                foreach (var claim in principal.Claims)
                {
                    if (claims.TryGetValue(claim.Type, out var existing))
                    {
                        if (existing is List<string> list)
                            list.Add(claim.Value);
                        else
                            claims[claim.Type] = new List<string> { existing.ToString()!, claim.Value };
                    }
                    else
                    {
                        claims[claim.Type] = claim.Value;
                    }
                }

                Log.Debug("ValidateJwtLocalAsync - JWT validated successfully with {ClaimCount} claims (alg={Alg}, validateIssuer={VI}, validateAudience={VA}, requireExp={RE}, validateNbf={VN})",
                    claims.Count, jwt.Header.Alg, validateIssuer, validateAudience, requireExp, validateNbf);
                return Task.FromResult<Dictionary<string, object>?>(claims);
            }
            catch (SecurityTokenExpiredException ex)
            {
                Log.Warning("ValidateJwtLocalAsync - Token expired at {Expiry}: {Message}", ex.Expires, ex.Message);
                return Task.FromResult<Dictionary<string, object>?>(null);
            }
            catch (SecurityTokenNotYetValidException ex)
            {
                Log.Warning("ValidateJwtLocalAsync - Token not yet valid (nbf={Nbf}): {Message}", ex.NotBefore, ex.Message);
                return Task.FromResult<Dictionary<string, object>?>(null);
            }
            catch (SecurityTokenInvalidSignatureException ex)
            {
                Log.Warning("ValidateJwtLocalAsync - Invalid signature: {Message}", ex.Message);
                return Task.FromResult<Dictionary<string, object>?>(null);
            }
            catch (SecurityTokenException ex)
            {
                Log.Warning(ex, "ValidateJwtLocalAsync - JWT validation failed ({ExType}): {Message}",
                    ex.GetType().Name, ex.Message);
                return Task.FromResult<Dictionary<string, object>?>(null);
            }
        }

        /// <summary>
        /// Extracts a <c>Dictionary&lt;string, object&gt;</c> from a <see cref="System.Text.Json.JsonElement"/>
        /// that represents a JSON object of claim name → value pairs.
        ///
        /// Value mapping rules:
        ///   - JSON array          → <c>List&lt;string&gt;</c>
        ///   - JSON object with <c>$values</c> (JSON.NET reference format) → <c>List&lt;string&gt;</c>
        ///   - JSON string         → <c>string</c>
        ///   - JSON number/boolean → <c>string</c> (via <c>ToString()</c>)
        ///   - Anything else       → <c>string</c>
        /// </summary>
        private static Dictionary<string, object> ExtractClaimsFromJsonElement(
            System.Text.Json.JsonElement claimsElement)
        {
            var claims = new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (var claim in claimsElement.EnumerateObject())
            {
                object value = claim.Value.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.Array => claim.Value
                        .EnumerateArray()
                        .Select(i => i.GetString() ?? "")
                        .ToList<string>() as object,

                    System.Text.Json.JsonValueKind.Object when
                        claim.Value.TryGetProperty("$values", out var valuesArray) =>
                        valuesArray.EnumerateArray()
                            .Select(i => i.GetString() ?? "")
                            .ToList<string>() as object,

                    System.Text.Json.JsonValueKind.String =>
                        claim.Value.GetString() ?? "",

                    _ => claim.Value.ToString()
                };

                // Merge duplicate claim names into a List<string> (same as JWT multi-value handling).
                if (claims.TryGetValue(claim.Name, out var existing))
                {
                    if (existing is List<string> existingList)
                    {
                        if (value is List<string> newList)
                            existingList.AddRange(newList);
                        else
                            existingList.Add(value.ToString()!);
                    }
                    else
                    {
                        var merged = new List<string> { existing.ToString()! };
                        if (value is List<string> newList2)
                            merged.AddRange(newList2);
                        else
                            merged.Add(value.ToString()!);
                        claims[claim.Name] = merged;
                    }
                }
                else
                {
                    claims[claim.Name] = value;
                }
            }

            return claims;
        }

        /// <summary>
        /// Get the client's IP address from the current HTTP context.
        /// Uses HttpContext.Connection.RemoteIpAddress which is populated by ForwardedHeadersMiddleware.
        /// </summary>
        /// <returns>The client's IP address as a string, or "unknown" if it cannot be determined.</returns>
        protected string GetClientIp()
        {
            try
            {
                // Use RemoteIpAddress which is populated by ForwardedHeadersMiddleware
                // after validation of X-Forwarded-For and X-Real-IP headers at the edge
                return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting client IP");
                return "unknown";
            }
        }
    }
} 