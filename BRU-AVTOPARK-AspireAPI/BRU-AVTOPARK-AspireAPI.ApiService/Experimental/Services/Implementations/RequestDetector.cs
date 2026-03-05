using BRU_AVTOPARK.Services.Interfaces;
using Microsoft.AspNetCore.Http;

namespace BRU_AVTOPARK.Services.Implementations;

/// <summary>
/// Detects whether the current HTTP request expects HTML (browser) or JSON (API client).
/// Extracted from the original AuthController.IsBrowserRequest() method.
/// </summary>
public sealed class RequestDetector : IRequestDetector
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RequestDetector(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    /// <inheritdoc />
    public bool IsBrowserRequest()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context is null) return false;

        var accept = context.Request.Headers.Accept.ToString().ToLower();
        return accept.Contains("text/html") || accept.Contains("application/xhtml+xml");
    }
}

