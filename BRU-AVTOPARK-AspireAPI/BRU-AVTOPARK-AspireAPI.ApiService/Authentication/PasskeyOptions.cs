using Microsoft.Extensions.Options;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Authentication;

public sealed class PasskeyOptions
{
    public bool Enabled { get; set; }
    public string PublicOrigin { get; set; } = "";
}
public sealed class PasskeyOptionsValidator(IConfiguration configuration, IHostEnvironment environment) : IValidateOptions<PasskeyOptions>
{
    public ValidateOptionsResult Validate(string? name, PasskeyOptions options)
    {
        if (!options.Enabled) return ValidateOptionsResult.Success;
        bool Origin(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.AbsolutePath == "/" &&
            (uri.Scheme == "https" || environment.IsDevelopment() && uri.Scheme == "http" && uri.IsLoopback);
        var origins = configuration.GetSection("Fido2:Origins").Get<string[]>() ?? [];
        var rp = configuration["Fido2:ServerDomain"];
        if (!Origin(options.PublicOrigin) || origins.Length == 0 || origins.Any(value => !Origin(value)) || string.IsNullOrWhiteSpace(rp))
            return ValidateOptionsResult.Fail("Passkeys require explicit Authentication:Passkeys:PublicOrigin, Fido2:ServerDomain and Fido2:Origins; origins must be HTTPS (Development loopback HTTP only).");
        var api = new Uri(options.PublicOrigin);
        if (!string.Equals(rp, api.IdnHost, StringComparison.OrdinalIgnoreCase) || !origins.Any(value => new Uri(value).GetLeftPart(UriPartial.Authority) == api.GetLeftPart(UriPartial.Authority)))
            return ValidateOptionsResult.Fail("Passkey RP id must equal the API public-origin host and Fido2 origins must contain that exact API origin.");
        return ValidateOptionsResult.Success;
    }
}
