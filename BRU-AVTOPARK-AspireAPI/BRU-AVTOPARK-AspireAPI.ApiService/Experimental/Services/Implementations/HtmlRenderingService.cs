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
    private readonly ILogger<HtmlRenderingService> _logger;

    public HtmlRenderingService(
        IRazorViewEngine razorViewEngine,
        ITempDataProvider tempDataProvider,
        IServiceProvider serviceProvider,
        ILogger<HtmlRenderingService> logger)
    {
        _razorViewEngine = razorViewEngine;
        _tempDataProvider = tempDataProvider;
        _serviceProvider = serviceProvider;
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

    // Stub implementations for interface methods - to be implemented in Phase 2
    public string RenderLoginForm(string? error = null, string? message = null) => 
        throw new NotImplementedException("To be implemented in Phase 2");

    public string RenderTotpSetup(string qrCodeUri, string secretKey) => 
        throw new NotImplementedException("To be implemented in Phase 2");

    public string RenderWebAuthnRegistration(string options) => 
        throw new NotImplementedException("To be implemented in Phase 2");

    public string RenderMagicLinkForm(string? error = null, string? message = null) => 
        throw new NotImplementedException("To be implemented in Phase 2");

    public string RenderQrLogin(string qrCode) => 
        throw new NotImplementedException("To be implemented in Phase 2");

    public string RenderOAuthLoginForm(string requestId, string clientName, string[] scopes, string? error = null) => 
        throw new NotImplementedException("To be implemented in Phase 2");

    public string RenderSuccessPage(string token) => 
        throw new NotImplementedException("To be implemented in Phase 2");

    public string RenderErrorPage(string error) => 
        throw new NotImplementedException("To be implemented in Phase 2");

    public string RenderRegisterForm(string? error = null, string? message = null, int? adminCheckAttempt = null, bool isAdmin = false) => 
        throw new NotImplementedException("To be implemented in Phase 2");

    public string RenderClaimAccountForm(string? error = null, string? message = null) => 
        throw new NotImplementedException("To be implemented in Phase 2");

    public string RenderProfilePage(
        SpacetimeDB.Types.UserProfile user,
        bool totpEnabled,
        List<BRU_AVTOPARK.Models.Responses.WebAuthnCredentialDto> webAuthnCredentials,
        List<SpacetimeDB.Types.Role> roles,
        List<SpacetimeDB.Types.Permission> permissions) => 
        throw new NotImplementedException("To be implemented in Phase 2");

    public string RenderOidcClientsList(List<BRU_AVTOPARK.Models.Responses.ClientDto> clients, string? token = null) => 
        throw new NotImplementedException("To be implemented in Phase 2");

    public string RenderOidcScopesList(List<BRU_AVTOPARK.Models.Responses.ScopeDto> scopes, string? token = null) => 
        throw new NotImplementedException("To be implemented in Phase 2");

    public string RenderOidcClientDetails(BRU_AVTOPARK.Models.Responses.GetClientResponse client, string? token = null) => 
        throw new NotImplementedException("To be implemented in Phase 2");

    public string RenderOidcClientForm(string? clientId = null, BRU_AVTOPARK.Models.Responses.GetClientResponse? client = null, string? token = null) => 
        throw new NotImplementedException("To be implemented in Phase 2");
}
