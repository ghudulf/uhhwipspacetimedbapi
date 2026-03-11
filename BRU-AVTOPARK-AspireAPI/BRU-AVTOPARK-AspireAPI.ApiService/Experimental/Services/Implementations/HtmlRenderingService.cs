using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using BRU_AVTOPARK.Services.Interfaces;

namespace BRU_AVTOPARK.Experimental.Services.Implementations;

/// <summary>
/// Service for rendering Razor views to HTML strings.
/// </summary>
public class HtmlRenderingService : IHtmlRenderingService
{
    private readonly IRazorViewEngine _razorViewEngine;
    private readonly ITempDataProvider _tempDataProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<HtmlRenderingService> _logger;

    public HtmlRenderingService(
        IRazorViewEngine razorViewEngine,
        ITempDataProvider tempDataProvider,
        IServiceProvider serviceProvider,
        IHttpContextAccessor httpContextAccessor,
        ILogger<HtmlRenderingService> logger)
    {
        _razorViewEngine = razorViewEngine;
        _tempDataProvider = tempDataProvider;
        _serviceProvider = serviceProvider;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> RenderViewToStringAsync<TModel>(
        string viewName,
        TModel model,
        HttpContext httpContext)
    {
        // STEP 1: Create ActionContext with proper route data
        // This is critical for Razor to understand the controller/action context
        var routeData = httpContext.GetRouteData();
        
        // Extract controller and action from viewName (e.g., "Profile/Index" -> controller="Profile", action="Index")
        var viewParts = viewName.Split('/');
        if (viewParts.Length == 2)
        {
            routeData.Values["controller"] = viewParts[0];
            routeData.Values["action"] = viewParts[1];
        }
        else
        {
            // Single part view name (e.g., "Index") - use default controller
            routeData.Values["controller"] = "Auth";
            routeData.Values["action"] = viewParts[0];
        }

        var actionDescriptor = new ActionDescriptor
        {
            RouteValues = new Dictionary<string, string?>
            {
                ["controller"] = routeData.Values["controller"]?.ToString(),
                ["action"] = routeData.Values["action"]?.ToString()
            }
        };

        var actionContext = new ActionContext(
            httpContext,
            routeData,
            actionDescriptor);

        // STEP 2: Find the main view using our custom FindView method
        await using var sw = new StringWriter();
        var viewResult = FindView(actionContext, viewName);

        if (viewResult.View == null)
        {
            _logger.LogError("View {ViewName} not found. Searched locations: {SearchedLocations}",
                viewName,
                string.Join(", ", viewResult.SearchedLocations ?? Array.Empty<string>()));
            throw new ArgumentNullException($"View {viewName} not found");
        }

        // STEP 3: Create ViewDataDictionary with model
        var viewDictionary = new ViewDataDictionary<TModel>(
            new EmptyModelMetadataProvider(),
            new ModelStateDictionary())
        {
            Model = model
        };

        // STEP 4: Create ViewContext with ExecutingFilePath set
        // CRITICAL: ExecutingFilePath tells Razor where the current view is located
        // This is used by the Razor engine to resolve relative partial paths
        // When Razor sees <partial name="_Sidebar" />, it will:
        // 1. Look in the same directory as ExecutingFilePath
        // 2. Look in /Experimental/Views/Shared/ (configured in Program.cs ViewLocationFormats)
        // 3. Look in /Views/Shared/ (fallback)
        var viewContext = new ViewContext(
            actionContext,
            viewResult.View,
            viewDictionary,
            new TempDataDictionary(actionContext.HttpContext, _tempDataProvider),
            sw,
            new HtmlHelperOptions());

        // Set the executing file path to the actual view location
        // This is the KEY to making partial views work correctly
        viewContext.ExecutingFilePath = $"/Experimental/Views/{viewName}.cshtml";
        
        // STEP 5: Log the view context setup for debugging
        _logger.LogDebug("Rendering view: {ViewName}", viewName);
        _logger.LogDebug("ExecutingFilePath: {ExecutingFilePath}", viewContext.ExecutingFilePath);
        _logger.LogDebug("Controller: {Controller}, Action: {Action}", 
            routeData.Values["controller"], 
            routeData.Values["action"]);

        // STEP 6: Render the view
        await viewResult.View.RenderAsync(viewContext);
        return sw.ToString();
    }


    /// <inheritdoc />
    public async Task<string> RenderPartialViewToStringAsync<TModel>(
        string partialViewName,
        TModel model,
        HttpContext httpContext)
    {
        // STEP 1: Create ActionContext
        var routeData = httpContext.GetRouteData();
        var actionDescriptor = new ActionDescriptor();
        
        var actionContext = new ActionContext(
            httpContext,
            routeData,
            actionDescriptor);

        // STEP 2: Find the partial view
        // Try multiple strategies to find the partial view
        await using var sw = new StringWriter();
        ViewEngineResult viewResult;

        // Strategy 1: Try with full path
        if (partialViewName.StartsWith("~/") || partialViewName.StartsWith("/"))
        {
            viewResult = _razorViewEngine.GetView(
                executingFilePath: null,
                viewPath: partialViewName,
                isMainPage: false);
        }
        else
        {
            // Strategy 2: Try FindView (uses ViewLocationFormats)
            viewResult = _razorViewEngine.FindView(actionContext, partialViewName, isMainPage: false);
            
            // Strategy 3: If not found, try explicit Experimental/Views/Shared path
            if (!viewResult.Success)
            {
                var experimentalPath = $"~/Experimental/Views/Shared/{partialViewName}.cshtml";
                viewResult = _razorViewEngine.GetView(
                    executingFilePath: "~/Experimental/Views/",
                    viewPath: experimentalPath,
                    isMainPage: false);
            }
        }

        if (viewResult.View == null)
        {
            _logger.LogError("Partial view {PartialViewName} not found. Searched locations: {SearchedLocations}",
                partialViewName,
                string.Join(", ", viewResult.SearchedLocations ?? Array.Empty<string>()));
            throw new ArgumentNullException($"Partial view {partialViewName} not found");
        }

        // STEP 3: Create ViewDataDictionary
        var viewDictionary = new ViewDataDictionary<TModel>(
            new EmptyModelMetadataProvider(),
            new ModelStateDictionary())
        {
            Model = model
        };

        // STEP 4: Create ViewContext with ExecutingFilePath
        var viewContext = new ViewContext(
            actionContext,
            viewResult.View,
            viewDictionary,
            new TempDataDictionary(actionContext.HttpContext, _tempDataProvider),
            sw,
            new HtmlHelperOptions());

        // Set executing file path for nested partial resolution
        viewContext.ExecutingFilePath = $"/Experimental/Views/Shared/{partialViewName}.cshtml";

        _logger.LogDebug("Rendering partial view: {PartialViewName}", partialViewName);
        _logger.LogDebug("Partial ExecutingFilePath: {ExecutingFilePath}", viewContext.ExecutingFilePath);

        // STEP 5: Render the partial view
        await viewResult.View.RenderAsync(viewContext);
        return sw.ToString();
    }

    /// <summary>
    /// Finds a view in the Experimental folder first, then falls back to standard locations.
    /// This method implements a custom view resolution strategy that prioritizes the Experimental folder.
    /// </summary>
    /// <param name="actionContext">The action context containing route and HTTP context information</param>
    /// <param name="viewName">The view name (e.g., "Profile/Index" or "Auth/Login")</param>
    /// <returns>ViewEngineResult containing the found view or search locations</returns>
    private ViewEngineResult FindView(ActionContext actionContext, string viewName)
    {
        // STRATEGY 1: Try GetView with explicit path to Experimental folder
        // GetView() is used when you know the exact path to the view
        // This is the most direct way to find views in non-standard locations
        var experimentalViewPath = $"~/Experimental/Views/{viewName}.cshtml";
        var experimentalViewResult = _razorViewEngine.GetView(
            executingFilePath: "~/Experimental/Views/",
            viewPath: experimentalViewPath,
            isMainPage: true);

        if (experimentalViewResult.Success)
        {
            _logger.LogDebug("Found view using GetView: {ViewPath}", experimentalViewPath);
            return experimentalViewResult;
        }

        _logger.LogDebug("GetView failed for {ViewPath}. Searched: {SearchedLocations}",
            experimentalViewPath,
            string.Join(", ", experimentalViewResult.SearchedLocations ?? Array.Empty<string>()));

        // STRATEGY 2: Try FindView with action context
        // FindView() uses the configured ViewLocationFormats from Program.cs
        // This will search in all configured locations:
        // - /Experimental/Views/{controller}/{action}.cshtml
        // - /Experimental/Views/Shared/{action}.cshtml
        // - /Views/{controller}/{action}.cshtml
        // - /Views/Shared/{action}.cshtml
        var findViewResult = _razorViewEngine.FindView(actionContext, viewName, isMainPage: true);

        if (findViewResult.Success)
        {
            _logger.LogDebug("Found view using FindView: {ViewName}", viewName);
            return findViewResult;
        }

        _logger.LogWarning("FindView failed for {ViewName}. Searched: {SearchedLocations}",
            viewName,
            string.Join(", ", findViewResult.SearchedLocations ?? Array.Empty<string>()));

        // STRATEGY 3: If viewName contains a slash, try treating it as a path
        if (viewName.Contains('/'))
        {
            var pathViewResult = _razorViewEngine.GetView(
                executingFilePath: null,
                viewPath: $"~/Experimental/Views/{viewName}.cshtml",
                isMainPage: true);

            if (pathViewResult.Success)
            {
                _logger.LogDebug("Found view using path-based GetView: {ViewName}", viewName);
                return pathViewResult;
            }
        }

        // Return the last result (which contains all searched locations for error reporting)
        return findViewResult;
    }

    // Authentication view rendering methods
    public string RenderLoginForm(string? error = null, string? message = null)
    {
        var httpContext = _httpContextAccessor.HttpContext 
            ?? throw new InvalidOperationException("HttpContext is not available");
        
        var viewModel = new BRU_AVTOPARK.Models.ViewModels.LoginViewModel
        {
            Error = error,
            Message = message
        };
        return RenderViewToStringAsync("Auth/Login", viewModel, httpContext).GetAwaiter().GetResult();
    }

    public string RenderRegisterForm(string? error = null, string? message = null, int? adminCheckAttempt = null, bool isAdmin = false)
    {
        var httpContext = _httpContextAccessor.HttpContext 
            ?? throw new InvalidOperationException("HttpContext is not available");
        
        var viewModel = new BRU_AVTOPARK.Models.ViewModels.RegisterViewModel
        {
            Error = error,
            Message = message,
            AdminCheckAttempt = adminCheckAttempt,
            IsAdmin = isAdmin
        };
        return RenderViewToStringAsync("Auth/Register", viewModel, httpContext).GetAwaiter().GetResult();
    }

    public string RenderTotpSetup(string qrCodeUri, string secretKey)
    {
        var httpContext = _httpContextAccessor.HttpContext 
            ?? throw new InvalidOperationException("HttpContext is not available");
        
        var viewModel = new BRU_AVTOPARK.Models.ViewModels.TotpSetupViewModel
        {
            QrCodeUri = qrCodeUri,
            SecretKey = secretKey
        };
        return RenderViewToStringAsync("Auth/TotpSetup", viewModel, httpContext).GetAwaiter().GetResult();
    }

    public string RenderWebAuthnRegistration(string options)
    {
        var httpContext = _httpContextAccessor.HttpContext 
            ?? throw new InvalidOperationException("HttpContext is not available");
        
        var viewModel = new BRU_AVTOPARK.Models.ViewModels.WebAuthnRegistrationViewModel
        {
            OptionsJson = options
        };
        return RenderViewToStringAsync("Auth/WebAuthnRegister", viewModel, httpContext).GetAwaiter().GetResult();
    }

    public string RenderMagicLinkForm(string? error = null, string? message = null)
    {
        var httpContext = _httpContextAccessor.HttpContext 
            ?? throw new InvalidOperationException("HttpContext is not available");
        
        var viewModel = new BRU_AVTOPARK.Models.ViewModels.MagicLinkViewModel
        {
            Error = error,
            Message = message
        };
        return RenderViewToStringAsync("Auth/MagicLink", viewModel, httpContext).GetAwaiter().GetResult();
    }

    public string RenderQrLogin(string qrCode)
    {
        var httpContext = _httpContextAccessor.HttpContext 
            ?? throw new InvalidOperationException("HttpContext is not available");
        
        var viewModel = new BRU_AVTOPARK.Models.ViewModels.QrLoginViewModel
        {
            QrCodeBase64 = qrCode,
            DeviceId = Guid.NewGuid().ToString()
        };
        return RenderViewToStringAsync("Auth/QrLogin", viewModel, httpContext).GetAwaiter().GetResult();
    }

    public string RenderOAuthLoginForm(string requestId, string clientName, string[] scopes, string? error = null)
    {
        var httpContext = _httpContextAccessor.HttpContext 
            ?? throw new InvalidOperationException("HttpContext is not available");
        
        var viewModel = new BRU_AVTOPARK.Models.ViewModels.OAuthLoginViewModel
        {
            RequestId = requestId,
            ClientName = clientName,
            Scopes = scopes,
            Error = error
        };
        return RenderViewToStringAsync("Auth/OAuthLogin", viewModel, httpContext).GetAwaiter().GetResult();
    }

    public string RenderClaimAccountForm(string? error = null, string? message = null)
    {
        var httpContext = _httpContextAccessor.HttpContext 
            ?? throw new InvalidOperationException("HttpContext is not available");
        
        var viewModel = new BRU_AVTOPARK.Models.ViewModels.ClaimAccountViewModel
        {
            Error = error,
            Message = message
        };
        return RenderViewToStringAsync("Auth/ClaimAccount", viewModel, httpContext).GetAwaiter().GetResult();
    }

    public string RenderSuccessPage(string token)
    {
        var httpContext = _httpContextAccessor.HttpContext 
            ?? throw new InvalidOperationException("HttpContext is not available");
        
        var viewModel = new BRU_AVTOPARK.Models.ViewModels.SuccessViewModel
        {
            Token = token,
            Message = "Login successful!"
        };
        return RenderViewToStringAsync("Auth/Success", viewModel, httpContext).GetAwaiter().GetResult();
    }

    public string RenderErrorPage(string error)
    {
        var httpContext = _httpContextAccessor.HttpContext 
            ?? throw new InvalidOperationException("HttpContext is not available");
        
        var viewModel = new BRU_AVTOPARK.Models.ViewModels.ErrorViewModel
        {
            Error = error
        };
        return RenderViewToStringAsync("Auth/Error", viewModel, httpContext).GetAwaiter().GetResult();
    }

    public string RenderLogoutPage()
    {
        var httpContext = _httpContextAccessor.HttpContext 
            ?? throw new InvalidOperationException("HttpContext is not available");
        
        return RenderViewToStringAsync<object>("Auth/Logout", null, httpContext).GetAwaiter().GetResult();
    }

    public string RenderProfilePage(
        SpacetimeDB.Types.UserProfile user,
        bool totpEnabled,
        List<BRU_AVTOPARK.Models.Responses.WebAuthnCredentialDto> webAuthnCredentials,
        List<SpacetimeDB.Types.Role> roles,
        List<SpacetimeDB.Types.Permission> permissions)
    {
        var httpContext = _httpContextAccessor.HttpContext 
            ?? throw new InvalidOperationException("HttpContext is not available");
        
        // Convert SpacetimeDB types to view models
        var userViewModel = new BRU_AVTOPARK.Models.ViewModels.UserProfileViewModel
        {
            UserId = user.UserId.ToString(),
            LegacyUserId = user.LegacyUserId,
            Login = user.Login,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            EmailConfirmed = user.EmailConfirmed,
            PhoneNumberConfirmed = user.PhoneNumberConfirmed,
            IsActive = user.IsActive,
            Xuid = user.Xuid?.ToString()
        };

        var webAuthnCredentialViewModels = webAuthnCredentials.Select(c => 
            new BRU_AVTOPARK.Models.ViewModels.WebAuthnCredentialViewModel
            {
                Id = c.Id,
                CreatedAt = c.CreatedAt,
                IsActive = true // WebAuthnCredentialDto doesn't have IsActive, default to true
            }).ToList();

        var roleViewModels = roles.Select(r => 
            new BRU_AVTOPARK.Models.ViewModels.RoleViewModel
            {
                LegacyRoleId = r.LegacyRoleId,
                Name = r.Name,
                Priority = (int)r.Priority,
                IsActive = r.IsActive
            }).ToList();

        var permissionViewModels = permissions.Select(p => 
            new BRU_AVTOPARK.Models.ViewModels.PermissionViewModel
            {
                Name = p.Name,
                IsActive = p.IsActive
            }).ToList();

        var viewModel = new BRU_AVTOPARK.Models.ViewModels.ProfileViewModel
        {
            User = userViewModel,
            TotpEnabled = totpEnabled,
            WebAuthnEnabled = webAuthnCredentials.Count > 0,
            WebAuthnCredentials = webAuthnCredentialViewModels,
            Roles = roleViewModels,
            Permissions = permissionViewModels
        };

        return RenderViewToStringAsync("Profile/Index", viewModel, httpContext).GetAwaiter().GetResult();
    }

    public string RenderOidcClientsList(List<BRU_AVTOPARK.Models.Responses.ClientDto> clients, string? token = null)
    {
        var httpContext = _httpContextAccessor.HttpContext 
            ?? throw new InvalidOperationException("HttpContext is not available");
        
        var clientViewModels = clients.Select(c => 
            new BRU_AVTOPARK.Models.ViewModels.ClientViewModel
            {
                ClientId = c.ClientId ?? string.Empty,
                DisplayName = c.DisplayName
            }).ToList();

        var viewModel = new BRU_AVTOPARK.Models.ViewModels.OidcClientsListViewModel
        {
            Clients = clientViewModels,
            Token = token
        };

        return RenderViewToStringAsync("OAuth/ClientsList", viewModel, httpContext).GetAwaiter().GetResult();
    }

    public string RenderOidcScopesList(List<BRU_AVTOPARK.Models.Responses.ScopeDto> scopes, string? token = null)
    {
        var httpContext = _httpContextAccessor.HttpContext 
            ?? throw new InvalidOperationException("HttpContext is not available");
        
        var scopeViewModels = scopes.Select(s => 
            new BRU_AVTOPARK.Models.ViewModels.ScopeViewModel
            {
                Name = s.Name,
                DisplayName = s.DisplayName,
                Description = s.Description,
                OidcId = s.OidcId
            }).ToList();

        var viewModel = new BRU_AVTOPARK.Models.ViewModels.OidcScopesListViewModel
        {
            Scopes = scopeViewModels,
            Token = token
        };

        return RenderViewToStringAsync("OAuth/ScopesList", viewModel, httpContext).GetAwaiter().GetResult();
    }

    public string RenderOidcClientDetails(BRU_AVTOPARK.Models.Responses.GetClientResponse client, string? token = null)
    {
        var httpContext = _httpContextAccessor.HttpContext 
            ?? throw new InvalidOperationException("HttpContext is not available");
        
        var viewModel = new BRU_AVTOPARK.Models.ViewModels.OidcClientDetailsViewModel
        {
            ClientId = client.ClientId,
            DisplayName = client.DisplayName,
            RedirectUris = client.RedirectUris,
            PostLogoutRedirectUris = client.PostLogoutRedirectUris,
            AllowedScopes = client.AllowedScopes,
            RequireConsent = client.RequireConsent,
            Token = token
        };

        return RenderViewToStringAsync("OAuth/ClientDetails", viewModel, httpContext).GetAwaiter().GetResult();
    }

    public string RenderOidcClientForm(string? clientId = null, BRU_AVTOPARK.Models.Responses.GetClientResponse? client = null, string? token = null)
    {
        var httpContext = _httpContextAccessor.HttpContext 
            ?? throw new InvalidOperationException("HttpContext is not available");
        
        var viewModel = new BRU_AVTOPARK.Models.ViewModels.OidcClientFormViewModel
        {
            ClientId = clientId ?? client?.ClientId,
            DisplayName = client?.DisplayName,
            RedirectUris = client != null ? string.Join("\n", client.RedirectUris) : string.Empty,
            PostLogoutRedirectUris = client != null ? string.Join("\n", client.PostLogoutRedirectUris) : string.Empty,
            AllowedScopes = client != null ? string.Join(" ", client.AllowedScopes) : string.Empty,
            RequireConsent = client?.RequireConsent ?? false,
            Token = token
        };

        return RenderViewToStringAsync("OAuth/ClientForm", viewModel, httpContext).GetAwaiter().GetResult();
    }
}
