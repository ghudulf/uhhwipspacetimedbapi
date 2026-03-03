using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using BRU_AVTOPARK_AspireAPI.ApiService.Models.Auth;
using BRU_AVTOPARK_AspireAPI.ApiService.Services.Auth;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Controllers
{
    public partial class AuthController
    {
        // ── GET+POST /connect/authorize ─────────────────────────────────

        [HttpGet("~/connect/authorize")]
        [HttpPost("~/connect/authorize")]
        [AllowAnonymous]
        public async Task<IActionResult> Authorize()
        {
            try
            {
                var oidcRequest = HttpContext.GetOpenIddictServerRequest()
                    ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

                var clientId = oidcRequest.ClientId ?? "";
                var (appSuccess, application, appError) =
                    await _openIdConnectService.GetApplicationByClientIdAsync(clientId);

                if (!appSuccess || application == null)
                {
                    return IsBrowserRequest()
                        ? Redirect($"/api/auth/error?message={Uri.EscapeDataString(appError ?? "Unknown client")}")
                        : BadRequest(new ApiResponse<object> { Success = false, Message = appError ?? "Unknown client" });
                }

                var scopes = oidcRequest.GetScopes().ToArray();
                var requestId = Guid.NewGuid().ToString();

                // Cache the OIDC request for the callback
                _cache.Set($"oidc_request_{requestId}", oidcRequest, TimeSpan.FromMinutes(10));

                if (IsBrowserRequest())
                {
                    return HtmlContent(_htmlRenderer.RenderOAuthLoginForm(
                        requestId, clientId, scopes));
                }

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Data = new { requestId, clientId, scopes }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing authorize request");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Authorization error" });
            }
        }

        // ── POST /connect/authorize/callback ────────────────────────────

        [HttpPost("~/connect/authorize/callback")]
        [AllowAnonymous]
        public async Task<IActionResult> AuthorizeCallback([FromForm] AuthorizeCallbackRequest request)
        {
            try
            {
                var user = await _authService.AuthenticateAsync(request.Username, request.Password);
                if (user == null)
                {
                    return IsBrowserRequest()
                        ? Redirect($"/api/auth/error?message={Uri.EscapeDataString("Invalid credentials")}")
                        : Unauthorized(new ApiResponse<object> { Success = false, Message = "Invalid credentials" });
                }

                if (!_cache.TryGetValue($"oidc_request_{request.RequestId}", out OpenIddictRequest? oidcRequest) ||
                    oidcRequest == null)
                {
                    return BadRequest(new ApiResponse<object> { Success = false, Message = "Request expired" });
                }

                _cache.Remove($"oidc_request_{request.RequestId}");

                var scopes = oidcRequest.GetScopes().ToArray();
                var (idSuccess, identity, idError) =
                    await _openIdConnectService.CreateIdentityFromUserAsync(user, scopes);

                if (!idSuccess || identity == null)
                    return BadRequest(new ApiResponse<object> { Success = false, Message = idError ?? "Identity creation failed" });

                // Set destinations for each claim
                foreach (var claim in identity.Claims)
                {
                    claim.SetDestinations(_openIdConnectService.GetDestinations(claim));
                }

                var principal = new ClaimsPrincipal(identity);
                return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing authorize callback");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Callback error" });
            }
        }

        // ── POST /connect/token ─────────────────────────────────────────

        [HttpPost("~/connect/token")]
        [AllowAnonymous]
        public async Task<IActionResult> Exchange()
        {
            try
            {
                var oidcRequest = HttpContext.GetOpenIddictServerRequest()
                    ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

                if (oidcRequest.IsAuthorizationCodeGrantType() || oidcRequest.IsRefreshTokenGrantType())
                {
                    var result = await HttpContext.AuthenticateAsync(
                        OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

                    if (!result.Succeeded)
                        return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

                    var principal = result.Principal!;

                    foreach (var claim in principal.Claims)
                    {
                        claim.SetDestinations(_openIdConnectService.GetDestinations(claim));
                    }

                    return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
                }

                if (oidcRequest.IsClientCredentialsGrantType())
                {
                    var identity = new ClaimsIdentity(
                        OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

                    identity.AddClaim(Claims.Subject,
                        oidcRequest.ClientId ?? throw new InvalidOperationException("Client ID missing"));

                    var principal = new ClaimsPrincipal(identity);
                    principal.SetScopes(oidcRequest.GetScopes());

                    return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
                }

                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "The specified grant type is not supported."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during token exchange");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Token exchange error" });
            }
        }

        // ── GET /connect/userinfo ───────────────────────────────────────

        [HttpGet("~/connect/userinfo")]
        [Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
        public async Task<IActionResult> UserInfo()
        {
            try
            {
                var sub = User.FindFirst(Claims.Subject)?.Value;
                if (string.IsNullOrEmpty(sub))
                    return Unauthorized(new ApiResponse<object> { Success = false, Message = "Subject claim missing" });

                if (uint.TryParse(sub, out var userId))
                {
                    var user = await _userService.GetUserByIdAsync(userId);
                    if (user != null)
                    {
                        return Ok(new UserInfoResponse
                        {
                            Sub = sub,
                            Name = user.Login,
                            PreferredUsername = user.Login,
                            Email = user.Email,
                            PhoneNumber = user.PhoneNumber
                        });
                    }
                }

                return Ok(new UserInfoResponse
                {
                    Sub = sub,
                    Name = User.FindFirst(Claims.Name)?.Value ?? "",
                    PreferredUsername = User.FindFirst(Claims.PreferredUsername)?.Value ?? ""
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user info");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error getting user info" });
            }
        }

        // ── GET /api/auth/connect/scopes ────────────────────────────────

        [HttpGet("connect/scopes")]
        [AllowAnonymous]
        public async Task<IActionResult> GetScopes()
        {
            try
            {
                var scopeManager = _openIdConnectService.GetScopeManager();
                var scopes = new List<ScopeDto>();

                await foreach (var scope in scopeManager.ListAsync())
                {
                    var name = await scopeManager.GetNameAsync(scope);
                    var displayName = await scopeManager.GetDisplayNameAsync(scope);
                    var description = await scopeManager.GetDescriptionAsync(scope);
                    var id = await scopeManager.GetIdAsync(scope);

                    scopes.Add(new ScopeDto
                    {
                        Name = name ?? "",
                        DisplayName = displayName,
                        Description = description,
                        OidcId = id ?? ""
                    });
                }

                return Ok(new ApiResponse<GetScopesResponse>
                {
                    Success = true,
                    Data = new GetScopesResponse { Scopes = scopes }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting scopes");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error getting scopes" });
            }
        }

        // ── OIDC Client Management ──────────────────────────────────────

        [HttpPost("connect/registerclient")]
        [AllowAnonymous]
        public async Task<IActionResult> RegisterClient([FromBody] RegisterClientRequest request)
        {
            try
            {
                if (!IsAdmin())
                    return Forbid();

                var (success, errorMessage) = await _openIdConnectService.RegisterClientApplicationAsync(
                    request.ClientId, request.ClientSecret, request.DisplayName,
                    request.RedirectUris, request.PostLogoutRedirectUris,
                    request.AllowedScopes, request.RequireConsent);

                if (!success)
                    return BadRequest(new ApiResponse<object> { Success = false, Message = errorMessage ?? "Registration failed" });

                return Ok(new ApiResponse<RegisterClientResponse>
                {
                    Success = true,
                    Data = new RegisterClientResponse { ClientId = request.ClientId, DisplayName = request.DisplayName }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering OIDC client");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error registering client" });
            }
        }

        [HttpPut("connect/update-client/{clientId}")]
        [AllowAnonymous]
        public async Task<IActionResult> UpdateClient(string clientId, [FromBody] UpdateClientRequest request)
        {
            try
            {
                if (!IsAdmin())
                    return Forbid();

                var (success, errorMessage) = await _openIdConnectService.UpdateClientApplicationAsync(
                    clientId, request.ClientSecret, request.DisplayName,
                    request.RedirectUris, request.PostLogoutRedirectUris,
                    request.AllowedScopes, request.RequireConsent);

                if (!success)
                    return BadRequest(new ApiResponse<object> { Success = false, Message = errorMessage ?? "Update failed" });

                return Ok(new ApiResponse<UpdateClientResponse>
                {
                    Success = true,
                    Data = new UpdateClientResponse { ClientId = clientId, DisplayName = request.DisplayName ?? "" }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating OIDC client: {ClientId}", clientId);
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error updating client" });
            }
        }

        [HttpDelete("connect/delete-client/{clientId}")]
        [AllowAnonymous]
        public async Task<IActionResult> DeleteClient(string clientId)
        {
            try
            {
                if (!IsAdmin())
                    return Forbid();

                var (success, errorMessage) = await _openIdConnectService.DeleteClientApplicationAsync(clientId);

                if (!success)
                    return BadRequest(new ApiResponse<object> { Success = false, Message = errorMessage ?? "Deletion failed" });

                return Ok(new ApiResponse<DeleteClientResponse>
                {
                    Success = true,
                    Data = new DeleteClientResponse { ClientId = clientId, Deleted = true }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting OIDC client: {ClientId}", clientId);
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error deleting client" });
            }
        }

        [HttpGet("connect/client/{clientId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetClient(string clientId)
        {
            try
            {
                if (!IsAdmin())
                    return Forbid();

                var (success, application, errorMessage) =
                    await _openIdConnectService.GetClientApplicationAsync(clientId);

                if (!success || application == null)
                    return NotFound(new ApiResponse<object> { Success = false, Message = errorMessage ?? "Client not found" });

                return Ok(new ApiResponse<object> { Success = true, Data = application });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting OIDC client: {ClientId}", clientId);
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error getting client" });
            }
        }

        // ── OIDC Admin HTML Pages ───────────────────────────────────────

        [HttpGet("connect/clients")]
        [AllowAnonymous]
        public async Task<IActionResult> ClientsListPage()
        {
            if (!IsAdmin()) return Redirect("/api/auth/login?error=Admin+access+required");

            try
            {
                var (success, applications, _) = await _openIdConnectService.GetAllClientApplicationsAsync();
                var clients = new List<ClientViewModel>();

                // Map applications to view models (the service returns object for flexibility)
                if (success && applications != null)
                {
                    foreach (var app in applications)
                    {
                        // Dynamic access — depends on concrete OpenIddict model
                        clients.Add(new ClientViewModel
                        {
                            ClientId = (app as dynamic)?.ClientId?.ToString(),
                            DisplayName = (app as dynamic)?.DisplayName?.ToString()
                        });
                    }
                }

                return HtmlContent(_htmlRenderer.RenderClientsList(clients));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading clients list");
                return HtmlContent(_htmlRenderer.RenderError("Error loading clients list"));
            }
        }

        [HttpGet("connect/clients/new")]
        [AllowAnonymous]
        public IActionResult NewClientPage()
        {
            if (!IsAdmin()) return Redirect("/api/auth/login?error=Admin+access+required");
            return HtmlContent(_htmlRenderer.RenderClientForm());
        }

        [HttpGet("connect/clients/{clientId}/edit")]
        [AllowAnonymous]
        public async Task<IActionResult> EditClientPage(string clientId)
        {
            if (!IsAdmin()) return Redirect("/api/auth/login?error=Admin+access+required");

            try
            {
                var (success, application, _) = await _openIdConnectService.GetClientApplicationAsync(clientId);
                if (!success || application == null)
                    return HtmlContent(_htmlRenderer.RenderError("Client not found"));

                // Build the form model (implementation depends on concrete OpenIddict type)
                return HtmlContent(_htmlRenderer.RenderClientForm(new ClientFormViewModel
                {
                    ClientId = clientId,
                    IsEdit = true,
                    DisplayName = (application as dynamic)?.DisplayName?.ToString()
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading client edit page");
                return HtmlContent(_htmlRenderer.RenderError("Error loading client"));
            }
        }

        [HttpGet("connect/clients/{clientId}")]
        [AllowAnonymous]
        public async Task<IActionResult> ClientDetailPage(string clientId)
        {
            if (!IsAdmin()) return Redirect("/api/auth/login?error=Admin+access+required");

            try
            {
                var (success, application, _) = await _openIdConnectService.GetClientApplicationAsync(clientId);
                if (!success || application == null)
                    return HtmlContent(_htmlRenderer.RenderError("Client not found"));

                return HtmlContent(_htmlRenderer.RenderClientDetail(new ClientDetailViewModel
                {
                    ClientId = clientId,
                    DisplayName = (application as dynamic)?.DisplayName?.ToString()
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading client detail");
                return HtmlContent(_htmlRenderer.RenderError("Error loading client details"));
            }
        }

        // ── Browser form POSTs for OIDC admin ───────────────────────────

        [HttpPost("connect/register-client")]
        [AllowAnonymous]
        public async Task<IActionResult> RegisterClientForm([FromForm] RegisterClientFormRequest request)
        {
            if (!IsAdmin()) return Redirect("/api/auth/login?error=Admin+access+required");

            try
            {
                var redirectUris = request.RedirectUris?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>();
                var postLogoutUris = request.PostLogoutRedirectUris?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>();
                var scopes = request.AllowedScopes?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>();

                var (success, errorMessage) = await _openIdConnectService.RegisterClientApplicationAsync(
                    request.ClientId, request.ClientSecret, request.DisplayName,
                    redirectUris, postLogoutUris, scopes, request.RequireConsent);

                if (!success)
                    return HtmlContent(_htmlRenderer.RenderClientForm(new ClientFormViewModel
                    {
                        ClientId = request.ClientId,
                        DisplayName = request.DisplayName,
                        Error = errorMessage ?? "Registration failed"
                    }));

                return Redirect($"/api/auth/connect/clients/{request.ClientId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering client via form");
                return HtmlContent(_htmlRenderer.RenderClientForm(new ClientFormViewModel { Error = ex.Message }));
            }
        }

        [HttpPost("connect/update-client/{clientId}")]
        [AllowAnonymous]
        public async Task<IActionResult> UpdateClientForm(string clientId, [FromForm] UpdateClientFormRequest request)
        {
            if (!IsAdmin()) return Redirect("/api/auth/login?error=Admin+access+required");

            try
            {
                var redirectUris = request.RedirectUris?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var postLogoutUris = request.PostLogoutRedirectUris?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var scopes = request.AllowedScopes?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var (success, errorMessage) = await _openIdConnectService.UpdateClientApplicationAsync(
                    clientId, request.ClientSecret, request.DisplayName,
                    redirectUris, postLogoutUris, scopes, request.RequireConsent);

                if (!success)
                    return HtmlContent(_htmlRenderer.RenderClientForm(new ClientFormViewModel
                    {
                        ClientId = clientId,
                        IsEdit = true,
                        Error = errorMessage ?? "Update failed"
                    }));

                return Redirect($"/api/auth/connect/clients/{clientId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating client via form");
                return HtmlContent(_htmlRenderer.RenderClientForm(new ClientFormViewModel { ClientId = clientId, IsEdit = true, Error = ex.Message }));
            }
        }

        [HttpPost("connect/clients/{clientId}/delete")]
        [AllowAnonymous]
        public async Task<IActionResult> DeleteClientForm(string clientId)
        {
            if (!IsAdmin()) return Redirect("/api/auth/login?error=Admin+access+required");

            try
            {
                await _openIdConnectService.DeleteClientApplicationAsync(clientId);
                return Redirect("/api/auth/connect/clients");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting client via form");
                return HtmlContent(_htmlRenderer.RenderError("Error deleting client: " + ex.Message));
            }
        }
    }
}
