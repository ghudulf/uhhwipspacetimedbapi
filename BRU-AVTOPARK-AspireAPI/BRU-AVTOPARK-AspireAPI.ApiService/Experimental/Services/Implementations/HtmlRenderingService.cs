using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace BRU_AVTOPARK.Experimental.Services.Implementations;

/// <summary>
/// Service for rendering Razor views to HTML strings.
/// </summary>
public sealed class HtmlRenderingService : IHtmlRenderingService
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
}
