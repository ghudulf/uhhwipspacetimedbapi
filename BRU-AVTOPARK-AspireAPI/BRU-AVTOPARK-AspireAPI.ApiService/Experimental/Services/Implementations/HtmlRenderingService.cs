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
        var actionContext = new ActionContext(
            httpContext,
            httpContext.GetRouteData(),
            new ActionDescriptor());

        await using var sw = new StringWriter();
        var viewResult = FindView(actionContext, viewName);

        if (viewResult.View == null)
        {
            _logger.LogError("View {ViewName} not found. Searched locations: {SearchedLocations}",
                viewName,
                string.Join(", ", viewResult.SearchedLocations ?? Array.Empty<string>()));
            throw new ArgumentNullException($"View {viewName} not found");
        }

        var viewDictionary = new ViewDataDictionary<TModel>(
            new EmptyModelMetadataProvider(),
            new ModelStateDictionary())
        {
            Model = model
        };

        var viewContext = new ViewContext(
            actionContext,
            viewResult.View,
            viewDictionary,
            new TempDataDictionary(actionContext.HttpContext, _tempDataProvider),
            sw,
            new HtmlHelperOptions());

        await viewResult.View.RenderAsync(viewContext);
        return sw.ToString();
    }

    /// <inheritdoc />
    public async Task<string> RenderPartialViewToStringAsync<TModel>(
        string partialViewName,
        TModel model,
        HttpContext httpContext)
    {
        var actionContext = new ActionContext(
            httpContext,
            httpContext.GetRouteData(),
            new ActionDescriptor());

        await using var sw = new StringWriter();
        var viewResult = _razorViewEngine.FindView(actionContext, partialViewName, false);

        if (viewResult.View == null)
        {
            _logger.LogError("Partial view {PartialViewName} not found. Searched locations: {SearchedLocations}",
                partialViewName,
                string.Join(", ", viewResult.SearchedLocations ?? Array.Empty<string>()));
            throw new ArgumentNullException($"Partial view {partialViewName} not found");
        }

        var viewDictionary = new ViewDataDictionary<TModel>(
            new EmptyModelMetadataProvider(),
            new ModelStateDictionary())
        {
            Model = model
        };

        var viewContext = new ViewContext(
            actionContext,
            viewResult.View,
            viewDictionary,
            new TempDataDictionary(actionContext.HttpContext, _tempDataProvider),
            sw,
            new HtmlHelperOptions());

        await viewResult.View.RenderAsync(viewContext);
        return sw.ToString();
    }

    private ViewEngineResult FindView(ActionContext actionContext, string viewName)
    {
        // Try to find the view in the Experimental folder first
        var experimentalViewResult = _razorViewEngine.GetView(
            executingFilePath: "~/Experimental/Views/",
            viewPath: $"~/Experimental/Views/{viewName}.cshtml",
            isMainPage: true);

        if (experimentalViewResult.Success)
        {
            return experimentalViewResult;
        }

        // Fall back to standard view search
        return _razorViewEngine.FindView(actionContext, viewName, true);
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
