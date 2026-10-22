using OpenIddict.Validation;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Authentication;

/// <summary>Validate encrypted access tokens inside the process; request Host never selects a credential destination.</summary>
public static class LocalTokenValidation
{
    public static async Task<Dictionary<string, object>> ValidateAsync(IServiceProvider services, string token, CancellationToken cancellationToken)
    {
        var validator = services.GetRequiredService<OpenIddictValidationService>();
        var principal = await validator.ValidateAccessTokenAsync(token, cancellationToken);
        return principal.Claims.GroupBy(c => c.Type).ToDictionary(g => g.Key,
            g => g.Count() == 1 ? (object)g.First().Value : g.Select(c => c.Value).ToArray(), StringComparer.Ordinal);
    }
}
