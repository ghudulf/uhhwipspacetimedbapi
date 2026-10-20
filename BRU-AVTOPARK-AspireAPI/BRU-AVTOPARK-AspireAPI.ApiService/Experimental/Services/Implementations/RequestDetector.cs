using BRU_AVTOPARK.Services.Interfaces;
using Microsoft.AspNetCore.Http;

namespace BRU_AVTOPARK.Services.Implementations;

/// <summary>
/// Detects whether an HTTP request expects HTML (browser) or JSON (API client)
/// by inspecting the Accept header. This is the single decision point for
/// content negotiation across all dual-mode endpoints.
///
/// Rules (in priority order):
///   1. Accept: application/json  → API client  → false
///   2. Accept: text/html         → browser     → true
///   3. Accept: application/xhtml+xml → browser → true
///   4. Accept: */*               → browser     → true  (backward compat)
///   5. Accept header missing     → browser     → true  (backward compat)
/// </summary>
public class RequestDetector : IRequestDetector
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
        if (context is null) return true; // default to HTML when no context
        return IsBrowserRequest(context);
    }

    /// <inheritdoc />
    public bool IsBrowserRequest(HttpContext context)
    {
        var accept = context.Request.Headers.Accept.ToString();

        // Missing or empty Accept header → treat as browser (backward compatibility)
        if (string.IsNullOrWhiteSpace(accept))
            return true;

        var lower = accept.ToLowerInvariant();

        // Explicit JSON request → API client
        if (lower.Contains("application/json"))
            return false;

        // Explicit HTML or XHTML request → browser
        if (lower.Contains("text/html") || lower.Contains("application/xhtml+xml"))
            return true;

        // Wildcard */* → treat as browser (backward compatibility; browsers send this)
        if (lower.Contains("*/*"))
            return true;

        // Any other Accept value (e.g. application/xml, image/*) → API client
        return false;
    }
}
