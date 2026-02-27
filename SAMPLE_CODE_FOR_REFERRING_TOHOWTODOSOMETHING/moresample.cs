/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/openiddict/openiddict-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Aridka.Server.Helpers;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Aridka.Server.Controllers;

public class AuthorizationController : Controller
{
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictScopeManager _scopeManager;

    public AuthorizationController(IOpenIddictApplicationManager applicationManager, IOpenIddictScopeManager scopeManager)
    {
        _applicationManager = applicationManager;
        _scopeManager = scopeManager;
    }

    [HttpPost("~/connect/token"), IgnoreAntiforgeryToken, Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest();
        if (request.IsClientCredentialsGrantType())
        {
            // Note: the client credentials are automatically validated by OpenIddict:
            // if client_id or client_secret are invalid, this action won't be invoked.

            var application = await _applicationManager.FindByClientIdAsync(request.ClientId);
            if (application == null)
            {
                throw new InvalidOperationException("The application details cannot be found in the database.");
            }

            // Create the claims-based identity that will be used by OpenIddict to generate tokens.
            var identity = new ClaimsIdentity(
                authenticationType: TokenValidationParameters.DefaultAuthenticationType,
                nameType: Claims.Name,
                roleType: Claims.Role);

            // Add the claims that will be persisted in the tokens (use the client_id as the subject identifier).
            identity.SetClaim(Claims.Subject, await _applicationManager.GetClientIdAsync(application));
            identity.SetClaim(Claims.Name, await _applicationManager.GetDisplayNameAsync(application));

            // Note: In the original OAuth 2.0 specification, the client credentials grant
            // doesn't return an identity token, which is an OpenID Connect concept.
            //
            // As a non-standardized extension, OpenIddict allows returning an id_token
            // to convey information about the client application when the "openid" scope
            // is granted (i.e specified when calling principal.SetScopes()). When the "openid"
            // scope is not explicitly set, no identity token is returned to the client application.

            // Set the list of scopes granted to the client application in access_token.
            identity.SetScopes(request.GetScopes());
            identity.SetResources(await _scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());
            identity.SetDestinations(GetDestinations);

            return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        throw new NotImplementedException("The specified grant type is not implemented.");
    }

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        // Note: by default, claims are NOT automatically included in the access and identity tokens.
        // To allow OpenIddict to serialize them, you must attach them a destination, that specifies
        // whether they should be included in access tokens, in identity tokens or in both.

        return claim.Type switch
        {
            Claims.Name or Claims.Subject => [Destinations.AccessToken, Destinations.IdentityToken],

            _ => [Destinations.AccessToken],
        };
    }
}
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Aridka.Server;

public static class Program
{
    public static void Main(string[] args) =>
        CreateHostBuilder(args).Build().Run();

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(builder => builder.UseStartup<Startup>());
}
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Dantooine.Server.Data;
using Dantooine.Server.Helpers;
using Dantooine.Server.ViewModels.Authorization;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Dantooine.Server.Controllers;

public class AuthorizationController : Controller
{
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictAuthorizationManager _authorizationManager;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public AuthorizationController(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictAuthorizationManager authorizationManager,
        IOpenIddictScopeManager scopeManager,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        _applicationManager = applicationManager;
        _authorizationManager = authorizationManager;
        _scopeManager = scopeManager;
        _signInManager = signInManager;
        _userManager = userManager;
    }

    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // Try to retrieve the user principal stored in the authentication cookie and redirect
        // the user agent to the login page (or to an external provider) in the following cases:
        //
        //  - If the user principal can't be extracted or the cookie is too old.
        //  - If prompt=login was specified by the client application.
        //  - If a max_age parameter was provided and the authentication cookie is not considered "fresh" enough.
        //
        // For scenarios where the default authentication handler configured in the ASP.NET Core
        // authentication options shouldn't be used, a specific scheme can be specified here.
        var result = await HttpContext.AuthenticateAsync();
        if (result == null || !result.Succeeded || request.HasPromptValue(PromptValues.Login) ||
           (request.MaxAge != null && result.Properties?.IssuedUtc != null &&
            DateTimeOffset.UtcNow - result.Properties.IssuedUtc > TimeSpan.FromSeconds(request.MaxAge.Value)))
        {
            // If the client application requested promptless authentication,
            // return an error indicating that the user is not logged in.
            if (request.HasPromptValue(PromptValues.None))
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.LoginRequired,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user is not logged in."
                    }));
            }

            // To avoid endless login -> authorization redirects, the prompt=login flag
            // is removed from the authorization request payload before redirecting the user.
            var prompt = string.Join(" ", request.GetPromptValues().Remove(PromptValues.Login));

            var parameters = Request.HasFormContentType ?
                Request.Form.Where(parameter => parameter.Key != Parameters.Prompt).ToList() :
                Request.Query.Where(parameter => parameter.Key != Parameters.Prompt).ToList();

            parameters.Add(KeyValuePair.Create(Parameters.Prompt, new StringValues(prompt)));

            // For scenarios where the default challenge handler configured in the ASP.NET Core
            // authentication options shouldn't be used, a specific scheme can be specified here.
            return Challenge(new AuthenticationProperties
            {
                RedirectUri = Request.PathBase + Request.Path + QueryString.Create(parameters)
            });
        }

        // Retrieve the profile of the logged in user.
        var user = await _userManager.GetUserAsync(result.Principal) ??
            throw new InvalidOperationException("The user details cannot be retrieved.");

        // Retrieve the application details from the database.
        var application = await _applicationManager.FindByClientIdAsync(request.ClientId) ??
            throw new InvalidOperationException("Details concerning the calling client application cannot be found.");

        // Retrieve the permanent authorizations associated with the user and the calling client application.
        var authorizations = await _authorizationManager.FindAsync(
            subject: await _userManager.GetUserIdAsync(user),
            client : await _applicationManager.GetIdAsync(application),
            status : Statuses.Valid,
            type   : AuthorizationTypes.Permanent,
            scopes : request.GetScopes()).ToListAsync();

        switch (await _applicationManager.GetConsentTypeAsync(application))
        {
            // If the consent is external (e.g when authorizations are granted by a sysadmin),
            // immediately return an error if no authorization can be found in the database.
            case ConsentTypes.External when authorizations.Count is 0:
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.ConsentRequired,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "The logged in user is not allowed to access this client application."
                    }));

            // If the consent is implicit or if an authorization was found,
            // return an authorization response without displaying the consent form.
            case ConsentTypes.Implicit:
            case ConsentTypes.External when authorizations.Count is not 0:
            case ConsentTypes.Explicit when authorizations.Count is not 0 && !request.HasPromptValue(PromptValues.Consent):
                // Create the claims-based identity that will be used by OpenIddict to generate tokens.
                var identity = new ClaimsIdentity(
                    authenticationType: TokenValidationParameters.DefaultAuthenticationType,
                    nameType: Claims.Name,
                    roleType: Claims.Role);

                // Add the claims that will be persisted in the tokens.
                identity.SetClaim(Claims.Subject, await _userManager.GetUserIdAsync(user))
                        .SetClaim(Claims.Email, await _userManager.GetEmailAsync(user))
                        .SetClaim(Claims.Name, await _userManager.GetUserNameAsync(user))
                        .SetClaim(Claims.PreferredUsername, await _userManager.GetUserNameAsync(user))
                        .SetClaims(Claims.Role, [.. (await _userManager.GetRolesAsync(user))]);

                // Note: in this sample, the granted scopes match the requested scope
                // but you may want to allow the user to uncheck specific scopes.
                // For that, simply restrict the list of scopes before calling SetScopes.
                identity.SetScopes(request.GetScopes());
                identity.SetResources(await _scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());

                // Automatically create a permanent authorization to avoid requiring explicit consent
                // for future authorization or token requests containing the same scopes.
                var authorization = authorizations.LastOrDefault();
                authorization ??= await _authorizationManager.CreateAsync(
                    identity: identity,
                    subject : await _userManager.GetUserIdAsync(user),
                    client  : await _applicationManager.GetIdAsync(application),
                    type    : AuthorizationTypes.Permanent,
                    scopes  : identity.GetScopes());

                identity.SetAuthorizationId(await _authorizationManager.GetIdAsync(authorization));
                identity.SetDestinations(GetDestinations);

                return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            // At this point, no authorization was found in the database and an error must be returned
            // if the client application specified prompt=none in the authorization request.
            case ConsentTypes.Explicit   when request.HasPromptValue(PromptValues.None):
            case ConsentTypes.Systematic when request.HasPromptValue(PromptValues.None):
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.ConsentRequired,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "Interactive user consent is required."
                    }));

            // In every other case, render the consent form.
            default: return View(new AuthorizeViewModel
            {
                ApplicationName = await _applicationManager.GetLocalizedDisplayNameAsync(application),
                Scope = request.Scope
            });
        }
    }

    [Authorize, FormValueRequired("submit.Accept")]
    [HttpPost("~/connect/authorize"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // Retrieve the profile of the logged in user.
        var user = await _userManager.GetUserAsync(User) ??
            throw new InvalidOperationException("The user details cannot be retrieved.");

        // Retrieve the application details from the database.
        var application = await _applicationManager.FindByClientIdAsync(request.ClientId) ??
            throw new InvalidOperationException("Details concerning the calling client application cannot be found.");

        // Retrieve the permanent authorizations associated with the user and the calling client application.
        var authorizations = await _authorizationManager.FindAsync(
            subject: await _userManager.GetUserIdAsync(user),
            client : await _applicationManager.GetIdAsync(application),
            status : Statuses.Valid,
            type   : AuthorizationTypes.Permanent,
            scopes : request.GetScopes()).ToListAsync();

        // Note: the same check is already made in the other action but is repeated
        // here to ensure a malicious user can't abuse this POST-only endpoint and
        // force it to return a valid response without the external authorization.
        if (authorizations.Count is 0 && await _applicationManager.HasConsentTypeAsync(application, ConsentTypes.External))
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.ConsentRequired,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                        "The logged in user is not allowed to access this client application."
                }));
        }

        // Create the claims-based identity that will be used by OpenIddict to generate tokens.
        var identity = new ClaimsIdentity(
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);

        // Add the claims that will be persisted in the tokens.
        identity.SetClaim(Claims.Subject, await _userManager.GetUserIdAsync(user))
                .SetClaim(Claims.Email, await _userManager.GetEmailAsync(user))
                .SetClaim(Claims.Name, await _userManager.GetUserNameAsync(user))
                .SetClaim(Claims.PreferredUsername, await _userManager.GetUserNameAsync(user))
                .SetClaims(Claims.Role, [.. (await _userManager.GetRolesAsync(user))]);

        // Note: in this sample, the granted scopes match the requested scope
        // but you may want to allow the user to uncheck specific scopes.
        // For that, simply restrict the list of scopes before calling SetScopes.
        identity.SetScopes(request.GetScopes());
        identity.SetResources(await _scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());

        // Automatically create a permanent authorization to avoid requiring explicit consent
        // for future authorization or token requests containing the same scopes.
        var authorization = authorizations.LastOrDefault();
        authorization ??= await _authorizationManager.CreateAsync(
            identity: identity,
            subject : await _userManager.GetUserIdAsync(user),
            client  : await _applicationManager.GetIdAsync(application),
            type    : AuthorizationTypes.Permanent,
            scopes  : identity.GetScopes());

        identity.SetAuthorizationId(await _authorizationManager.GetIdAsync(authorization));
        identity.SetDestinations(GetDestinations);

        // Returning a SignInResult will ask OpenIddict to issue the appropriate access/identity tokens.
        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [Authorize, FormValueRequired("submit.Deny")]
    [HttpPost("~/connect/authorize"), ValidateAntiForgeryToken]
    // Notify OpenIddict that the authorization grant has been denied by the resource owner
    // to redirect the user agent to the client application using the appropriate response_mode.
    public IActionResult Deny() => Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

    [HttpGet("~/connect/logout")]
    public IActionResult Logout() => View();

    [ActionName(nameof(Logout)), HttpPost("~/connect/logout"), ValidateAntiForgeryToken]
    public async Task<IActionResult> LogoutPost()
    {
        // Ask ASP.NET Core Identity to delete the local and external cookies created
        // when the user agent is redirected from the external identity provider
        // after a successful authentication flow (e.g Google or Facebook).
        await _signInManager.SignOutAsync();

        // Returning a SignOutResult will ask OpenIddict to redirect the user agent
        // to the post_logout_redirect_uri specified by the client application or to
        // the RedirectUri specified in the authentication properties if none was set.
        return SignOut(
            authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            properties: new AuthenticationProperties
            {
                RedirectUri = "/"
            });
    }

    [HttpPost("~/connect/token"), IgnoreAntiforgeryToken, Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            // Retrieve the claims principal stored in the authorization code/refresh token.
            var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            // Retrieve the user profile corresponding to the authorization code/refresh token.
            var user = await _userManager.FindByIdAsync(result.Principal.GetClaim(Claims.Subject));
            if (user is null)
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The token is no longer valid."
                    }));
            }

            // Ensure the user is still allowed to sign in.
            if (!await _signInManager.CanSignInAsync(user))
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user is no longer allowed to sign in."
                    }));
            }

            var identity = new ClaimsIdentity(result.Principal.Claims,
                authenticationType: TokenValidationParameters.DefaultAuthenticationType,
                nameType: Claims.Name,
                roleType: Claims.Role);

            // Override the user claims present in the principal in case they
            // changed since the authorization code/refresh token was issued.
            identity.SetClaim(Claims.Subject, await _userManager.GetUserIdAsync(user))
                    .SetClaim(Claims.Email, await _userManager.GetEmailAsync(user))
                    .SetClaim(Claims.Name, await _userManager.GetUserNameAsync(user))
                    .SetClaim(Claims.PreferredUsername, await _userManager.GetUserNameAsync(user))
                    .SetClaims(Claims.Role, [.. (await _userManager.GetRolesAsync(user))]);

            identity.SetDestinations(GetDestinations);

            // Returning a SignInResult will ask OpenIddict to issue the appropriate access/identity tokens.
            return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        throw new InvalidOperationException("The specified grant type is not supported.");
    }

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        // Note: by default, claims are NOT automatically included in the access and identity tokens.
        // To allow OpenIddict to serialize them, you must attach them a destination, that specifies
        // whether they should be included in access tokens, in identity tokens or in both.

        switch (claim.Type)
        {
            case Claims.Name or Claims.PreferredUsername:
                yield return Destinations.AccessToken;

                if (claim.Subject.HasScope(Scopes.Profile))
                    yield return Destinations.IdentityToken;

                yield break;

            case Claims.Email:
                yield return Destinations.AccessToken;

                if (claim.Subject.HasScope(Scopes.Email))
                    yield return Destinations.IdentityToken;

                yield break;

            case Claims.Role:
                yield return Destinations.AccessToken;

                if (claim.Subject.HasScope(Scopes.Roles))
                    yield return Destinations.IdentityToken;

                yield break;

            // Never include the security stamp in the access and identity tokens, as it's a secret value.
            case "AspNet.Identity.SecurityStamp": yield break;

            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}
@using Microsoft.Extensions.Primitives
@model AuthorizeViewModel

<div class="jumbotron">
    <h1>Authorization</h1>

    <p class="lead text-left">Do you want to grant <strong>@Model.ApplicationName</strong> access to your data? (scopes requested: @Model.Scope)</p>

    <form asp-controller="Authorization" asp-action="Authorize" method="post">
        @* Flow the request parameters so they can be received by the Accept/Reject actions: *@
        @foreach (var parameter in Context.Request.HasFormContentType ?
            (IEnumerable<KeyValuePair<string, StringValues>>) Context.Request.Form : Context.Request.Query)
        {
            <input type="hidden" name="@parameter.Key" value="@parameter.Value" />
        }

        <input class="btn btn-lg btn-success" name="submit.Accept" type="submit" value="Yes" />
        <input class="btn btn-lg btn-danger" name="submit.Deny" type="submit" value="No" />
    </form>
</div>
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using Dantooine.Server.Data;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Dantooine.Server;

public class Startup
{
    public Startup(IConfiguration configuration)
        => Configuration = configuration;

    public IConfiguration Configuration { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllersWithViews();
        services.AddRazorPages();

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            // Configure the context to use sqlite.
            options.UseSqlite($"Filename={Path.Combine(Path.GetTempPath(), "openiddict-dantooine-server.sqlite3")}");

            // Register the entity sets needed by OpenIddict.
            // Note: use the generic overload if you need
            // to replace the default OpenIddict entities.
            options.UseOpenIddict();
        });

        services.AddDatabaseDeveloperPageExceptionFilter();

        // Register the Identity services.
        services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders()
            .AddDefaultUI();

        // OpenIddict offers native integration with Quartz.NET to perform scheduled tasks
        // (like pruning orphaned authorizations/tokens from the database) at regular intervals.
        services.AddQuartz(options =>
        {
            options.UseSimpleTypeLoader();
            options.UseInMemoryStore();
        });

        // Register the Quartz.NET service and configure it to block shutdown until jobs are complete.
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        services.AddOpenIddict()

            // Register the OpenIddict core components.
            .AddCore(options =>
            {
                // Configure OpenIddict to use the Entity Framework Core stores and models.
                // Note: call ReplaceDefaultEntities() to replace the default OpenIddict entities.
                options.UseEntityFrameworkCore()
                       .UseDbContext<ApplicationDbContext>();

                // Enable Quartz.NET integration.
                options.UseQuartz();
            })

            // Register the OpenIddict server components.
            .AddServer(options =>
            {
                // Enable the authorization, logout, token and userinfo endpoints.
                options.SetAuthorizationEndpointUris("connect/authorize")
                       .SetEndSessionEndpointUris("connect/logout")
                       .SetIntrospectionEndpointUris("connect/introspect")
                       .SetTokenEndpointUris("connect/token")
                       .SetUserInfoEndpointUris("connect/userinfo")
                       .SetEndUserVerificationEndpointUris("connect/verify");

                // Mark the "email", "profile" and "roles" scopes as supported scopes.
                options.RegisterScopes(Scopes.Email, Scopes.Profile, Scopes.Roles);

                // Note: this sample only uses the authorization code and refresh token
                // flows but you can enable the other flows if you need to support
                // implicit, password or client credentials.
                options.AllowAuthorizationCodeFlow()
                       .AllowRefreshTokenFlow();

                // Register the signing and encryption credentials.
                options.AddDevelopmentEncryptionCertificate()
                       .AddDevelopmentSigningCertificate();

                // Register the ASP.NET Core host and configure the ASP.NET Core-specific options.
                options.UseAspNetCore()
                       .EnableAuthorizationEndpointPassthrough()
                       .EnableEndSessionEndpointPassthrough()
                       .EnableTokenEndpointPassthrough()
                       .EnableUserInfoEndpointPassthrough()
                       .EnableStatusCodePagesIntegration();
            })

            // Register the OpenIddict validation components.
            .AddValidation(options =>
            {
                // Import the configuration from the local OpenIddict server instance.
                options.UseLocalServer();

                // Register the ASP.NET Core host.
                options.UseAspNetCore();
            });

        // Register the worker responsible for seeding the database.
        // Note: in a real world application, this step should be part of a setup script.
        services.AddHostedService<Worker>();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.UseMigrationsEndPoint();
        }
        else
        {
            app.UseStatusCodePagesWithReExecute("~/error");
            //app.UseExceptionHandler("~/error");

            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            //app.UseHsts();
        }
        app.UseHttpsRedirection();
        app.UseStaticFiles();

        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
            endpoints.MapDefaultControllerRoute();
            endpoints.MapRazorPages();
        });
    }
}
/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/openiddict/openiddict-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Velusia.Server.Controllers;

public class AuthorizationController : Controller
{
    private readonly IOpenIddictApplicationManager _applicationManager;

    public AuthorizationController(IOpenIddictApplicationManager applicationManager)
        => _applicationManager = applicationManager;

    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // Retrieve the Windows identity associated with the current authorization request.
        // If it can't be extracted, trigger an Integrated Windows Authentication dance.
        var result = await HttpContext.AuthenticateAsync(NegotiateDefaults.AuthenticationScheme);
        if (result is not { Succeeded: true })
        {
            return Challenge(
                authenticationSchemes: NegotiateDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + QueryString.Create(
                        Request.HasFormContentType ? [.. Request.Form] : [.. Request.Query])
                });
        }

        // Retrieve the application details from the database.
        var application = await _applicationManager.FindByClientIdAsync(request.ClientId) ??
            throw new InvalidOperationException("Details concerning the calling client application cannot be found.");

        // This sample doesn't include a consent view mechanism and requires that the application use implicit consents.
        if (!await _applicationManager.HasConsentTypeAsync(application, ConsentTypes.Implicit))
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.ServerError,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                        "The specified client application is not correctly configured."
                }));
        }

        // Create the claims-based identity that will be used by OpenIddict to generate tokens.
        var identity = new ClaimsIdentity(result.Principal.Claims,
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);

        // The Windows identity doesn't contain the "sub" claim required by OpenIddict to represent
        // a stable identifier of the authenticated user. To work around that, a "sub" claim is
        // manually created by using the primary SID claim resolved from the Windows identity.
        var sid = identity.FindFirst(ClaimTypes.PrimarySid)?.Value;
        identity.AddClaim(new Claim(Claims.Subject, sid));

        // Allow all the claims resolved from the principal to be copied to the access and identity tokens.
        identity.SetDestinations(claim => [Destinations.AccessToken, Destinations.IdentityToken]);

        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
}
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Weytta.Server;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllersWithViews();

        services.AddDbContext<DbContext>(options =>
        {
            // Configure the context to use sqlite.
            options.UseSqlite($"Filename={Path.Combine(Path.GetTempPath(), "openiddict-weytta-server.sqlite3")}");

            // Register the entity sets needed by OpenIddict.
            // Note: use the generic overload if you need
            // to replace the default OpenIddict entities.
            options.UseOpenIddict();
        });

        // OpenIddict offers native integration with Quartz.NET to perform scheduled tasks
        // (like pruning orphaned authorizations/tokens from the database) at regular intervals.
        services.AddQuartz(options =>
        {
            options.UseSimpleTypeLoader();
            options.UseInMemoryStore();
        });

        // Register the Quartz.NET service and configure it to block shutdown until jobs are complete.
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        // Register the Negotiate handler (when running on IIS, it will automatically
        // delegate the actual Integrated Windows Authentication process to IIS).
        services.AddAuthentication()
            .AddNegotiate();

        services.AddOpenIddict()

            // Register the OpenIddict core components.
            .AddCore(options =>
            {
                // Configure OpenIddict to use the Entity Framework Core stores and models.
                // Note: call ReplaceDefaultEntities() to replace the default OpenIddict entities.
                options.UseEntityFrameworkCore()
                       .UseDbContext<DbContext>();

                // Enable Quartz.NET integration.
                options.UseQuartz();
            })

            // Register the OpenIddict server components.
            .AddServer(options =>
            {
                // Enable the authorization and token endpoints.
                options.SetAuthorizationEndpointUris("connect/authorize")
                       .SetTokenEndpointUris("connect/token");

                // Mark the "email", "profile" and "roles" scopes as supported scopes.
                options.RegisterScopes(Scopes.Email, Scopes.Profile, Scopes.Roles);

                // Note: this sample only uses the authorization code flow but you can enable
                // the other flows if you need to support implicit, password or client credentials.
                options.AllowAuthorizationCodeFlow();

                // Register the signing and encryption credentials.
                options.AddDevelopmentEncryptionCertificate()
                       .AddDevelopmentSigningCertificate();

                // Register the ASP.NET Core host and configure the ASP.NET Core-specific options.
                //
                // Note: unlike other samples, this sample doesn't use token endpoint pass-through
                // to handle token requests in a custom MVC action. As such, the token requests
                // will be automatically handled by OpenIddict, that will reuse the identity
                // resolved from the authorization code to produce access and identity tokens.
                //
                options.UseAspNetCore()
                       .EnableAuthorizationEndpointPassthrough()
                       .EnableStatusCodePagesIntegration();
            })

            // Register the OpenIddict validation components.
            .AddValidation(options =>
            {
                // Import the configuration from the local OpenIddict server instance.
                options.UseLocalServer();

                // Register the ASP.NET Core host.
                options.UseAspNetCore();
            });

        // Register the worker responsible for seeding the database.
        // Note: in a real world application, this step should be part of a setup script.
        services.AddHostedService<Worker>();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
            endpoints.MapDefaultControllerRoute();
        });

        app.UseWelcomePage();
    }
}
using System.Security.Cryptography;
using System.Text;
using ChatAIze.Passkeys.DataTransferObjects;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace ChatAIze.Passkeys;

[method: ActivatorUtilitiesConstructor]
public sealed class PasskeyProvider(IOptions<PasskeyOptions> globalOptions, IJSRuntime jsRuntime) : IAsyncDisposable
{
    private readonly Lazy<Task<IJSObjectReference>> moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>("import", "./_content/ChatAIze.Passkeys/passkeys.js").AsTask());

    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public async ValueTask<bool> ArePasskeysSupportedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var module = await moduleTask.Value;
            return await module.InvokeAsync<bool>("arePasskeysSupported", cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask<Passkey?> CreatePasskeyAsync(byte[] userId, string userName, string? displayName = null, PasskeyOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            options ??= globalOptions.Value;
            cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token).Token;

            var module = await moduleTask.Value;
            var challenge = RandomNumberGenerator.GetBytes(32);
            var passkeyCreationResult = await module.InvokeAsync<PasskeyCreationResult>("createPasskey", cancellationToken, options.Domain, options.AppName, userId, userName, displayName ?? userName, challenge);

            var fido2Configuration = new Fido2Configuration
            {
                ServerDomain = options.Domain,
                ServerName = options.AppName,
                Origins = [.. options.Origins]
            };

            var fido2 = new Fido2(fido2Configuration);

            var response = new AuthenticatorAttestationRawResponse
            {
                Id = passkeyCreationResult.CredentialId,
                RawId = passkeyCreationResult.CredentialId,
                Type = PublicKeyCredentialType.PublicKey,
                Response = new AuthenticatorAttestationRawResponse.ResponseData
                {
                    AttestationObject = passkeyCreationResult.Attestation,
                    ClientDataJson = passkeyCreationResult.ClientDataJson,
                }
            };

            var user = new Fido2User
            {
                Id = userId,
                Name = userName,
                DisplayName = displayName ?? userName,
            };

            var credentialCreateOptions = new CredentialCreateOptions
            {
                Challenge = challenge,
                User = user,
                Rp = new PublicKeyCredentialRpEntity(fido2Configuration.ServerDomain, fido2Configuration.ServerName, null)
            };

            var credentialCreationResult = await fido2.MakeNewCredentialAsync(response, credentialCreateOptions, (_, _) => Task.FromResult(true), cancellationToken: cancellationToken);
            if (credentialCreationResult is null || credentialCreationResult.Result is null)
            {
                return null;
            }

            var passkey = new Passkey
            {
                UserHandle = userId,
                CredentialId = credentialCreationResult.Result.CredentialId,
                PublicKey = credentialCreationResult.Result.PublicKey,
            };

            return passkey;
        }
        catch
        {
            return null;
        }
    }

    public async Task<Passkey?> CreatePasskeyAsync(string userId, string? userName = null, string? displayName = null, PasskeyOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await CreatePasskeyAsync(Encoding.UTF8.GetBytes(userId), userName ?? userId, displayName ?? userName ?? userId, options, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<Passkey?> CreatePasskeyAsync(Guid userId, string? userName = null, string? displayName = null, PasskeyOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var userIdString = userId.ToString();
            return await CreatePasskeyAsync(userIdString, userName, displayName, options, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async ValueTask<Passkey?> GetPasskeyAsync(PasskeyOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            options ??= globalOptions.Value;
            cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token).Token;

            var module = await moduleTask.Value;
            var challenge = RandomNumberGenerator.GetBytes(32);
            var result = await module.InvokeAsync<PasskeyRetrievalResult>("getPasskey", cancellationToken, options.Domain, challenge);

            var passkey = new Passkey
            {
                UserHandle = result.UserHandle,
                CredentialId = result.CredentialId,
                Challenge = challenge,
                AuthenticatorData = result.AuthenticatorData,
                ClientDataJson = result.ClientDataJson,
                Signature = result.Signature
            };

            return passkey;
        }
        catch
        {
            return null;
        }
    }

    public async ValueTask<bool> VerifyPasskeyAsync(Passkey passkey, byte[] userId, byte[] publicKey, PasskeyOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            options ??= globalOptions.Value;
            cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token).Token;

            var fido2Configuration = new Fido2Configuration
            {
                ServerDomain = options.Domain,
                ServerName = options.AppName,
                Origins = [.. options.Origins]
            };

            var fido2 = new Fido2(fido2Configuration);

            var response = new AuthenticatorAssertionRawResponse
            {
                Id = passkey.CredentialId,
                RawId = passkey.CredentialId,
                Type = PublicKeyCredentialType.PublicKey,
                Response = new AuthenticatorAssertionRawResponse.AssertionResponse
                {
                    AuthenticatorData = passkey.AuthenticatorData,
                    ClientDataJson = passkey.ClientDataJson,
                    Signature = passkey.Signature,
                }
            };

            var assertionOptions = new AssertionOptions
            {
                Challenge = passkey.Challenge,
                RpId = fido2Configuration.ServerDomain,
            };

            var assertionResult = await fido2.MakeAssertionAsync(response, assertionOptions, publicKey, 0, (args, _) => Task.FromResult(args.UserHandle == userId), cancellationToken: cancellationToken);
            return assertionResult.Status == "ok";
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask<bool> VerifyPasskeyAsync(Passkey passkey, string userId, string publicKey, PasskeyOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await VerifyPasskeyAsync(passkey, Encoding.UTF8.GetBytes(userId), Convert.FromBase64String(publicKey), options, cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask<bool> VerifyPasskeyAsync(Passkey passkey, Guid userId, string publicKey, PasskeyOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await VerifyPasskeyAsync(passkey, userId.ToString(), publicKey, options, cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (moduleTask.IsValueCreated)
        {
            if (!_cancellationTokenSource.IsCancellationRequested && _cancellationTokenSource.Token.CanBeCanceled)
            {
                await _cancellationTokenSource.CancelAsync();
                _cancellationTokenSource.Dispose();
            }

            var module = await moduleTask.Value;
            await module.DisposeAsync();
        }
    }
}
using System.Diagnostics.CodeAnalysis;

namespace ChatAIze.Passkeys;

public sealed record PasskeyOptions
{
    public PasskeyOptions() { }

    [SetsRequiredMembers]
    public PasskeyOptions(string appName, string domain, List<string> origins)
    {
        AppName = appName;
        Domain = domain;
        Origins = origins;
    }

    public required string AppName { get; set; }

    public required string Domain { get; set; }

    public required List<string> Origins { get; set; }
}
namespace ChatAIze.Passkeys;

public sealed record Passkey
{
    public required byte[] UserHandle { get; init; }

    public required byte[] CredentialId { get; init; }

    public byte[]? PublicKey { get; init; }

    public string UserHandleBase64 => Convert.ToBase64String(UserHandle);

    public string CredentialIdBase64 => Convert.ToBase64String(CredentialId);

    public string? PublicKeyBase64 => PublicKey is not null ? Convert.ToBase64String(PublicKey) : null;

    internal byte[]? Challenge { get; init; }

    internal byte[]? AuthenticatorData { get; init; }

    internal byte[]? ClientDataJson { get; init; }

    internal byte[]? Signature { get; init; }
}
using Microsoft.Extensions.DependencyInjection;

namespace ChatAIze.Passkeys;

public static class PasskeyProviderExtensions
{
    public static IServiceCollection AddPasskeyProvider(this IServiceCollection services, Action<PasskeyOptions> configure)
    {
        services.AddScoped<PasskeyProvider>();
        services.Configure(configure);

        return services;
    }
}
namespace ChatAIze.Passkeys.DataTransferObjects;

internal sealed record PasskeyCreationResult
{
    public required byte[] CredentialId { get; init; }

    public required byte[] Attestation { get; init; }

    public required byte[] ClientDataJson { get; init; }
}
namespace ChatAIze.Passkeys.DataTransferObjects;

internal sealed record PasskeyRetrievalResult
{
    public required byte[] UserHandle { get; init; }

    public required byte[] CredentialId { get; init; }

    public required byte[] AuthenticatorData { get; init; }

    public required byte[] ClientDataJson { get; init; }

    public required byte[] Signature { get; init; }
}
namespace BlazorWasmDemo.Server.Controllers;

using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

using Fido2NetLib;
using Fido2NetLib.Development;
using Fido2NetLib.Objects;

using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private static readonly SigningCredentials _signingCredentials = new(
        new SymmetricSecurityKey("This is my very long and totally secret key for signing tokens, which clients may never learn or I'd have to replace it."u8.ToArray()),
        SecurityAlgorithms.HmacSha256
    );

    private static readonly DevelopmentInMemoryStore _demoStorage = new();
    private static readonly Dictionary<string, CredentialCreateOptions> _pendingCredentials = new();
    private static readonly Dictionary<string, AssertionOptions> _pendingAssertions = new();
    private readonly IFido2 _fido2;

    private static string FormatException(Exception e) => $"{e.Message}{e.InnerException?.Message ?? string.Empty}";

    public UserController(IFido2 fido2)
    {
        _fido2 = fido2;
    }

    /// <summary>
    /// Creates options to create a new credential for a user.
    /// </summary>
    /// <param name="username">(optional) The user's internal identifier. Omit for usernameless account.</param>
    /// <param name="displayName">(optional as query) Name for display purposes.</param>
    /// <param name="attestationType">(optional as query)</param>
    /// <param name="authenticator">(optional as query)</param>
    /// <param name="userVerification">(optional as query)</param>
    /// <param name="residentKey">(optional as query)</param>
    /// <returns>A new <see cref="CredentialCreateOptions"/>. Contains an error message if .Status is "error".</returns>
    [HttpGet("{username}/credential-options")]
    [HttpGet("credential-options")]
    public CredentialCreateOptions GetCredentialOptions(
        [FromRoute] string? username,
        [FromQuery] string? displayName,
        [FromQuery] AttestationConveyancePreference? attestationType,
        [FromQuery] AuthenticatorAttachment? authenticator,
        [FromQuery] UserVerificationRequirement? userVerification,
        [FromQuery] ResidentKeyRequirement? residentKey)
    {
        try
        {
            var key = username;
            if (string.IsNullOrEmpty(username))
            {
                var created = DateTime.UtcNow;
                if (string.IsNullOrEmpty(displayName))
                {
                    // More precise generated name for less collisions in _pendingCredentials
                    username = $"(Usernameless user created {created})";
                }
                else
                {
                    // Less precise but nicer for user if there's a displayName set anyway
                    username = $"{displayName} (Usernameless user created {created.ToShortDateString()})";
                }
                key = Convert.ToBase64String(Encoding.UTF8.GetBytes(username));
            }
            Debug.Assert(key != null); // If it was null before, it was set to the base64 value. Analyzer doesn't understand this though.

            // 1. Get user from DB by username (in our example, auto create missing users)
            var user = _demoStorage.GetOrAddUser(username, () => new Fido2User
            {
                DisplayName = displayName,
                Name = username,
                Id = Encoding.UTF8.GetBytes(username) // byte representation of userID is required
            });

            // 2. Get user existing keys by username
            var existingKeys = _demoStorage.GetCredentialsByUser(user).Select(c => c.Descriptor).ToList();

            // 3. Build authenticator selection
            var authenticatorSelection = AuthenticatorSelection.Default;
            if (authenticator != null)
            {
                authenticatorSelection.AuthenticatorAttachment = authenticator;
            }

            if (userVerification != null)
            {
                authenticatorSelection.UserVerification = userVerification.Value;
            }

            if (residentKey != null)
            {
                authenticatorSelection.ResidentKey = residentKey.Value;
            }

            // 4. Create options
            var options = _fido2.RequestNewCredential(new RequestNewCredentialParams
            {
                User = user,
                ExcludeCredentials = existingKeys,
                AuthenticatorSelection = authenticatorSelection,
                AttestationPreference = attestationType ?? AttestationConveyancePreference.None,
                Extensions = new AuthenticationExtensionsClientInputs
                {
                    Extensions = true,
                    UserVerificationMethod = true,
                    CredProps = true
                }
            });

            // 5. Temporarily store options, session/in-memory cache/redis/db
            _pendingCredentials[key] = options;

            // 6. return options to client
            return options;
        }
        catch (Exception)
        {
            throw;
        }
    }

    /// <summary>
    /// Creates a new credential for a user.
    /// </summary>
    /// <param name="username">Username of registering user. If usernameless, use base64 encoded options.User.Name from the credential-options used to create the credential.</param>
    /// <param name="attestationResponse"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>a string containing either "OK" or an error message.</returns>
    [HttpPut("{username}/credential")]
    public async Task<string> CreateCredentialAsync([FromRoute] string username, [FromBody] AuthenticatorAttestationRawResponse attestationResponse, CancellationToken cancellationToken)
    {
        try
        {
            // 1. Get the options we sent the client
            var options = _pendingCredentials[username];

            // 2. Create callback so that lib can verify credential id is unique to this user

            // 3. Verify and make the credentials
            var credential = await _fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = attestationResponse,
                OriginalOptions = options,
                IsCredentialIdUniqueToUserCallback = CredentialIdUniqueToUserAsync
            }, cancellationToken: cancellationToken);

            // 4. Store the credentials in db
            _demoStorage.AddCredentialToUser(options.User, new StoredCredential
            {

                AttestationFormat = credential.AttestationFormat,
                Id = credential.Id,
                PublicKey = credential.PublicKey,
                UserHandle = credential.User.Id,
                SignCount = credential.SignCount,
                RegDate = DateTimeOffset.UtcNow,
                AaGuid = credential.AaGuid,
                Transports = credential.Transports,
                IsBackupEligible = credential.IsBackupEligible,
                IsBackedUp = credential.IsBackedUp,
                AttestationObject = credential.AttestationObject,
                AttestationClientDataJson = credential.AttestationClientDataJson,
            });

            // 5. Now we need to remove the options from the pending dictionary
            _pendingCredentials.Remove(Request.Host.ToString());

            // 5. return OK to client
            return "OK";
        }
        catch (Exception e)
        {
            return FormatException(e);
        }
    }

    private static async Task<bool> CredentialIdUniqueToUserAsync(IsCredentialIdUniqueToUserParams args, CancellationToken cancellationToken)
    {
        var users = await _demoStorage.GetUsersByCredentialIdAsync(args.CredentialId, cancellationToken);
        return users.Count <= 0;
    }

    [HttpGet("{username}/assertion-options")]
    [HttpGet("assertion-options")]
    public AssertionOptions MakeAssertionOptions([FromRoute] string? username, [FromQuery] UserVerificationRequirement? userVerification)
    {
        try
        {
            var existingKeys = new List<PublicKeyCredentialDescriptor>();
            if (!string.IsNullOrEmpty(username))
            {
                // 1. Get user and their credentials from DB
                var user = _demoStorage.GetUser(username);

                if (user != null)
                    existingKeys = _demoStorage.GetCredentialsByUser(user).Select(c => c.Descriptor).ToList();
            }

            var exts = new AuthenticationExtensionsClientInputs
            {
                UserVerificationMethod = true,
                Extensions = true
            };

            // 2. Create options (usernameless users will be prompted by their device to select a credential from their own list)
            var options = _fido2.GetAssertionOptions(new GetAssertionOptionsParams
            {
                AllowedCredentials = existingKeys,
                UserVerification = userVerification ?? UserVerificationRequirement.Discouraged,
                Extensions = exts
            });

            // 4. Temporarily store options, session/in-memory cache/redis/db
            _pendingAssertions[new string(options.Challenge.Select(b => (char)b).ToArray())] = options;

            // 5. return options to client
            return options;
        }
        catch (Exception)
        {
            throw;
        }
    }

    /// <summary>
    /// Verifies an assertion response from a client, generating a new JWT for the user.
    /// </summary>
    /// <param name="clientResponse">The client's authenticator's response to the challenge.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>
    /// Either a new JWT header or an error message.
    /// Example successful response:
    /// "Bearer eyyylmaooimtotallyatoken"
    /// Example error response:
    /// "Error: Invalid assertion"
    /// </returns>
    [HttpPost("assertion")]
    public async Task<string> MakeAssertionAsync([FromBody] AuthenticatorAssertionRawResponse clientResponse,
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. Get the assertion options we sent the client remove them from memory so they can't be used again
            var response = JsonSerializer.Deserialize<AuthenticatorResponse>(clientResponse.Response.ClientDataJson);
            if (response is null)
            {
                return "Error: Could not deserialize client data";
            }

            var key = new string(response.Challenge.Select(b => (char)b).ToArray());
            if (!_pendingAssertions.TryGetValue(key, out var options))
            {
                return "Error: Challenge not found, please get a new one via GET /{username?}/assertion-options";
            }
            _pendingAssertions.Remove(key);

            // 2. Get registered credential from database
            var creds = _demoStorage.GetCredentialById(clientResponse.Id) ?? throw new Exception("Unknown credentials");

            // 3. Make the assertion
            var res = await _fido2.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = clientResponse,
                OriginalOptions = options,
                StoredPublicKey = creds.PublicKey,
                StoredSignatureCounter = creds.SignCount,
                IsUserHandleOwnerOfCredentialIdCallback = UserHandleOwnerOfCredentialIdAsync
            }, cancellationToken: cancellationToken);

            // 4. Store the updated counter
            _demoStorage.UpdateCounter(res.CredentialId, res.SignCount);


            // 5. return result to client
            var handler = new JwtSecurityTokenHandler();
            var token = handler.CreateEncodedJwt(
                HttpContext.Request.Host.Host,
                HttpContext.Request.Headers.Referer,
                new ClaimsIdentity(new Claim[] { new(ClaimTypes.Actor, Encoding.UTF8.GetString(creds.UserHandle)) }),
                DateTime.Now.Subtract(TimeSpan.FromMinutes(1)),
                DateTime.Now.AddDays(1),
                DateTime.Now,
                _signingCredentials,
                null);

            if (token is null)
            {
                return "Error: Token couldn't be created";
            }

            return $"Bearer {token}";
        }
        catch (Exception e)
        {
            return $"Error: {FormatException(e)}";
        }
    }

    private static async Task<bool> UserHandleOwnerOfCredentialIdAsync(IsUserHandleOwnerOfCredentialIdParams args, CancellationToken cancellationToken)
    {
        var storedCreds = await _demoStorage.GetCredentialsByUserHandleAsync(args.UserHandle, cancellationToken);
        return storedCreds.Exists(c => c.Descriptor.Id.SequenceEqual(args.CredentialId));
    }
}
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
var origin = new Uri(builder.Configuration["Origin"]!);
builder.Services.AddFido2(options =>
{
    options.ServerDomain = origin.Host;
    options.ServerName = "FIDO2 Server";
    options.Origins = new HashSet<string> { origin.AbsoluteUri };
    options.TimestampDriftTolerance = 1000;
});
builder.Services.AddSwaggerGen(opts =>
{
    opts.SwaggerDoc("v1", new() { Title = "FIDO2 Server", Version = "v1" });
    opts.SchemaGeneratorOptions.SupportNonNullableReferenceTypes = true;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    //app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseSwagger();
app.UseSwaggerUI();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();


app.MapRazorPages();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
@page "/identity/roles"
@attribute [Authorize(IdentityPermissions.Roles.Default)]
@using Volo.Abp.Identity
@using Microsoft.AspNetCore.Authorization
@using Volo.Abp.PermissionManagement.Blazor.Components
@using Volo.Abp.Identity.Localization
@using Volo.Abp.AspNetCore.Components.Web
@using Volo.Abp.AspNetCore.Components.Web.Theming
@using Volo.Abp.BlazoriseUI.Components.ObjectExtending
@using Volo.Abp.AspNetCore.Components.Web.Theming.Layout
@inject AbpBlazorMessageLocalizerHelper<IdentityResource> LH

@inherits AbpCrudPageBase<IIdentityRoleAppService, IdentityRoleDto, Guid, GetIdentityRolesInput, IdentityRoleCreateDto, IdentityRoleUpdateDto>
<Card>
    <CardHeader>
        @* ************************* PAGE HEADER ************************* *@
        <PageHeader Title="@L["Roles"]"
                    BreadcrumbItems="@BreadcrumbItems"
                    Toolbar="@Toolbar">
        </PageHeader>
    </CardHeader>
    <CardBody>
        @* ************************* DATA GRID ************************* *@
        <AbpExtensibleDataGrid TItem="IdentityRoleDto"
                               Data="@Entities"
                               ReadData="@OnDataGridReadAsync"
                               TotalItems="@TotalCount"
                               ShowPager="true"
                               PageSize="@PageSize"
                               CurrentPage="@CurrentPage"
                               Columns="@RoleManagementTableColumns">
        </AbpExtensibleDataGrid>
    </CardBody>
</Card>

@* ************************* CREATE MODAL ************************* *@
@if (HasCreatePermission)
{
    <Modal @ref="CreateModal" Closing="@ClosingCreateModal">
        <ModalContent Centered="true">
            <Form>
                <ModalHeader>
                    <ModalTitle>@L["NewRole"]</ModalTitle>
                    <CloseButton Clicked="CloseCreateModalAsync"/>
                </ModalHeader>
                <ModalBody>
                    <Validations @ref="@CreateValidationsRef" Model="@NewEntity" ValidateOnLoad="false">
                        <Validation MessageLocalizer="@LH.Localize">
                            <Field>
                                <FieldLabel>@L["DisplayName:RoleName"] *</FieldLabel>
                                <TextEdit @bind-Text="@NewEntity.Name" Autofocus="true">
                                    <Feedback>
                                        <ValidationError/>
                                    </Feedback>
                                </TextEdit>
                            </Field>
                            <ExtensionProperties TEntityType="IdentityRoleCreateDto" TResourceType="IdentityResource" Entity="@NewEntity" LH="@LH" ModalType="ExtensionPropertyModalType.CreateModal" />
                        </Validation>
                        <Field>
                            <Check TValue="bool" @bind-Checked="@NewEntity.IsDefault">@L["DisplayName:IsDefault"]</Check>
                            <Check TValue="bool" @bind-Checked="@NewEntity.IsPublic">@L["DisplayName:IsPublic"]</Check>
                        </Field>
                    </Validations>
                </ModalBody>
                <ModalFooter>
                    <Button Color="Color.Primary" Outline Clicked="CloseCreateModalAsync">@L["Cancel"]</Button>
                    <SubmitButton Clicked="@CreateEntityAsync"/>
                </ModalFooter>
            </Form>
        </ModalContent>
    </Modal>
}
@* ************************* EDIT MODAL ************************* *@
@if (HasUpdatePermission)
{
    <Modal @ref="EditModal" Closing="@ClosingEditModal">
        <ModalContent Centered="true">
            <Form>
                <ModalHeader>
                    <ModalTitle>@L["Edit"]</ModalTitle>
                    <CloseButton Clicked="CloseEditModalAsync"/>
                </ModalHeader>
                <ModalBody>
                    <Validations @ref="@EditValidationsRef" Model="@EditingEntity" ValidateOnLoad="false">
                        <input type="hidden" name="ConcurrencyStamp" @bind-value="EditingEntity.ConcurrencyStamp"/>
                        <Validation MessageLocalizer="@LH.Localize">
                            <Field>
                                <FieldLabel>@L["DisplayName:RoleName"] *</FieldLabel>
                                <TextEdit @bind-Text="EditingEntity.Name" Autofocus="true">
                                    <Feedback>
                                        <ValidationError/>
                                    </Feedback>
                                </TextEdit>
                            </Field>
                            <ExtensionProperties TEntityType="IdentityRoleUpdateDto" TResourceType="IdentityResource" Entity="@EditingEntity" LH="@LH"  ModalType="ExtensionPropertyModalType.EditModal" />
                        </Validation>
                        <Field>
                            <Check TValue="bool" @bind-Checked="@EditingEntity.IsDefault">@L["DisplayName:IsDefault"]</Check>
                            <Check TValue="bool" @bind-Checked="@EditingEntity.IsPublic">@L["DisplayName:IsPublic"]</Check>
                        </Field>
                    </Validations>
                </ModalBody>
                <ModalFooter>
                    <Button Color="Color.Primary" Outline Clicked="CloseEditModalAsync">@L["Cancel"]</Button>
                    <SubmitButton Clicked="@UpdateEntityAsync"/>
                </ModalFooter>
            </Form>
        </ModalContent>
    </Modal>
}

@if (HasManagePermissionsPermission)
{
    <PermissionManagementModal @ref="PermissionManagementModal"/>
}@page "/identity/users"
@attribute [Authorize(IdentityPermissions.Users.Default)]
@using Microsoft.AspNetCore.Authorization
@using Volo.Abp.PermissionManagement.Blazor.Components
@using Volo.Abp.BlazoriseUI.Components.ObjectExtending
@using Volo.Abp.Identity.Localization
@using Volo.Abp.AspNetCore.Components.Web.Theming.Layout
@inject AbpBlazorMessageLocalizerHelper<IdentityResource> LH

@inherits AbpCrudPageBase<IIdentityUserAppService, IdentityUserDto, Guid, GetIdentityUsersInput, IdentityUserCreateDto, IdentityUserUpdateDto>

<Card>
    <CardHeader>
        @* ************************* PAGE HEADER ************************* *@
        <PageHeader Title="@L["Users"]"
                    BreadcrumbItems="@BreadcrumbItems"
                    Toolbar="@Toolbar" />
    </CardHeader>
    <CardBody class="row">

        <Column ColumnSize="ColumnSize.Is8">
        </Column>
        <Column ColumnSize="ColumnSize.Is4" class="form-group row" style="text-align:right;">
            <label for="inputPassword" class="col-sm-4 col-form-label pt-1">  @L["Search"] </label>
            <div class="col-sm-8">
                <TextEdit class="form-control-sm" id="inputPassword" Text="@GetListInput.Filter" TextChanged="@OnSearchTextChanged" />
            </div>
        </Column>

        @* ************************* DATA GRID ************************* *@
        <AbpExtensibleDataGrid TItem="IdentityUserDto"
                               Data="Entities"
                               ReadData="OnDataGridReadAsync"
                               TotalItems="TotalCount"
                               ShowPager="true"
                               PageSize="PageSize"
                               CurrentPage="@CurrentPage"
                               Columns="@UserManagementTableColumns">
        </AbpExtensibleDataGrid>
    </CardBody>
</Card>

@* ************************* CREATE MODAL ************************* *@
@if (HasCreatePermission)
{
    <Modal @ref="CreateModal" Closing="@ClosingCreateModal">
        <ModalContent Centered="true">
            <Form>
                <ModalHeader>
                    <ModalTitle>@L["NewUser"]</ModalTitle>
                    <CloseButton Clicked="CloseCreateModalAsync" />
                </ModalHeader>
                <ModalBody>
                    <Validations @ref="@CreateValidationsRef" Model="@NewEntity" ValidateOnLoad="false">
                        <Tabs @bind-SelectedTab="@CreateModalSelectedTab">
                            <Items>
                                <Tab Name="UserInformations">@L["UserInformations"]</Tab>
                                <Tab Name="Roles">@L["Roles"]</Tab>
                            </Items>
                            <Content>
                                <TabPanel Name="UserInformations">
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:UserName"] *</FieldLabel>
                                            <TextEdit @bind-Text="NewEntity.UserName" Autofocus="true">
                                                <Feedback>
                                                    <ValidationError />
                                                </Feedback>
                                            </TextEdit>
                                        </Field>
                                    </Validation>
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:Name"]</FieldLabel>
                                            <TextEdit @bind-Text="NewEntity.Name">
                                                <Feedback>
                                                    <ValidationError />
                                                </Feedback>
                                            </TextEdit>
                                        </Field>
                                    </Validation>
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:Surname"]</FieldLabel>
                                            <TextEdit @bind-Text="NewEntity.Surname">
                                                <Feedback>
                                                    <ValidationError />
                                                </Feedback>
                                            </TextEdit>
                                        </Field>
                                    </Validation>
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:Password"] *</FieldLabel>
                                            <Addons>
                                                <Addon AddonType="AddonType.Body">
                                                    <TextEdit Role="@_passwordTextRole" @bind-Text="NewEntity.Password">
                                                    </TextEdit>
                                                </Addon>
                                                <Addon AddonType="AddonType.End">
                                                    <Button Color="Color.Secondary" Clicked="@(() => ChangePasswordTextRole(null))">
                                                        <Icon Name="ShowPassword ? IconName.Eye : IconName.EyeSlash" />
                                                    </Button>
                                                </Addon>
                                            </Addons>
                                            <ValidationError Style="display: block" />
                                        </Field>
                                    </Validation>
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:Email"] *</FieldLabel>
                                            <TextEdit @bind-Text="NewEntity.Email">
                                                <Feedback>
                                                    <ValidationError />
                                                </Feedback>
                                            </TextEdit>
                                        </Field>
                                    </Validation>
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:PhoneNumber"]</FieldLabel>
                                            <TextEdit @bind-Text="NewEntity.PhoneNumber">
                                                <Feedback>
                                                    <ValidationError />
                                                </Feedback>
                                            </TextEdit>
                                        </Field>
                                    </Validation>
                                    <Field>
                                        <Check TValue="bool" @bind-Checked="@NewEntity.IsActive">@L["DisplayName:IsActive"]</Check>
                                    </Field>
                                    <Field>
                                        <Tooltip Text="@L["Description:LockoutEnabled"].Value" style="width: fit-content;" Placement="TooltipPlacement.Right" >
                                            <Check TValue="bool" @bind-Checked="@NewEntity.LockoutEnabled">@L["DisplayName:LockoutEnabled"] <Icon Name="IconName.InfoCircle"/> </Check>
                                        </Tooltip>
                                    </Field>
                                    <ExtensionProperties TEntityType="IdentityUserCreateDto" TResourceType="IdentityResource" Entity="@NewEntity" LH="@LH" ModalType="ExtensionPropertyModalType.CreateModal" />
                                </TabPanel>
                                <TabPanel Name="Roles">
                                    @if (NewUserRoles != null)
                                    {
                                        @foreach (var role in NewUserRoles)
                                        {
                                            <Field>
                                                <input type="hidden" @bind-value="@role.Name" />
                                                <Check TValue="bool" @bind-Checked="@role.IsAssigned">@role.Name</Check>
                                            </Field>
                                        }
                                    }
                                </TabPanel>
                            </Content>
                        </Tabs>
                    </Validations>
                </ModalBody>
                <ModalFooter>
                    <Button Color="Color.Primary" Outline Clicked="CloseCreateModalAsync">@L["Cancel"]</Button>
                    <SubmitButton Clicked="@CreateEntityAsync" />
                </ModalFooter>
            </Form>
        </ModalContent>
    </Modal>
}

@* ************************* EDIT MODAL ************************* *@
@if (HasUpdatePermission)
{
    <Modal @ref="EditModal" Closing="@ClosingEditModal">
        <ModalContent Centered="true">
            <Form>
                <ModalHeader>
                    <ModalTitle>@L["Edit"]</ModalTitle>
                    <CloseButton Clicked="CloseEditModalAsync" />
                </ModalHeader>
                <ModalBody>
                    <Validations @ref="@EditValidationsRef" Model="@EditingEntity" ValidateOnLoad="false">
                        <input type="hidden" name="ConcurrencyStamp" @bind-value="EditingEntity.ConcurrencyStamp" />

                        <Tabs @bind-SelectedTab="@EditModalSelectedTab">
                            <Items>
                                <Tab Name="UserInformations">@L["UserInformations"]</Tab>
                                @if (EditUserRoles != null && EditUserRoles.Any())
                                {
                                    <Tab Name="Roles">@L["Roles"]</Tab>
                                }
                            </Items>
                            <Content>
                                <TabPanel Name="UserInformations">
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:UserName"] *</FieldLabel>
                                            <TextEdit @bind-Text="EditingEntity.UserName" Autofocus="true">
                                                <Feedback>
                                                    <ValidationError />
                                                </Feedback>
                                            </TextEdit>
                                        </Field>
                                    </Validation>
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:Name"]</FieldLabel>
                                            <TextEdit @bind-Text="EditingEntity.Name">
                                                <Feedback>
                                                    <ValidationError />
                                                </Feedback>
                                            </TextEdit>
                                        </Field>
                                    </Validation>
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:Surname"]</FieldLabel>
                                            <TextEdit @bind-Text="EditingEntity.Surname">
                                                <Feedback>
                                                    <ValidationError />
                                                </Feedback>
                                            </TextEdit>
                                        </Field>
                                    </Validation>
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:Password"]</FieldLabel>
                                            <Addons>
                                                <Addon AddonType="AddonType.Body">
                                                    <TextEdit Role="@_passwordTextRole" @bind-Text="EditingEntity.Password">
                                                    </TextEdit>
                                                </Addon>
                                                <Addon AddonType="AddonType.End">
                                                    <Button Color="Color.Secondary" Clicked="@(() => ChangePasswordTextRole(null))">
                                                        <Icon Name="ShowPassword ? IconName.Eye : IconName.EyeSlash" />
                                                    </Button>
                                                </Addon>
                                            </Addons>
                                            <ValidationError Style="display: block" />
                                        </Field>
                                    </Validation>
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:Email"] *</FieldLabel>
                                            <TextEdit @bind-Text="EditingEntity.Email">
                                                <Feedback>
                                                    <ValidationError />
                                                </Feedback>
                                            </TextEdit>
                                        </Field>
                                    </Validation>
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:PhoneNumber"]</FieldLabel>
                                            <TextEdit @bind-Text="EditingEntity.PhoneNumber">
                                                <Feedback>
                                                    <ValidationError />
                                                </Feedback>
                                            </TextEdit>
                                        </Field>
                                    </Validation>
                                    @if (!IsEditCurrentUser)
                                    {
                                        <Field>
                                            <Check TValue="bool" @bind-Checked="EditingEntity.IsActive">@L["DisplayName:IsActive"]</Check>
                                        </Field>
                                    }
                                    <Field>
                                            <Tooltip Text="@L["Description:LockoutEnabled"].Value" style="width: fit-content;" Placement="TooltipPlacement.Right" >
                                                <Check TValue="bool" @bind-Checked="EditingEntity.LockoutEnabled">@L["DisplayName:LockoutEnabled"] <Icon Name="IconName.InfoCircle"/> </Check>
                                            </Tooltip>
                                        </Field>
                                    <ExtensionProperties TEntityType="IdentityUserUpdateDto" TResourceType="IdentityResource" Entity="@EditingEntity" LH="@LH" ModalType="ExtensionPropertyModalType.EditModal" />
                                </TabPanel>
                                @if (EditUserRoles != null && EditUserRoles.Any())
                                {
                                    <TabPanel Name="Roles">
                                        @if (EditUserRoles != null)
                                        {
                                            @foreach (var role in EditUserRoles)
                                            {
                                                <Field>
                                                    <input type="hidden" @bind-value="@role.Name" />
                                                    <Check TValue="bool" @bind-Checked="@role.IsAssigned">@role.Name</Check>
                                                </Field>
                                            }
                                        }
                                    </TabPanel>
                                }
                            </Content>
                        </Tabs>
                    </Validations>
                </ModalBody>
                <ModalFooter>
                    <Button Color="Color.Primary" Outline Clicked="CloseEditModalAsync">@L["Cancel"]</Button>
                    <SubmitButton Clicked="@UpdateEntityAsync" />
                </ModalFooter>
            </Form>
        </ModalContent>
    </Modal>
}

@if (HasManagePermissionsPermission)
{
    <PermissionManagementModal @ref="PermissionManagementModal" />
}
@page "/identity/users"
@attribute [Authorize(IdentityPermissions.Users.Default)]
@using Microsoft.AspNetCore.Authorization
@using Volo.Abp.PermissionManagement.Blazor.Components
@using Volo.Abp.BlazoriseUI.Components.ObjectExtending
@using Volo.Abp.Identity.Localization
@using Volo.Abp.AspNetCore.Components.Web.Theming.Layout
@inject AbpBlazorMessageLocalizerHelper<IdentityResource> LH

@inherits AbpCrudPageBase<IIdentityUserAppService, IdentityUserDto, Guid, GetIdentityUsersInput, IdentityUserCreateDto, IdentityUserUpdateDto>

<Card>
    <CardHeader>
        @* ************************* PAGE HEADER ************************* *@
        <PageHeader Title="@L["Users"]"
                    BreadcrumbItems="@BreadcrumbItems"
                    Toolbar="@Toolbar" />
    </CardHeader>
    <CardBody class="row">

        <Column ColumnSize="ColumnSize.Is8">
        </Column>
        <Column ColumnSize="ColumnSize.Is4" class="form-group row" style="text-align:right;">
            <label for="inputPassword" class="col-sm-4 col-form-label pt-1">  @L["Search"] </label>
            <div class="col-sm-8">
                <TextEdit class="form-control-sm" id="inputPassword" Text="@GetListInput.Filter" TextChanged="@OnSearchTextChanged" />
            </div>
        </Column>

        @* ************************* DATA GRID ************************* *@
        <AbpExtensibleDataGrid TItem="IdentityUserDto"
                               Data="Entities"
                               ReadData="OnDataGridReadAsync"
                               TotalItems="TotalCount"
                               ShowPager="true"
                               PageSize="PageSize"
                               CurrentPage="@CurrentPage"
                               Columns="@UserManagementTableColumns">
        </AbpExtensibleDataGrid>
    </CardBody>
</Card>

@* ************************* CREATE MODAL ************************* *@
@if (HasCreatePermission)
{
    <Modal @ref="CreateModal" Closing="@ClosingCreateModal">
        <ModalContent Centered="true">
            <Form>
                <ModalHeader>
                    <ModalTitle>@L["NewUser"]</ModalTitle>
                    <CloseButton Clicked="CloseCreateModalAsync" />
                </ModalHeader>
                <ModalBody>
                    <Validations @ref="@CreateValidationsRef" Model="@NewEntity" ValidateOnLoad="false">
                        <Tabs @bind-SelectedTab="@CreateModalSelectedTab">
                            <Items>
                                <Tab Name="UserInformations">@L["UserInformations"]</Tab>
                                <Tab Name="Roles">@L["Roles"]</Tab>
                            </Items>
                            <Content>
                                <TabPanel Name="UserInformations">
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:UserName"] *</FieldLabel>
                                            <TextEdit @bind-Text="NewEntity.UserName" Autofocus="true">
                                                <Feedback>
                                                    <ValidationError />
                                                </Feedback>
                                            </TextEdit>
                                        </Field>
                                    </Validation>
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:Name"]</FieldLabel>
                                            <TextEdit @bind-Text="NewEntity.Name">
                                                <Feedback>
                                                    <ValidationError />
                                                </Feedback>
                                            </TextEdit>
                                        </Field>
                                    </Validation>
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:Surname"]</FieldLabel>
                                            <TextEdit @bind-Text="NewEntity.Surname">
                                                <Feedback>
                                                    <ValidationError />
                                                </Feedback>
                                            </TextEdit>
                                        </Field>
                                    </Validation>
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:Password"] *</FieldLabel>
                                            <Addons>
                                                <Addon AddonType="AddonType.Body">
                                                    <TextEdit Role="@_passwordTextRole" @bind-Text="NewEntity.Password">
                                                    </TextEdit>
                                                </Addon>
                                                <Addon AddonType="AddonType.End">
                                                    <Button Color="Color.Secondary" Clicked="@(() => ChangePasswordTextRole(null))">
                                                        <Icon Name="ShowPassword ? IconName.Eye : IconName.EyeSlash" />
                                                    </Button>
                                                </Addon>
                                            </Addons>
                                            <ValidationError Style="display: block" />
                                        </Field>
                                    </Validation>
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:Email"] *</FieldLabel>
                                            <TextEdit @bind-Text="NewEntity.Email">
                                                <Feedback>
                                                    <ValidationError />
                                                </Feedback>
                                            </TextEdit>
                                        </Field>
                                    </Validation>
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:PhoneNumber"]</FieldLabel>
                                            <TextEdit @bind-Text="NewEntity.PhoneNumber">
                                                <Feedback>
                                                    <ValidationError />
                                                </Feedback>
                                            </TextEdit>
                                        </Field>
                                    </Validation>
                                    <Field>
                                        <Check TValue="bool" @bind-Checked="@NewEntity.IsActive">@L["DisplayName:IsActive"]</Check>
                                    </Field>
                                    <Field>
                                        <Tooltip Text="@L["Description:LockoutEnabled"].Value" style="width: fit-content;" Placement="TooltipPlacement.Right" >
                                            <Check TValue="bool" @bind-Checked="@NewEntity.LockoutEnabled">@L["DisplayName:LockoutEnabled"] <Icon Name="IconName.InfoCircle"/> </Check>
                                        </Tooltip>
                                    </Field>
                                    <ExtensionProperties TEntityType="IdentityUserCreateDto" TResourceType="IdentityResource" Entity="@NewEntity" LH="@LH" ModalType="ExtensionPropertyModalType.CreateModal" />
                                </TabPanel>
                                <TabPanel Name="Roles">
                                    @if (NewUserRoles != null)
                                    {
                                        @foreach (var role in NewUserRoles)
                                        {
                                            <Field>
                                                <input type="hidden" @bind-value="@role.Name" />
                                                <Check TValue="bool" @bind-Checked="@role.IsAssigned">@role.Name</Check>
                                            </Field>
                                        }
                                    }
                                </TabPanel>
                            </Content>
                        </Tabs>
                    </Validations>
                </ModalBody>
                <ModalFooter>
                    <Button Color="Color.Primary" Outline Clicked="CloseCreateModalAsync">@L["Cancel"]</Button>
                    <SubmitButton Clicked="@CreateEntityAsync" />
                </ModalFooter>
            </Form>
        </ModalContent>
    </Modal>
}

@* ************************* EDIT MODAL ************************* *@
@if (HasUpdatePermission)
{
    <Modal @ref="EditModal" Closing="@ClosingEditModal">
        <ModalContent Centered="true">
            <Form>
                <ModalHeader>
                    <ModalTitle>@L["Edit"]</ModalTitle>
                    <CloseButton Clicked="CloseEditModalAsync" />
                </ModalHeader>
                <ModalBody>
                    <Validations @ref="@EditValidationsRef" Model="@EditingEntity" ValidateOnLoad="false">
                        <input type="hidden" name="ConcurrencyStamp" @bind-value="EditingEntity.ConcurrencyStamp" />

                        <Tabs @bind-SelectedTab="@EditModalSelectedTab">
                            <Items>
                                <Tab Name="UserInformations">@L["UserInformations"]</Tab>
                                @if (EditUserRoles != null && EditUserRoles.Any())
                                {
                                    <Tab Name="Roles">@L["Roles"]</Tab>
                                }
                            </Items>
                            <Content>
                                <TabPanel Name="UserInformations">
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:UserName"] *</FieldLabel>
                                            <TextEdit @bind-Text="EditingEntity.UserName" Autofocus="true">
                                                <Feedback>
                                                    <ValidationError />
                                                </Feedback>
                                            </TextEdit>
                                        </Field>
                                    </Validation>
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:Name"]</FieldLabel>
                                            <TextEdit @bind-Text="EditingEntity.Name">
                                                <Feedback>
                                                    <ValidationError />
                                                </Feedback>
                                            </TextEdit>
                                        </Field>
                                    </Validation>
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:Surname"]</FieldLabel>
                                            <TextEdit @bind-Text="EditingEntity.Surname">
                                                <Feedback>
                                                    <ValidationError />
                                                </Feedback>
                                            </TextEdit>
                                        </Field>
                                    </Validation>
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:Password"]</FieldLabel>
                                            <Addons>
                                                <Addon AddonType="AddonType.Body">
                                                    <TextEdit Role="@_passwordTextRole" @bind-Text="EditingEntity.Password">
                                                    </TextEdit>
                                                </Addon>
                                                <Addon AddonType="AddonType.End">
                                                    <Button Color="Color.Secondary" Clicked="@(() => ChangePasswordTextRole(null))">
                                                        <Icon Name="ShowPassword ? IconName.Eye : IconName.EyeSlash" />
                                                    </Button>
                                                </Addon>
                                            </Addons>
                                            <ValidationError Style="display: block" />
                                        </Field>
                                    </Validation>
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:Email"] *</FieldLabel>
                                            <TextEdit @bind-Text="EditingEntity.Email">
                                                <Feedback>
                                                    <ValidationError />
                                                </Feedback>
                                            </TextEdit>
                                        </Field>
                                    </Validation>
                                    <Validation MessageLocalizer="@LH.Localize">
                                        <Field>
                                            <FieldLabel>@L["DisplayName:PhoneNumber"]</FieldLabel>
                                            <TextEdit @bind-Text="EditingEntity.PhoneNumber">
                                                <Feedback>
                                                    <ValidationError />
                                                </Feedback>
                                            </TextEdit>
                                        </Field>
                                    </Validation>
                                    @if (!IsEditCurrentUser)
                                    {
                                        <Field>
                                            <Check TValue="bool" @bind-Checked="EditingEntity.IsActive">@L["DisplayName:IsActive"]</Check>
                                        </Field>
                                    }
                                    <Field>
                                            <Tooltip Text="@L["Description:LockoutEnabled"].Value" style="width: fit-content;" Placement="TooltipPlacement.Right" >
                                                <Check TValue="bool" @bind-Checked="EditingEntity.LockoutEnabled">@L["DisplayName:LockoutEnabled"] <Icon Name="IconName.InfoCircle"/> </Check>
                                            </Tooltip>
                                        </Field>
                                    <ExtensionProperties TEntityType="IdentityUserUpdateDto" TResourceType="IdentityResource" Entity="@EditingEntity" LH="@LH" ModalType="ExtensionPropertyModalType.EditModal" />
                                </TabPanel>
                                @if (EditUserRoles != null && EditUserRoles.Any())
                                {
                                    <TabPanel Name="Roles">
                                        @if (EditUserRoles != null)
                                        {
                                            @foreach (var role in EditUserRoles)
                                            {
                                                <Field>
                                                    <input type="hidden" @bind-value="@role.Name" />
                                                    <Check TValue="bool" @bind-Checked="@role.IsAssigned">@role.Name</Check>
                                                </Field>
                                            }
                                        }
                                    </TabPanel>
                                }
                            </Content>
                        </Tabs>
                    </Validations>
                </ModalBody>
                <ModalFooter>
                    <Button Color="Color.Primary" Outline Clicked="CloseEditModalAsync">@L["Cancel"]</Button>
                    <SubmitButton Clicked="@UpdateEntityAsync" />
                </ModalFooter>
            </Form>
        </ModalContent>
    </Modal>
}

@if (HasManagePermissionsPermission)
{
    <PermissionManagementModal @ref="PermissionManagementModal" />
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using OpenIddict.Abstractions;
using Volo.Abp;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.OpenIddict.Applications;
using Volo.Abp.OpenIddict.Scopes;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Uow;

namespace BRU.ABP.ASPIREAPI.OpenIddict;

/* Creates initial data that is needed to property run the application
 * and make client-to-server communication possible.
 */
public class OpenIddictDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IConfiguration _configuration;
    private readonly IOpenIddictApplicationRepository _openIddictApplicationRepository;
    private readonly IAbpApplicationManager _applicationManager;
    private readonly IOpenIddictScopeRepository _openIddictScopeRepository;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly IPermissionDataSeeder _permissionDataSeeder;
    private readonly IStringLocalizer<OpenIddictResponse> L;

    public OpenIddictDataSeedContributor(
        IConfiguration configuration,
        IOpenIddictApplicationRepository openIddictApplicationRepository,
        IAbpApplicationManager applicationManager,
        IOpenIddictScopeRepository openIddictScopeRepository,
        IOpenIddictScopeManager scopeManager,
        IPermissionDataSeeder permissionDataSeeder,
        IStringLocalizer<OpenIddictResponse> l)
    {
        _configuration = configuration;
        _openIddictApplicationRepository = openIddictApplicationRepository;
        _applicationManager = applicationManager;
        _openIddictScopeRepository = openIddictScopeRepository;
        _scopeManager = scopeManager;
        _permissionDataSeeder = permissionDataSeeder;
        L = l;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        await CreateScopesAsync();
        await CreateApplicationsAsync();
    }

    private async Task CreateScopesAsync()
    {
        if (await _openIddictScopeRepository.FindByNameAsync("ASPIREAPI") == null)
        {
            await _scopeManager.CreateAsync(new OpenIddictScopeDescriptor {
                Name = "ASPIREAPI", DisplayName = "ASPIREAPI API", Resources = { "ASPIREAPI" }
            });
        }
    }

    private async Task CreateApplicationsAsync()
    {
        var commonScopes = new List<string> {
            OpenIddictConstants.Permissions.Scopes.Address,
            OpenIddictConstants.Permissions.Scopes.Email,
            OpenIddictConstants.Permissions.Scopes.Phone,
            OpenIddictConstants.Permissions.Scopes.Profile,
            OpenIddictConstants.Permissions.Scopes.Roles,
            "ASPIREAPI"
        };

        var configurationSection = _configuration.GetSection("OpenIddict:Applications");


        //Console Test / Angular Client
        var consoleAndAngularClientId = configurationSection["ASPIREAPI_App:ClientId"];
        if (!consoleAndAngularClientId.IsNullOrWhiteSpace())
        {
            var consoleAndAngularClientRootUrl = configurationSection["ASPIREAPI_App:RootUrl"]?.TrimEnd('/');
            await CreateApplicationAsync(
                applicationType: OpenIddictConstants.ApplicationTypes.Web,
                name: consoleAndAngularClientId!,
                type: OpenIddictConstants.ClientTypes.Public,
                consentType: OpenIddictConstants.ConsentTypes.Implicit,
                displayName: "Console Test / Angular Application",
                secret: null,
                grantTypes: new List<string> {
                    OpenIddictConstants.GrantTypes.AuthorizationCode,
                    OpenIddictConstants.GrantTypes.Password,
                    OpenIddictConstants.GrantTypes.ClientCredentials,
                    OpenIddictConstants.GrantTypes.RefreshToken,
                    "LinkLogin",
                    "Impersonation"
                },
                scopes: commonScopes,
                redirectUri: consoleAndAngularClientRootUrl,
                postLogoutRedirectUri: consoleAndAngularClientRootUrl,
                clientUri: consoleAndAngularClientRootUrl,
                logoUri: "/images/clients/angular.svg"
            );
        }

        
        




        // Swagger Client
        var swaggerClientId = configurationSection["ASPIREAPI_Swagger:ClientId"];
        if (!swaggerClientId.IsNullOrWhiteSpace())
        {
            var swaggerRootUrl = configurationSection["ASPIREAPI_Swagger:RootUrl"]?.TrimEnd('/');

            await CreateApplicationAsync(
                applicationType: OpenIddictConstants.ApplicationTypes.Web,
                name: swaggerClientId!,
                type: OpenIddictConstants.ClientTypes.Public,
                consentType: OpenIddictConstants.ConsentTypes.Implicit,
                displayName: "Swagger Application",
                secret: null,
                grantTypes: new List<string> { OpenIddictConstants.GrantTypes.AuthorizationCode, },
                scopes: commonScopes,
                redirectUri: $"{swaggerRootUrl}/swagger/oauth2-redirect.html",
                clientUri: swaggerRootUrl.EnsureEndsWith('/') + "swagger",
                logoUri: "/images/clients/swagger.svg"
            );
        }


    }

    private async Task CreateApplicationAsync(
        [NotNull] string applicationType,
        [NotNull] string name,
        [NotNull] string type,
        [NotNull] string consentType,
        string displayName,
        string? secret,
        List<string> grantTypes,
        List<string> scopes,
        string? redirectUri = null,
        string? postLogoutRedirectUri = null,
        List<string>? permissions = null,
        string? clientUri = null,
        string? logoUri = null)
    {
        if (!string.IsNullOrEmpty(secret) && string.Equals(type, OpenIddictConstants.ClientTypes.Public,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException(L["NoClientSecretCanBeSetForPublicApplications"]);
        }

        if (string.IsNullOrEmpty(secret) && string.Equals(type, OpenIddictConstants.ClientTypes.Confidential,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException(L["TheClientSecretIsRequiredForConfidentialApplications"]);
        }

        var client = await _openIddictApplicationRepository.FindByClientIdAsync(name);

        var application = new AbpApplicationDescriptor {
            ApplicationType = applicationType,
            ClientId = name,
            ClientType = type,
            ClientSecret = secret,
            ConsentType = consentType,
            DisplayName = displayName,
            ClientUri = clientUri,
            LogoUri = logoUri,
        };

        Check.NotNullOrEmpty(grantTypes, nameof(grantTypes));
        Check.NotNullOrEmpty(scopes, nameof(scopes));

        if (new[] { OpenIddictConstants.GrantTypes.AuthorizationCode, OpenIddictConstants.GrantTypes.Implicit }.All(
                grantTypes.Contains))
        {
            application.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.CodeIdToken);

            if (string.Equals(type, OpenIddictConstants.ClientTypes.Public, StringComparison.OrdinalIgnoreCase))
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.CodeIdTokenToken);
                application.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.CodeToken);
            }
        }

        if (!redirectUri.IsNullOrWhiteSpace() || !postLogoutRedirectUri.IsNullOrWhiteSpace())
        {
            application.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.EndSession);
        }

        var buildInGrantTypes = new[] {
            OpenIddictConstants.GrantTypes.Implicit, OpenIddictConstants.GrantTypes.Password,
            OpenIddictConstants.GrantTypes.AuthorizationCode, OpenIddictConstants.GrantTypes.ClientCredentials,
            OpenIddictConstants.GrantTypes.DeviceCode, OpenIddictConstants.GrantTypes.RefreshToken
        };

        foreach (var grantType in grantTypes)
        {
            if (grantType == OpenIddictConstants.GrantTypes.AuthorizationCode)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode);
                application.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Code);
            }

            if (grantType == OpenIddictConstants.GrantTypes.AuthorizationCode ||
                grantType == OpenIddictConstants.GrantTypes.Implicit)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Authorization);
            }

            if (grantType == OpenIddictConstants.GrantTypes.AuthorizationCode ||
                grantType == OpenIddictConstants.GrantTypes.ClientCredentials ||
                grantType == OpenIddictConstants.GrantTypes.Password ||
                grantType == OpenIddictConstants.GrantTypes.RefreshToken ||
                grantType == OpenIddictConstants.GrantTypes.DeviceCode)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
                application.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Revocation);
                application.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Introspection);
            }

            if (grantType == OpenIddictConstants.GrantTypes.ClientCredentials)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);
            }

            if (grantType == OpenIddictConstants.GrantTypes.Implicit)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.Implicit);
            }

            if (grantType == OpenIddictConstants.GrantTypes.Password)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.Password);
            }

            if (grantType == OpenIddictConstants.GrantTypes.RefreshToken)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.RefreshToken);
            }

            if (grantType == OpenIddictConstants.GrantTypes.DeviceCode)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.DeviceCode);
                application.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.DeviceAuthorization);
            }

            if (grantType == OpenIddictConstants.GrantTypes.Implicit)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.IdToken);
                if (string.Equals(type, OpenIddictConstants.ClientTypes.Public, StringComparison.OrdinalIgnoreCase))
                {
                    application.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.IdTokenToken);
                    application.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Token);
                }
            }

            if (!buildInGrantTypes.Contains(grantType))
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.GrantType + grantType);
            }
        }

        var buildInScopes = new[] {
            OpenIddictConstants.Permissions.Scopes.Address, OpenIddictConstants.Permissions.Scopes.Email,
            OpenIddictConstants.Permissions.Scopes.Phone, OpenIddictConstants.Permissions.Scopes.Profile,
            OpenIddictConstants.Permissions.Scopes.Roles
        };

        foreach (var scope in scopes)
        {
            if (buildInScopes.Contains(scope))
            {
                application.Permissions.Add(scope);
            }
            else
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + scope);
            }
        }

        if (redirectUri != null)
        {
            if (!redirectUri.IsNullOrEmpty())
            {
                if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri) || !uri.IsWellFormedOriginalString())
                {
                    throw new BusinessException(L["InvalidRedirectUri", redirectUri]);
                }

                if (application.RedirectUris.All(x => x != uri))
                {
                    application.RedirectUris.Add(uri);
                }
            }
        }

        if (postLogoutRedirectUri != null)
        {
            if (!postLogoutRedirectUri.IsNullOrEmpty())
            {
                if (!Uri.TryCreate(postLogoutRedirectUri, UriKind.Absolute, out var uri) ||
                    !uri.IsWellFormedOriginalString())
                {
                    throw new BusinessException(L["InvalidPostLogoutRedirectUri", postLogoutRedirectUri]);
                }

                if (application.PostLogoutRedirectUris.All(x => x != uri))
                {
                    application.PostLogoutRedirectUris.Add(uri);
                }
            }
        }

        if (permissions != null)
        {
            await _permissionDataSeeder.SeedAsync(
                ClientPermissionValueProvider.ProviderName,
                name,
                permissions,
                null
            );
        }

        if (client == null)
        {
            await _applicationManager.CreateAsync(application);
            return;
        }

        if (!HasSameRedirectUris(client, application))
        {
            client.RedirectUris = JsonSerializer.Serialize(application.RedirectUris.Select(q => q.ToString().RemovePostFix("/")));
            client.PostLogoutRedirectUris = JsonSerializer.Serialize(application.PostLogoutRedirectUris.Select(q => q.ToString().RemovePostFix("/")));

            await _applicationManager.UpdateAsync(client.ToModel());
        }

        if (!HasSameScopes(client, application))
        {
            client.Permissions = JsonSerializer.Serialize(application.Permissions.Select(q => q.ToString()));
            await _applicationManager.UpdateAsync(client.ToModel());
        }
    }

    private bool HasSameRedirectUris(OpenIddictApplication existingClient, AbpApplicationDescriptor application)
    {
        return existingClient.RedirectUris == JsonSerializer.Serialize(application.RedirectUris.Select(q => q.ToString().RemovePostFix("/")));
    }

    private bool HasSameScopes(OpenIddictApplication existingClient, AbpApplicationDescriptor application)
    {
        return existingClient.Permissions == JsonSerializer.Serialize(application.Permissions.Select(q => q.ToString().TrimEnd('/')));
    }
}
using System.ComponentModel.DataAnnotations;
using System.Text;
using AliceIdentityService.Models;
using AliceIdentityService.Services;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace AliceIdentityService.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<User> _userManager;
        private readonly SignInManager<User> _signInManager;
        private readonly EmailSender _emailSender;

        private readonly IMapper _mapper;
        private readonly ILogger<AccountController> _logger;

        public AccountController(UserManager<User> userManager, SignInManager<User> signInManager,
            EmailSender emailSender, IMapper mapper, ILogger<AccountController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _emailSender = emailSender;
            _mapper = mapper;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View(new LoginInputModel());
        }

        [HttpPost]
        public async Task<IActionResult> LoginAsync(LoginInputModel input, string returnUrl)
        {
            if (!ModelState.IsValid) return View(input);

            returnUrl ??= Url.Content("~/");

            var result = await _signInManager.PasswordSignInAsync(input.Email, input.Password, input.RememberMe, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                _logger.LogInformation("{user} signed in", input.Email);
                return Redirect(returnUrl);
            }
            else
            {
                _logger.LogInformation("{user} failed to log in. LockedOut: {lockedOut}; NotAllowed: {notAllowed}",
                    input.Email, result.IsLockedOut, result.IsNotAllowed);
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return View(input);
            }
        }

        public async Task<IActionResult> LogoutAsync(string returnUrl)
        {
            var name = User.Identity.Name;
            await _signInManager.SignOutAsync();
            _logger.LogInformation("{user} signed out", name);
            return LocalRedirect(returnUrl ?? Url.Content("~/"));
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View(new RegistrationInputModel());
        }

        [HttpPost]
        public async Task<IActionResult> RegisterAsync(RegistrationInputModel input)
        {
            if (!ModelState.IsValid) return View(input);

            var user = _mapper.Map<User>(input);
            user.UserName = input.Email;
            user.ScreenName = $"{input.FirstName} {input.LastName}";
            var result = await _userManager.CreateAsync(user, input.Password);
            if (result.Succeeded)
            {
                _logger.LogInformation("New account for {user} created", input.Email);

                var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                var link = Url.Action("ConfirmEmail", "Account", new { userId = user.Id, code });
                await _emailSender.SendEmailVerificationMessageAsync(user, link);
                _logger.LogInformation("Verification email to {email}", user.Email);

                return View("Status", new StatusViewModel
                {
                    Subject = "Registration",
                    Message = $@"Thank you for registering on Alice Identity Service. An email has been
                        sent to the address {user.Email}. Please click on the link in the email to confirm
                        your email address and activate your account."
                });
            }
            else
            {
                foreach (var error in result.Errors)
                    ModelState.AddModelError(string.Empty, error.Description);
                return View(input);
            }
        }

        public async Task<IActionResult> ConfirmEmailAsync(string userId, string code)
        {
            if (userId == null || code == null)
                return LocalRedirect("~/");

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound($"Unable to load user with ID '{userId}'.");

            code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            var result = await _userManager.ConfirmEmailAsync(user, code);

            if (result.Succeeded)
            {
                _logger.LogInformation("Email confirmatin successful for {userId}", userId);
                return View("Status", new StatusViewModel
                {
                    Subject = "Email Confirmed",
                    Message = "Thank you for confirming your email. Your account is now activated."
                });
            }
            else
            {
                _logger.LogError("Email confirmation failed for {userId}", userId);
                return View("Error", new ErrorViewModel
                {
                    Message = "Sorry we cannot verify your email."
                });
            }
        }

        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ForgotPasswordAsync(string email)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user != null && await _userManager.IsEmailConfirmedAsync(user))
            {
                var code = await _userManager.GeneratePasswordResetTokenAsync(user);
                code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                var link = Url.Action("ResetPassword", "Account", new { code });
                await _emailSender.SendResetPasswordMessageAsync(email, link);
                _logger.LogInformation("ResetPassword email to {email}", user.Email);
            }

            // Don't reveal that the user does not exist or is not confirmed
            return View("Status", new StatusViewModel
            {
                Subject = "Reset Password",
                Message = @"An email is sent to you with instructions on how to reset your password. If you don't
                        get the email after a couple of minutes, please check your spam folder."
            });
        }

        [HttpGet]
        public IActionResult ResetPassword(string code)
        {
            if (code == null)
                return View("Error", new ErrorViewModel
                {
                    Message = "A code must be supplied for password reset."
                });

            return View(new ResetPasswordInputModel
            {
                Code = code
            });
        }

        [HttpPost]
        public async Task<IActionResult> ResetPasswordAsync(ResetPasswordInputModel input)
        {
            if (!ModelState.IsValid) return View(input);

            var statusViewModel = new StatusViewModel
            {
                Subject = "Reset Password",
                Message = "Your password has been reset."
            };

            var user = await _userManager.FindByEmailAsync(input.Email);

            // Don't reveal that the user does not exist
            if (user == null) return View("Status", statusViewModel);

            var result = await _userManager.ResetPasswordAsync(user,
                Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(input.Code)), input.Password);
            if (result.Succeeded)
            {
                _logger.LogInformation("Password reset successful for {email}", input.Email);
                return View("Status", statusViewModel);
            }
            else
            {
                _logger.LogError("Password reset failed for {email}. {result}", input.Email, result);
                return View("Error", new ErrorViewModel
                {
                    Message = "Sorry we cannot reset your password."
                });
            }
        }

        public IActionResult AccessDenied()
        {
            return View();
        }

        [Authorize]
        public IActionResult Profile()
        {
            return View();
        }
    }
}

namespace AliceIdentityService.Models
{
    public class LoginInputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; }

        [Required, DataType(DataType.Password)]
        public string Password { get; set; }

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }

    public class RegistrationInputModel
    {
        [Required, MaxLength(255), Display(Name = "First Name")]
        public string FirstName { get; set; }

        [Required, MaxLength(255), Display(Name = "Last Name")]
        public string LastName { get; set; }

        [Required, EmailAddress]
        public string Email { get; set; }

        [Required, DataType(DataType.Password)]
        public string Password { get; set; }

        [Required, DataType(DataType.Password), Display(Name = "Confirm Password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; }
    }

    public class ResetPasswordInputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; }

        [Required, DataType(DataType.Password), Display(Name = "New Password")]
        public string Password { get; set; }

        [Required, DataType(DataType.Password), Display(Name = "Confirm Password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; }

        [Required]
        public string Code { get; set; }
    }
}
using System.Security.Claims;
using System.Text.Json;
using AliceIdentityService.Helpers;
using AliceIdentityService.Models;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using OpenIddict.EntityFrameworkCore.Models;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AliceIdentityService.Controllers;

// Taken from the Velusia sample of OpenIddict at
// https://github.com/openiddict/openiddict-samples/tree/dev/samples/Velusia/Velusia.Server/Controllers
// with some changes on claims inclusion.
public class AuthorizationController : Controller
{
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictAuthorizationManager _authorizationManager;
    private readonly OpenIddictScopeManager<OpenIddictEntityFrameworkCoreScope> _scopeManager;
    private readonly SignInManager<User> _signInManager;
    private readonly UserManager<User> _userManager;

    public AuthorizationController(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictAuthorizationManager authorizationManager,
        OpenIddictScopeManager<OpenIddictEntityFrameworkCoreScope> scopeManager,
        SignInManager<User> signInManager,
        UserManager<User> userManager)
    {
        _applicationManager = applicationManager;
        _authorizationManager = authorizationManager;
        _scopeManager = scopeManager;
        _signInManager = signInManager;
        _userManager = userManager;
    }

    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // If prompt=login was specified by the client application,
        // immediately return the user agent to the login page.
        if (request.HasPrompt(Prompts.Login))
        {
            // To avoid endless login -> authorization redirects, the prompt=login flag
            // is removed from the authorization request payload before redirecting the user.
            var prompt = string.Join(" ", request.GetPrompts().Remove(Prompts.Login));

            var parameters = Request.HasFormContentType ?
                Request.Form.Where(parameter => parameter.Key != Parameters.Prompt).ToList() :
                Request.Query.Where(parameter => parameter.Key != Parameters.Prompt).ToList();

            parameters.Add(KeyValuePair.Create(Parameters.Prompt, new StringValues(prompt)));

            return Challenge(
                authenticationSchemes: IdentityConstants.ApplicationScheme,
                properties: new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + QueryString.Create(parameters)
                });
        }

        // Retrieve the user principal stored in the authentication cookie.
        // If a max_age parameter was provided, ensure that the cookie is not too old.
        // If the user principal can't be extracted or the cookie is too old, redirect the user to the login page.
        var result = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (result == null || !result.Succeeded || (request.MaxAge != null && result.Properties?.IssuedUtc != null &&
            DateTimeOffset.UtcNow - result.Properties.IssuedUtc > TimeSpan.FromSeconds(request.MaxAge.Value)))
        {
            // If the client application requested promptless authentication,
            // return an error indicating that the user is not logged in.
            if (request.HasPrompt(Prompts.None))
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.LoginRequired,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user is not logged in."
                    }));
            }

            return Challenge(
                authenticationSchemes: IdentityConstants.ApplicationScheme,
                properties: new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + QueryString.Create(
                        Request.HasFormContentType ? Request.Form.ToList() : Request.Query.ToList())
                });
        }

        // Retrieve the profile of the logged in user.
        var user = await _userManager.GetUserAsync(result.Principal) ??
            throw new InvalidOperationException("The user details cannot be retrieved.");

        // Retrieve the application details from the database.
        var application = await _applicationManager.FindByClientIdAsync(request.ClientId) ??
            throw new InvalidOperationException("Details concerning the calling client application cannot be found.");

        // Retrieve the permanent authorizations associated with the user and the calling client application.
        var authorizations = await _authorizationManager.FindAsync(
            subject: await _userManager.GetUserIdAsync(user),
            client: await _applicationManager.GetIdAsync(application),
            status: Statuses.Valid,
            type: AuthorizationTypes.Permanent,
            scopes: request.GetScopes()).ToListAsync();

        switch (await _applicationManager.GetConsentTypeAsync(application))
        {
            // If the consent is external (e.g when authorizations are granted by a sysadmin),
            // immediately return an error if no authorization can be found in the database.
            case ConsentTypes.External when !authorizations.Any():
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.ConsentRequired,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "The logged in user is not allowed to access this client application."
                    }));

            // If the consent is implicit or if an authorization was found,
            // return an authorization response without displaying the consent form.
            case ConsentTypes.Implicit:
            case ConsentTypes.External when authorizations.Any():
            case ConsentTypes.Explicit when authorizations.Any() && !request.HasPrompt(Prompts.Consent):
                var principal = await _signInManager.CreateUserPrincipalAsync(user);

                // Note: in this sample, the granted scopes match the requested scope
                // but you may want to allow the user to uncheck specific scopes.
                // For that, simply restrict the list of scopes before calling SetScopes.
                principal.SetScopes(request.GetScopes());
                principal.SetResources(await _scopeManager.ListResourcesAsync(principal.GetScopes()).ToListAsync());

                // Automatically create a permanent authorization to avoid requiring explicit consent
                // for future authorization or token requests containing the same scopes.
                var authorization = authorizations.LastOrDefault();
                if (authorization == null)
                {
                    authorization = await _authorizationManager.CreateAsync(
                        principal: principal,
                        subject: await _userManager.GetUserIdAsync(user),
                        client: await _applicationManager.GetIdAsync(application),
                        type: AuthorizationTypes.Permanent,
                        scopes: principal.GetScopes());
                }

                principal.SetAuthorizationId(await _authorizationManager.GetIdAsync(authorization));

                await SetClaimDestinationsAsync(principal);

                return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            // At this point, no authorization was found in the database and an error must be returned
            // if the client application specified prompt=none in the authorization request.
            case ConsentTypes.Explicit when request.HasPrompt(Prompts.None):
            case ConsentTypes.Systematic when request.HasPrompt(Prompts.None):
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.ConsentRequired,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "Interactive user consent is required."
                    }));

            // In every other case, render the consent form.
            default:
                return View(new AuthorizeViewModel
                {
                    ApplicationName = await _applicationManager.GetDisplayNameAsync(application),
                    Scope = request.Scope
                });
        }
    }

    [Authorize, FormValueRequired("submit.Accept")]
    [HttpPost("~/connect/authorize"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // Retrieve the profile of the logged in user.
        var user = await _userManager.GetUserAsync(User) ??
            throw new InvalidOperationException("The user details cannot be retrieved.");

        // Retrieve the application details from the database.
        var application = await _applicationManager.FindByClientIdAsync(request.ClientId) ??
            throw new InvalidOperationException("Details concerning the calling client application cannot be found.");

        // Retrieve the permanent authorizations associated with the user and the calling client application.
        var authorizations = await _authorizationManager.FindAsync(
            subject: await _userManager.GetUserIdAsync(user),
            client: await _applicationManager.GetIdAsync(application),
            status: Statuses.Valid,
            type: AuthorizationTypes.Permanent,
            scopes: request.GetScopes()).ToListAsync();

        // Note: the same check is already made in the other action but is repeated
        // here to ensure a malicious user can't abuse this POST-only endpoint and
        // force it to return a valid response without the external authorization.
        if (!authorizations.Any() && await _applicationManager.HasConsentTypeAsync(application, ConsentTypes.External))
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.ConsentRequired,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                        "The logged in user is not allowed to access this client application."
                }));
        }

        var principal = await _signInManager.CreateUserPrincipalAsync(user);

        // Note: in this sample, the granted scopes match the requested scope
        // but you may want to allow the user to uncheck specific scopes.
        // For that, simply restrict the list of scopes before calling SetScopes.
        principal.SetScopes(request.GetScopes());
        principal.SetResources(await _scopeManager.ListResourcesAsync(principal.GetScopes()).ToListAsync());

        // Automatically create a permanent authorization to avoid requiring explicit consent
        // for future authorization or token requests containing the same scopes.
        var authorization = authorizations.LastOrDefault();
        if (authorization == null)
        {
            authorization = await _authorizationManager.CreateAsync(
                principal: principal,
                subject: await _userManager.GetUserIdAsync(user),
                client: await _applicationManager.GetIdAsync(application),
                type: AuthorizationTypes.Permanent,
                scopes: principal.GetScopes());
        }

        principal.SetAuthorizationId(await _authorizationManager.GetIdAsync(authorization));

        await SetClaimDestinationsAsync(principal);

        // Returning a SignInResult will ask OpenIddict to issue the appropriate access/identity tokens.
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [Authorize, FormValueRequired("submit.Deny")]
    [HttpPost("~/connect/authorize"), ValidateAntiForgeryToken]
    // Notify OpenIddict that the authorization grant has been denied by the resource owner
    // to redirect the user agent to the client application using the appropriate response_mode.
    public IActionResult Deny() => Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

    [HttpGet("~/connect/logout")]
    [HttpPost("~/connect/logout")]
    public async Task<IActionResult> Logout()
    {
        // Ask ASP.NET Core Identity to delete the local and external cookies created
        // when the user agent is redirected from the external identity provider
        // after a successful authentication flow (e.g Google or Facebook).
        await _signInManager.SignOutAsync();

        // Returning a SignOutResult will ask OpenIddict to redirect the user agent
        // to the post_logout_redirect_uri specified by the client application or to
        // the RedirectUri specified in the authentication properties if none was set.
        return SignOut(
            authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            properties: new AuthenticationProperties
            {
                RedirectUri = "/"
            });
    }

    [HttpPost("~/connect/token"), Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            // Retrieve the claims principal stored in the authorization code/device code/refresh token.
            var principal = (await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;

            // Retrieve the user profile corresponding to the authorization code/refresh token.
            // Note: if you want to automatically invalidate the authorization code/refresh token
            // when the user password/roles change, use the following line instead:
            // var user = _signInManager.ValidateSecurityStampAsync(info.Principal);
            var user = await _userManager.GetUserAsync(principal);
            if (user == null)
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The token is no longer valid."
                    }));
            }

            // Ensure the user is still allowed to sign in.
            if (!await _signInManager.CanSignInAsync(user))
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user is no longer allowed to sign in."
                    }));
            }

            await SetClaimDestinationsAsync(principal);

            // Returning a SignInResult will ask OpenIddict to issue the appropriate access/identity tokens.
            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        throw new InvalidOperationException("The specified grant type is not supported.");
    }

    private async Task SetClaimDestinationsAsync(ClaimsPrincipal principal)
    {
        var idTokenClaims = new HashSet<string>();
        var accessTokenClaims = new HashSet<string>();
        foreach (var scopeName in principal.GetScopes())
        {
            if (AisConstants.StandardScopes.ContainsKey(scopeName))
            {
                // openid claims like sub, iss, aud etc. are automatically added by OpenIddict
                if (scopeName == "openid") continue;

                idTokenClaims.UnionWith(AisConstants.StandardScopes[scopeName]);
                switch (scopeName)
                {
                    case "email":
                        accessTokenClaims.UnionWith(AisConstants.StandardScopes[scopeName]);
                        break;
                    case "profile":
                        accessTokenClaims.Add("name");
                        break;
                }
            }
            else
            {
                var scope = await _scopeManager.FindByNameAsync(scopeName);
                if (scope != null)
                {
                    using var document = JsonDocument.Parse(scope.Properties);
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if (property.Name == "claims")
                        {
                            var claims = property.Value.EnumerateArray().Select(e => e.GetString()).ToHashSet();
                            idTokenClaims.UnionWith(claims);
                            accessTokenClaims.UnionWith(claims);
                            break;
                        }
                    }
                }
            }
        }

        // According to https://documentation.openiddict.com/guides/migration/30-to-40.html, we are encouraged
        // to use principal.SetDEstinations(), though I don't quite see the difference between that and a
        // for loop like what we have here.
        foreach (var claim in principal.Claims)
        {
            if (idTokenClaims.Contains(claim.Type))
            {
                if (accessTokenClaims.Contains(claim.Type))
                    claim.SetDestinations(new string[] { Destinations.IdentityToken, Destinations.AccessToken });
                else
                    claim.SetDestinations(new string[] { Destinations.IdentityToken });
            }
            else if (idTokenClaims.Contains(claim.Type))
                claim.SetDestinations(new string[] { Destinations.AccessToken });
        }
    }

    private IEnumerable<string> GetDestinations(Claim claim, ClaimsPrincipal principal)
    {
        // Note: by default, claims are NOT automatically included in the access and identity tokens.
        // To allow OpenIddict to serialize them, you must attach them a destination, that specifies
        // whether they should be included in access tokens, in identity tokens or in both.

        switch (claim.Type)
        {
            case Claims.Name:
                yield return Destinations.AccessToken;

                if (principal.HasScope(Scopes.Profile))
                    yield return Destinations.IdentityToken;

                yield break;

            case Claims.Email:
                yield return Destinations.AccessToken;

                if (principal.HasScope(Scopes.Email))
                    yield return Destinations.IdentityToken;

                yield break;

            case Claims.Role:
                yield return Destinations.AccessToken;

                if (principal.HasScope(Scopes.Roles))
                    yield return Destinations.IdentityToken;

                yield break;

            // Never include the security stamp in the access and identity tokens, as it's a secret value.
            case "AspNet.Identity.SecurityStamp": yield break;

            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}
using System.ComponentModel.DataAnnotations;
using AliceIdentityService.Helpers;
using AliceIdentityService.Models;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using OpenIddict.EntityFrameworkCore.Models;

namespace AliceIdentityService.Controllers
{
    [Authorize(Policy = AisConstants.Policy.IsAdmin)]
    public class ClientController : Controller
    {
        private readonly OpenIddictScopeManager<OpenIddictEntityFrameworkCoreScope> _scopeManager;
        private readonly OpenIddictApplicationManager<OpenIddictEntityFrameworkCoreApplication> _applicationManager;

        private readonly IMapper _mapper;
        private readonly ILogger<ClientController> _logger;

        public ClientController(OpenIddictScopeManager<OpenIddictEntityFrameworkCoreScope> scopeManager,
            OpenIddictApplicationManager<OpenIddictEntityFrameworkCoreApplication> applicationManager,
            IMapper mapper, ILogger<ClientController> logger)
        {
            _scopeManager = scopeManager;
            _applicationManager = applicationManager;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<IActionResult> IndexAsync()
        {
            return View(await _applicationManager.ListAsync().ToListAsync());
        }

        [HttpGet]
        public async Task<IActionResult> ViewAsync(string id)
        {
            var client = await _applicationManager.FindByIdAsync(id);
            if (client == null) return NotFound();

            var descriptor = new OpenIddictApplicationDescriptor();
            await _applicationManager.PopulateAsync(descriptor, client);
            var allowedScopes = Utility.GetAllowedScopes(descriptor);
            var availableScopes = (await _scopeManager.ListAsync().ToListAsync())
                .Where(s => !allowedScopes.Contains(s.Name)).Select(s => s.Name);

            ViewBag.Client = client;
            ViewBag.Scopes = allowedScopes;
            ViewBag.AvailableScopes = availableScopes;

            return View(descriptor);
        }

        [HttpGet]
        public IActionResult Add()
        {
            return View(new ApplicationInputModel() { IsNewClientSecret = true });
        }

        [HttpPost]
        public async Task<IActionResult> AddAsync(ApplicationInputModel input)
        {
            if (!ModelState.IsValid) return View(input);

            var descriptor = new OpenIddictApplicationDescriptor
            {
                Permissions =
                {
                    OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                    OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                    OpenIddictConstants.Permissions.ResponseTypes.Code,
                    OpenIddictConstants.Permissions.Scopes.Profile,
                    OpenIddictConstants.Permissions.Scopes.Email
                },
                Requirements =
                {
                    OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange
                },
                ConsentType = OpenIddictConstants.ConsentTypes.Implicit
            };

            _mapper.Map(input, descriptor);

            var client = await _applicationManager.CreateAsync(descriptor);
            _logger.LogInformation("{user} created new client {client}", User.Identity.Name, client.ClientId);
            return RedirectToAction("View", new { id = client.Id });
        }

        [HttpGet]
        public async Task<IActionResult> EditAsync(string id)
        {
            var client = await _applicationManager.FindByIdAsync(id);
            if (client == null) return NotFound();

            var descriptor = new OpenIddictApplicationDescriptor();
            await _applicationManager.PopulateAsync(descriptor, client);
            var allowedScopes = Utility.GetAllowedScopes(descriptor);
            var availableScopes = (await _scopeManager.ListAsync().ToListAsync())
                .Where(s => !allowedScopes.Contains(s.Name)).Select(s => s.Name);

            ViewBag.Client = client;
            ViewBag.Scopes = allowedScopes;
            ViewBag.AvailableScopes = availableScopes;

            return View(_mapper.Map<ApplicationInputModel>(descriptor));
        }

        [HttpPost]
        public async Task<IActionResult> EditAsync(string id, ApplicationInputModel input)
        {
            if (!ModelState.IsValid) return View(input);

            var client = await _applicationManager.FindByIdAsync(id);
            if (client == null) return NotFound();

            var descriptor = new OpenIddictApplicationDescriptor();
            await _applicationManager.PopulateAsync(descriptor, client);

            _mapper.Map(input, descriptor);
            // It's not easy to map a bool to a readonly collection with Automapper so we just do it here.
            if (input.IsPkce) descriptor.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);
            else descriptor.Requirements.Remove(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);

            await _applicationManager.UpdateAsync(client, descriptor);
            _logger.LogInformation("{user} updated client {client}", User.Identity.Name, descriptor.ClientId);

            return RedirectToAction("View", new { id });
        }

        [HttpGet]
        public async Task<IActionResult> DeleteAsync(string id)
        {
            var client = await _applicationManager.FindByIdAsync(id);
            if (client == null) return NotFound();

            var clientId = client.ClientId;
            await _applicationManager.DeleteAsync(client);
            _logger.LogInformation("{user} deleted client {client}", User.Identity.Name, clientId);

            return RedirectToAction("Index");
        }

        public async Task<IActionResult> AddScopeAsync(string clientId, string scope)
        {
            var client = await _applicationManager.FindByIdAsync(clientId);
            if (scope == null) return NotFound();

            var descriptor = new OpenIddictApplicationDescriptor();
            await _applicationManager.PopulateAsync(descriptor, client);

            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + scope);

            await _applicationManager.UpdateAsync(client, descriptor);
            _logger.LogInformation("{user} added scope {scope} to {client}", User.Identity.Name, scope, client.ClientId);

            return RedirectToAction("View", new { id = clientId });
        }

        public async Task<IActionResult> RemoveScopeAsync(string clientId, string scope)
        {
            var client = await _applicationManager.FindByIdAsync(clientId);
            if (scope == null) return NotFound();

            var descriptor = new OpenIddictApplicationDescriptor();
            await _applicationManager.PopulateAsync(descriptor, client);

            descriptor.Permissions.Remove(OpenIddictConstants.Permissions.Prefixes.Scope + scope);

            await _applicationManager.UpdateAsync(client, descriptor);
            _logger.LogInformation("{user} removed scope {scope} from {client}", User.Identity.Name, scope, client.ClientId);

            return RedirectToAction("View", new { id = clientId });
        }

        public IActionResult GenerateSecret()
        {
            var secret = Utility.GenerateClientSecret();
            return new JsonResult(new { secret });
        }
    }
}

namespace AliceIdentityService.Models
{
    public class ApplicationInputModel
    {
        [Display(Name = "Display Name")]
        public string DisplayName { get; set; }

        [Required, MaxLength(100), Display(Name = "Client Id")]
        public string ClientId { get; set; }

        [Display(Name = "Client Secret")]
        public string ClientSecret { get; set; }

        [Display(Name = "Redirect URIs")]
        public string RedirectUris { get; set; }

        [Display(Name = "Post-Logout Redirect URIs")]
        public string PostLogoutRedirectUris { get; set; }

        public bool IsNewClientSecret { get; set; }

        [Display(Name = "PKCE")]
        public bool IsPkce { get; set; } = true;
    }
}
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using AliceIdentityService.Models;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using OpenIddict.EntityFrameworkCore.Models;

namespace AliceIdentityService.Controllers
{
    [Authorize(Policy = AisConstants.Policy.IsAdmin)]
    public class ScopeController : Controller
    {
        private readonly OpenIddictScopeManager<OpenIddictEntityFrameworkCoreScope> _scopeManager;

        private readonly IMapper _mapper;
        private readonly ILogger<ScopeController> _logger;

        public ScopeController(OpenIddictScopeManager<OpenIddictEntityFrameworkCoreScope> scopeManager,
            IMapper mapper, ILogger<ScopeController> logger)
        {
            _scopeManager = scopeManager;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<IActionResult> IndexAsync()
        {
            return View(await _scopeManager.ListAsync().ToListAsync());
        }

        [HttpGet]
        public async Task<IActionResult> ViewAsync(string id)
        {
            var scope = await _scopeManager.FindByIdAsync(id);
            if (scope == null) return NotFound();

            var descriptor = new OpenIddictScopeDescriptor();
            await _scopeManager.PopulateAsync(descriptor, scope);

            ViewBag.Scope = scope;
            ViewBag.Claims = descriptor.Properties["claims"].EnumerateArray().Select(e => e.GetString()).ToList();

            return View(descriptor);
        }

        [HttpGet]
        public IActionResult Add()
        {
            return View(new ScopeInputModel());
        }

        [HttpPost]
        public async Task<IActionResult> AddAsync(ScopeInputModel input)
        {
            if (!ModelState.IsValid) return View(input);

            var descriptor = _mapper.Map<OpenIddictScopeDescriptor>(input);
            descriptor.Properties["claims"] = JsonSerializer.SerializeToElement(new string[] { });
            var scope = await _scopeManager.CreateAsync(descriptor);
            _logger.LogInformation("{user} created new scope {scope}", User.Identity.Name, scope.Name);
            return RedirectToAction("View", new { id = scope.Id });
        }

        [HttpGet]
        public async Task<IActionResult> EditAsync(string id)
        {
            var scope = await _scopeManager.FindByIdAsync(id);
            if (scope == null) return NotFound();

            var descriptor = new OpenIddictScopeDescriptor();
            await _scopeManager.PopulateAsync(descriptor, scope);

            ViewBag.Scope = scope;
            ViewBag.Descriptor = descriptor;
            ViewBag.Claims = descriptor.Properties["claims"].EnumerateArray().Select(e => e.GetString()).ToList();

            return View(_mapper.Map<ScopeInputModel>(scope));
        }

        [HttpPost]
        public async Task<IActionResult> EditAsync(string id, ScopeInputModel input)
        {
            if (!ModelState.IsValid) return View(input);

            var scope = await _scopeManager.FindByIdAsync(id);
            if (scope == null) return NotFound();

            var descriptor = new OpenIddictScopeDescriptor();
            await _scopeManager.PopulateAsync(descriptor, scope);

            _mapper.Map(input, descriptor);
            await _scopeManager.UpdateAsync(scope, descriptor);
            _logger.LogInformation("{user} updated scope {scope}", User.Identity.Name, descriptor.Name);

            return RedirectToAction("View", new { id });
        }

        [HttpGet]
        public async Task<IActionResult> DeleteAsync(string id)
        {
            var scope = await _scopeManager.FindByIdAsync(id);
            if (scope == null) return NotFound();

            await _scopeManager.DeleteAsync(scope);
            _logger.LogInformation("{user} deleted scope {scope}", User.Identity.Name, id);

            return RedirectToAction("Index");
        }

        public async Task<IActionResult> AddClaimAsync(string scopeId, string claim)
        {
            var scope = await _scopeManager.FindByIdAsync(scopeId);
            if (scope == null) return NotFound();

            var descriptor = new OpenIddictScopeDescriptor();
            await _scopeManager.PopulateAsync(descriptor, scope);

            var claims = descriptor.Properties["claims"].EnumerateArray().Select(e => e.GetString()).ToList();
            claims.Add(claim);
            descriptor.Properties["claims"] = JsonSerializer.SerializeToElement(claims);

            await _scopeManager.UpdateAsync(scope, descriptor);
            _logger.LogInformation("{user} added claim {claim} to {scope}", User.Identity.Name, claim, scope.Name);

            return RedirectToAction("View", new { id = scopeId });
        }

        public async Task<IActionResult> RemoveClaimAsync(string scopeId, string claim)
        {
            var scope = await _scopeManager.FindByIdAsync(scopeId);
            if (scope == null) return NotFound();

            var descriptor = new OpenIddictScopeDescriptor();
            await _scopeManager.PopulateAsync(descriptor, scope);

            var claims = descriptor.Properties["claims"].EnumerateArray().Select(e => e.GetString()).ToList();
            claims.Remove(claim);
            descriptor.Properties["claims"] = JsonSerializer.SerializeToElement(claims);

            await _scopeManager.UpdateAsync(scope, descriptor);
            _logger.LogInformation("{user} removed claim {claim} from {scope}", User.Identity.Name, claim, scope.Name);

            return RedirectToAction("View", new { id = scopeId });
        }


        public async Task<IActionResult> AddResourceAsync(string scopeId, string resource)
        {
            var scope = await _scopeManager.FindByIdAsync(scopeId);
            if (scope == null) return NotFound();

            var descriptor = new OpenIddictScopeDescriptor();
            await _scopeManager.PopulateAsync(descriptor, scope);

            descriptor.Resources.Add(resource);
            await _scopeManager.UpdateAsync(scope, descriptor);
            _logger.LogInformation("{user} added resource {resource} to {scope}", User.Identity.Name, resource, scope.Name);

            return RedirectToAction("View", new { id = scopeId });
        }

        public async Task<IActionResult> RemoveResourceAsync(string scopeId, string resource)
        {
            var scope = await _scopeManager.FindByIdAsync(scopeId);
            if (scope == null) return NotFound();

            var descriptor = new OpenIddictScopeDescriptor();
            await _scopeManager.PopulateAsync(descriptor, scope);

            descriptor.Resources.Remove(resource);

            await _scopeManager.UpdateAsync(scope, descriptor);
            _logger.LogInformation("{user} removed resource {resource} from {scope}", User.Identity.Name, resource, scope.Name);

            return RedirectToAction("View", new { id = scopeId });
        }
    }
}

namespace AliceIdentityService.Models
{
    public class ScopeInputModel
    {
        [Required, MaxLength(200)]
        public string Name { get; set; }

        [Display(Name = "Display Name")]
        public string DisplayName { get; set; }

        public string Description { get; set; }
    }
}
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AliceIdentityService.Models;
using AliceIdentityService.Services;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Core;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AliceIdentityService.Controllers
{
    [Authorize(Policy = AisConstants.Policy.IsAdmin)]
    public class UserController : Controller
    {
        private readonly UserService _userService;
        private readonly UserManager<User> _userManager;
        private readonly OpenIddictTokenManager<OpenIddictEntityFrameworkCoreToken> _tokenManager;

        private readonly IMapper _mapper;
        private readonly ILogger<UserController> _logger;

        public UserController(UserService userService, UserManager<User> userManager,
            OpenIddictTokenManager<OpenIddictEntityFrameworkCoreToken> tokenManager,
            IMapper mapper, ILogger<UserController> logger)
        {
            _userService = userService;
            _userManager = userManager;
            _tokenManager = tokenManager;
            _mapper = mapper;
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View(_userService.GetCounts());
        }

        public IActionResult Recent()
        {
            return View(_userService.GetRecentUsers());
        }

        public IActionResult Unconfirmed()
        {
            return View(_userService.GetUnconfirmedUsers());
        }

        public List<User> Autocomplete(string searchText)
        {
            return _userService.SearchUsersByPrefix(searchText, 10);
        }

        public async Task<IActionResult> ViewAsync(string id)
        {
            var user = _userService.GetUser(id);
            if (user == null) return NotFound();

            ViewBag.Claims = await _userManager.GetClaimsAsync(user);

            return View(user);
        }

        [HttpGet]
        public IActionResult Add()
        {
            return View(new RegistrationInputModel());
        }

        [HttpPost]
        public async Task<IActionResult> AddAsync(RegistrationInputModel input)
        {
            if (!ModelState.IsValid) return View(input);

            var user = _mapper.Map<User>(input);
            user.UserName = input.Email;
            user.ScreenName = $"{input.FirstName} {input.LastName}";
            user.EmailConfirmed = true;
            var result = await _userManager.CreateAsync(user, input.Password);
            if (result.Succeeded)
            {
                _logger.LogInformation("{user} created account for {newUser}", User.Identity.Name, input.Email);
                return RedirectToAction("View", new { id = user.Id });
            }
            else
            {
                foreach (var error in result.Errors)
                    ModelState.AddModelError(string.Empty, error.Description);
                return View(input);
            }
        }

        [HttpGet]
        public async Task<IActionResult> EditAsync(string id)
        {
            var user = _userService.GetUser(id);
            if (user == null) return NotFound();

            ViewBag.Claims = await _userManager.GetClaimsAsync(user);

            return View(_mapper.Map<EditUserInputModel>(user));
        }

        [HttpPost]
        public async Task<IActionResult> EditAsync(string id, EditUserInputModel input)
        {
            if (!ModelState.IsValid) return View(input);

            var user = _userService.GetUser(id);

            if (!string.IsNullOrWhiteSpace(input.NewPassword))
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var result = await _userManager.ResetPasswordAsync(user, token, input.NewPassword);
                if (!result.Succeeded)
                {
                    foreach (var error in result.Errors)
                        ModelState.AddModelError("Password", error.Description);
                    return View(input);
                }
            }

            user.EmailConfirmed = input.EmailConfirmed;
            if (!string.IsNullOrWhiteSpace(input.FirstName))
                user.FirstName = input.FirstName;
            if (!string.IsNullOrWhiteSpace(input.LastName))
                user.LastName = input.LastName;
            if (!string.IsNullOrWhiteSpace(input.ScreenName))
                user.ScreenName = input.ScreenName;

            _userService.SaveChanges();

            _logger.LogInformation("{user} edited account {account}", User.Identity.Name, input.Email);

            return RedirectToAction("View", new { id = user.Id });
        }

        public async Task<IActionResult> DeleteAsync(string id)
        {
            if (User.FindFirstValue(Claims.Subject) == id)
            {
                return View("Error", new ErrorViewModel
                {
                    Message = "Cannot delete the current user."
                });
            }

            await foreach (var token in _tokenManager.FindBySubjectAsync(id))
            {
                if (!await _tokenManager.TryRevokeAsync(token))
                    _logger.LogWarning("Failed to revoke {token}", token.Id);
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            var result = await _userManager.DeleteAsync(user);
            if (result.Succeeded)
                _logger.LogInformation("{user} deleted account {account}", User.Identity.Name, id);
            else
                _logger.LogWarning("Failed to delete account {account}", id);

            return RedirectToAction("Index");
        }

        public async Task<IActionResult> AddClaimAsync(string userId, string claimType, string claimValue)
        {
            var user = _userService.GetUser(userId);
            var result = await _userManager.AddClaimAsync(user, new Claim(claimType?.Trim(), claimValue?.Trim()));
            if (result.Succeeded)
            {
                _logger.LogError("{user} added claim {claimType}={claimValue} to {account}",
                    User.Identity.Name, claimType, claimValue, userId);
            }
            else
            {
                _logger.LogError("Failed to add claim {claimType}={claimValue} to {account}: {errors}",
                        claimType, claimValue, userId, result.Errors);
            }
            return RedirectToAction("View", new { id = userId });
        }

        public async Task<IActionResult> RemoveClaimAsync(string userId, string claimType, string claimValue)
        {
            var user = _userService.GetUser(userId);
            var result = await _userManager.RemoveClaimAsync(user, new Claim(claimType, claimValue));
            if (result.Succeeded)
            {
                _logger.LogError("{user} removed claim {claimType}={claimValue} from {account}",
                    User.Identity.Name, claimType, claimValue, userId);
            }
            else
            {
                _logger.LogError("Failed to remove claim {claimType}={claimValue} from {account}: {errors}",
                        claimType, claimValue, userId, result.Errors);
            }
            return RedirectToAction("View", new { id = userId });
        }
    }
}

namespace AliceIdentityService.Models
{
    public class EditUserInputModel
    {
        public string Id { get; set; }

        [MaxLength(255), Display(Name = "First Name")]
        public string FirstName { get; set; }

        [MaxLength(255), Display(Name = "Last Name")]
        public string LastName { get; set; }

        [EmailAddress]
        public string Email { get; set; }

        [MaxLength(255), Display(Name = "Screen Name")]
        public string ScreenName { get; set; }

        [DataType(DataType.Password), Display(Name = "New Password")]
        public string NewPassword { get; set; }

        [DataType(DataType.Password), Display(Name = "Confirm New Password")]
        [Compare("NewPassword", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmNewPassword { get; set; }

        [Display(Name = "Email Confirmed")]
        public bool EmailConfirmed { get; set; }

        public string FullName => $"{FirstName} {LastName}";
    }
}
using System.Security.Claims;
using AliceIdentityService.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AliceIdentityService.Services;

public class AppUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<User>
{
    public AppUserClaimsPrincipalFactory(UserManager<User> userManager,
        IOptions<IdentityOptions> optionsAccessor) : base(userManager, optionsAccessor)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(User user)
    {
        // Claims in AspNetUserClaims are added here.
        var identity = await base.GenerateClaimsAsync(user);

        // Add the claims based on User properties. 
        var claims = new List<Claim>
        {
            new Claim(Claims.GivenName, user.FirstName),
            new Claim(Claims.FamilyName, user.LastName),
            new Claim(Claims.Nickname, user.ScreenName),
        };

        identity.AddClaims(claims);

        return identity;
    }
}
using AliceIdentityService.Models;
using Microsoft.EntityFrameworkCore;

namespace AliceIdentityService.Services;

public class UserService
{
    public enum CountType
    {
        Total,      // total # of the users
        Recent,     // # of users who registered/added in the last 30 days
        Unconfirmed // # of users whose emails are not confirmed
    }

    private readonly AppDbContext _db;

    public UserService(AppDbContext db)
    {
        _db = db;
    }

    public List<User> GetUsers() => _db.Users.AsNoTracking()
        .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
        .ToList();

    public List<User> GetRecentUsers(int days = 30) => _db.Users.AsNoTracking()
        .Where(u => u.CreationTime > DateTime.UtcNow.AddDays(-days))
        .OrderByDescending(u => u.CreationTime)
        .ToList();

    public List<User> GetUnconfirmedUsers() => _db.Users.AsNoTracking()
        .Where(u => !u.EmailConfirmed)
        .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
        .ToList();

    // maxResults=null for unlimited results
    public List<User> SearchUsersByPrefix(string prefix, int? maxResults = 100)
    {
        prefix = prefix?.Trim();
        if (prefix == null || prefix.Length < 2) return new List<User>();

        return _db.Users.FromSqlRaw("SELECT * FROM \"SearchUsers\"({0}, {1})",
            $"{prefix}%".ToLower(), maxResults).AsNoTracking().ToList();
    }

    public User GetUser(string id)
    {
        return _db.Users.Find(id);
    }

    public Dictionary<CountType, int> GetCounts() => new Dictionary<CountType, int>
        {
            { CountType.Total, _db.Users.Count() },
            { CountType.Recent, _db.Users.Where(u => u.CreationTime > DateTime.UtcNow.AddDays(-30)).Count() },
            { CountType.Unconfirmed, _db.Users.Where(u => !u.EmailConfirmed).Count() }
        };

    public void SaveChanges() => _db.SaveChanges();
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;

using TicketSalesApp.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using SpacetimeDB.Types;
using SpacetimeDB;
using Fido2NetLib;
using Fido2NetLib.Objects;
using System.Text.Json;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthenticationService _authService;
        private readonly IQRAuthenticationService _qrAuthService;
        private readonly IUserService _userService;
        private readonly ITotpService _totpService;
        private readonly IWebAuthnService _webAuthnService;
        private readonly IMagicLinkService _magicLinkService;
        private readonly IOpenIdConnectService _openIdConnectService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;
        private readonly IMemoryCache _cache;
        private readonly ISpacetimeDBService _spacetimeService;

        public AuthController(
            IAuthenticationService authService, 
            IQRAuthenticationService qrAuthService,
            IUserService userService,
            ITotpService totpService,
            IWebAuthnService webAuthnService,
            IMagicLinkService magicLinkService,
            IOpenIdConnectService openIdConnectService,
            IConfiguration configuration,
            ILogger<AuthController> logger,
            IMemoryCache cache,
            ISpacetimeDBService spacetimeService)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _qrAuthService = qrAuthService ?? throw new ArgumentNullException(nameof(qrAuthService));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _totpService = totpService ?? throw new ArgumentNullException(nameof(totpService));
            _webAuthnService = webAuthnService ?? throw new ArgumentNullException(nameof(webAuthnService));
            _magicLinkService = magicLinkService ?? throw new ArgumentNullException(nameof(magicLinkService));
            _openIdConnectService = openIdConnectService ?? throw new ArgumentNullException(nameof(openIdConnectService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
        }

        #region Traditional Authentication

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
        {
            // this is a mess from the deepest pit of the seven hells and i doubt this shit wont crash and burn even if i fix all the errors
            try
            {
                _logger.LogInformation("Login attempt for user: {Username}", request.Username);

                if (!ModelState.IsValid)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid request data",
                        Errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage)).ToList()
                    });
                }

                // Authenticate user
                var user = await _authService.AuthenticateAsync(request.Username, request.Password);
                if (user == null)
                {
                    _logger.LogWarning("Authentication failed for user: {Username}", request.Username);
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid username or password"
                    });
                }

                // Check if 2FA is enabled
                bool totpEnabled = await _totpService.IsTotpEnabledAsync(user.UserId);
                bool webAuthnEnabled = await _webAuthnService.IsWebAuthnEnabledAsync(user.UserId);

                if (totpEnabled && !request.SkipTwoFactor)
                {
                    // Generate temporary token for 2FA
                    var tempToken = GenerateTemporaryToken();
                    
                    // Store token in database with expiry
                    var conn = _spacetimeService.GetConnection();
                    await conn.Reducers.CreateTwoFactorToken(
                        user.UserId,
                        tempToken,
                        (ulong)new DateTimeOffset(DateTime.UtcNow.AddMinutes(10)).ToUnixTimeMilliseconds(),
                        Request.Headers["User-Agent"].ToString(),
                        HttpContext.Connection.RemoteIpAddress?.ToString()
                    );
                    
                    return Ok(new ApiResponse<TwoFactorResponse>
                    {
                        Success = true,
                        Message = "Two-factor authentication required",
                        Data = new TwoFactorResponse
                        {
                            RequiresTwoFactor = true,
                            TwoFactorType = "totp",
                            TempToken = tempToken
                        }
                    });
                }
                
                if (webAuthnEnabled && !request.SkipTwoFactor)
                {
                    // Generate temporary token for WebAuthn
                    var tempToken = GenerateTemporaryToken();
                    
                    // Store token in database with expiry
                    var conn = _spacetimeService.GetConnection();
                    await conn.Reducers.CreateTwoFactorToken(
                        user.UserId,
                        tempToken,
                        (ulong)new DateTimeOffset(DateTime.UtcNow.AddMinutes(10)).ToUnixTimeMilliseconds(),
                        Request.Headers["User-Agent"].ToString(),
                        HttpContext.Connection.RemoteIpAddress?.ToString()
                    );
                    
                    // Get WebAuthn assertion options
                    var (success, options, errorMessage) = await _webAuthnService.GetAssertionOptionsAsync(request.Username);
                    if (!success || options == null)
                    {
                        return BadRequest(new ApiResponse<object>
                        {
                            Success = false,
                            Message = errorMessage ?? "Failed to get WebAuthn options"
                        });
                    }
                    
                    return Ok(new ApiResponse<WebAuthnTwoFactorResponse>
                    {
                        Success = true,
                        Message = "WebAuthn authentication required",
                        Data = new WebAuthnTwoFactorResponse
                        {
                            RequiresTwoFactor = true,
                            TwoFactorType = "webauthn",
                            TempToken = tempToken,
                            Options = options
                        }
                    });
                }

                // Generate JWT token
                var token = GenerateJwtToken(user);

                return Ok(new ApiResponse<LoginResponse>
                {
                    Success = true,
                    Message = "Authentication successful",
                    Data = new LoginResponse
                    {
                        Token = token,
                        User = new UserDto
                        {
                            Id = user.LegacyUserId,
                            Username = user.Login,
                            Email = user.Email,
                            PhoneNumber = user.PhoneNumber,
                            Role = _authService.GetUserRole(user.UserId)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for user: {Username}", request.Username);
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred during login"
                });
            }
        }

        [HttpPost("register")]
        [Authorize(Roles = "Administrator")]
        public async Task<ActionResult<RegisterResponse>> Register([FromBody] RegisterRequest request)
        {
            try
            {
                _logger.LogInformation("Registration attempt for user: {Username}", request.Username);

                if (!ModelState.IsValid)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid request data",
                        Errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage)).ToList()
                    });
                }

                // Check if user already exists
                var existingUser = await _userService.GetUserByLoginAsync(request.Username);
                if (existingUser != null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Username already exists"
                    });
                }

                // Register user
                var success = await _authService.RegisterAsync(
                    request.Username,
                    request.Password,
                    request.Role,
                    request.Email,
                    request.PhoneNumber
                );

                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Failed to register user"
                    });
                }

                // Get the newly created user
                var newUser = await _userService.GetUserByLoginAsync(request.Username);
                if (newUser == null)
                {
                    return StatusCode(500, new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User was created but could not be retrieved"
                    });
                }

                return Ok(new ApiResponse<RegisterResponse>
                {
                    Success = true,
                    Message = "User registered successfully",
                    Data = new RegisterResponse
                    {
                        User = new UserDto
                        {
                            Id = newUser.LegacyUserId,
                            Username = newUser.Login,
                            Email = newUser.Email,
                            PhoneNumber = newUser.PhoneNumber,
                            Role = _authService.GetUserRole(newUser.UserId)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration for user: {Username}", request.Username);
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred during registration"
                });
            }
        }

        #endregion

        #region TOTP (Time-based One-Time Password)

        [HttpPost("totp/setup")]
        [Authorize]
        public async Task<ActionResult<TotpSetupResponse>> SetupTotp()
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                var user = await _userService.GetUserByIdAsync(userId);
                if (user == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });
                }

                // Generate TOTP setup
                var setupResult = await _totpService.SetupTotpAsync(userId, user.Login);
                bool success = setupResult.success;
                string? secretKey = setupResult.secretKey;
                string? qrCodeUri = setupResult.qrCodeUri;
                string? errorMessage = setupResult.errorMessage;
                if (!success || secretKey == null || qrCodeUri == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to set up TOTP"
                    });
                }

                return Ok(new ApiResponse<TotpSetupResponse>
                {
                    Success = true,
                    Message = "TOTP setup successful",
                    Data = new TotpSetupResponse
                    {
                        SecretKey = secretKey,
                        QrCodeUri = qrCodeUri
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting up TOTP");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while setting up TOTP"
                });
            }
        }

        [HttpPost("totp/verify")]
        [Authorize]
        public async Task<ActionResult<VerifyTotpResponse>> VerifyTotp([FromBody] VerifyTotpRequest request)
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                // Verify TOTP code
                var (success, errorMessage) = await _totpService.EnableTotpAsync(userId, request.Code, request.SecretKey);
                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to verify TOTP code"
                    });
                }

                return Ok(new ApiResponse<VerifyTotpResponse>
                {
                    Success = true,
                    Message = "TOTP verification successful",
                    Data = new VerifyTotpResponse
                    {
                        Enabled = true
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying TOTP");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while verifying TOTP"
                });
            }
        }

        [HttpPost("totp/disable")]
        [Authorize]
        public async Task<ActionResult<DisableTotpResponse>> DisableTotp()
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                // Disable TOTP
                var (success, errorMessage) = await _totpService.DisableTotpAsync(userId);
                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to disable TOTP"
                    });
                }

                return Ok(new ApiResponse<DisableTotpResponse>
                {
                    Success = true,
                    Message = "TOTP disabled successfully",
                    Data = new DisableTotpResponse
                    {
                        Disabled = true
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disabling TOTP");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while disabling TOTP"
                });
            }
        }

        
        [HttpPost("totp/validate")]
        [AllowAnonymous]
        public async Task<ActionResult<ValidateTotpResponse>> ValidateTotp([FromBody] ValidateTotpRequest request)
        {
            try
            {
                // Validate TOTP with token
                var (success, errorMessage) = await _totpService.ValidateTotpWithTokenAsync(request.TempToken, request.Code);
                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to validate TOTP code"
                    });
                }

                // Get user from token
                var conn = _spacetimeService.GetConnection();
                var twoFactorToken = conn.Db.TwoFactorToken.Iter()
                    .FirstOrDefault(t => t.Token == request.TempToken && 
                    t.ExpiresAt > (ulong)new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds() && 
                    !t.IsUsed);
                
                if (twoFactorToken == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid or expired token"
                    });
                }

                var user = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.UserId.Equals(twoFactorToken.UserId));
                
                if (user == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });
                }

                // Delete the token
                await conn.Reducers.DeleteTwoFactorToken(twoFactorToken.Id);

                // Generate JWT token
                var token = GenerateJwtToken(user);

                return Ok(new ApiResponse<ValidateTotpResponse>
                {
                    Success = true,
                    Message = "TOTP validation successful",
                    Data = new ValidateTotpResponse
                    {
                        Token = token,
                        User = new UserDto
                        {
                            Id = user.LegacyUserId,
                            Username = user.Login,
                            Email = user.Email,
                            PhoneNumber = user.PhoneNumber,
                            Role = _authService.GetUserRole(user.UserId)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating TOTP");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while validating TOTP"
                });
            }
        }

        #endregion

        #region WebAuthn (FIDO2)

        [HttpPost("webauthn/register/options")]
        [Authorize]
        public async Task<ActionResult<WebAuthnRegisterOptionsResponse>> GetWebAuthnRegisterOptions()
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                var user = await _userService.GetUserByIdAsync(userId);
                if (user == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });
                }

                // Get WebAuthn registration options
                var (success, options, errorMessage) = await _webAuthnService.GetCredentialCreateOptionsAsync(userId, user.Login);
                if (!success || options == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to get WebAuthn registration options"
                    });
                }

                return Ok(new ApiResponse<WebAuthnRegisterOptionsResponse>
                {
                    Success = true,
                    Message = "WebAuthn registration options generated",
                    Data = new WebAuthnRegisterOptionsResponse
                    {
                        Options = options
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting WebAuthn registration options");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while getting WebAuthn registration options"
                });
            }
        }

        [HttpPost("webauthn/register/complete")]
        [Authorize]
        public async Task<ActionResult<WebAuthnRegisterCompleteResponse>> CompleteWebAuthnRegistration([FromBody] WebAuthnRegisterCompleteRequest request)
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                var user = await _userService.GetUserByIdAsync(userId);
                if (user == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });
                }

                // Complete WebAuthn registration
                var (success, errorMessage) = await _webAuthnService.CompleteRegistrationAsync(userId, user.Login, request.AttestationResponse);
                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to complete WebAuthn registration"
                    });
                }

                return Ok(new ApiResponse<WebAuthnRegisterCompleteResponse>
                {
                    Success = true,
                    Message = "WebAuthn registration completed successfully",
                    Data = new WebAuthnRegisterCompleteResponse
                    {
                        Registered = true
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing WebAuthn registration");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while completing WebAuthn registration"
                });
            }
        }

        [HttpPost("webauthn/login/options")]
        [AllowAnonymous]
        public async Task<ActionResult<WebAuthnLoginOptionsResponse>> GetWebAuthnLoginOptions([FromBody] WebAuthnLoginOptionsRequest request)
        {
            try
            {
                // Get WebAuthn assertion options
                var (success, options, errorMessage) = await _webAuthnService.GetAssertionOptionsAsync(request.Username);
                if (!success || options == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to get WebAuthn login options"
                    });
                }

                return Ok(new ApiResponse<WebAuthnLoginOptionsResponse>
                {
                    Success = true,
                    Message = "WebAuthn login options generated",
                    Data = new WebAuthnLoginOptionsResponse
                    {
                        Options = options
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting WebAuthn login options");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while getting WebAuthn login options"
                });
            }
        }

        [HttpPost("webauthn/login/complete")]
        [AllowAnonymous]
        public async Task<ActionResult<WebAuthnLoginCompleteResponse>> CompleteWebAuthnLogin([FromBody] WebAuthnLoginCompleteRequest request)
        {
            try
            {
                // Complete WebAuthn login
                var (success, user, errorMessage) = await _webAuthnService.CompleteAssertionAsync(request.Username, request.AssertionResponse);
                if (!success || user == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to complete WebAuthn login"
                    });
                }

                // Generate JWT token
                var token = GenerateJwtToken(user);

                return Ok(new ApiResponse<WebAuthnLoginCompleteResponse>
                {
                    Success = true,
                    Message = "WebAuthn login completed successfully",
                    Data = new WebAuthnLoginCompleteResponse
                    {
                        Token = token,
                        User = new UserDto
                        {
                            Id = user.LegacyUserId,
                            Username = user.Login,
                            Email = user.Email,
                            PhoneNumber = user.PhoneNumber,
                            Role = _authService.GetUserRole(user.UserId)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing WebAuthn login");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while completing WebAuthn login"
                });
            }
        }

        [HttpPost("webauthn/validate")]
        [AllowAnonymous]
        public async Task<ActionResult<WebAuthnValidateResponse>> ValidateWebAuthn([FromBody] WebAuthnValidateRequest request)
        {
            try
            {
                // Get user from token
                var conn = _spacetimeService.GetConnection();
                var twoFactorToken = conn.Db.TwoFactorToken.Iter()
                    .FirstOrDefault(t => t.Token == request.TempToken && t.ExpiresAt > (ulong)new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds() && !t.IsUsed);
                
                if (twoFactorToken == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid or expired token"
                    });
                }

                var user = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.UserId.Equals(twoFactorToken.UserId));
                
                if (user == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });
                }

                // Complete WebAuthn assertion
                var (success, _, errorMessage) = await _webAuthnService.CompleteAssertionAsync(user.Login, request.AssertionResponse);
                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to validate WebAuthn"
                    });
                }

                // Delete the token
                await conn.Reducers.DeleteTwoFactorToken(twoFactorToken.Id);

                // Generate JWT token
                var token = GenerateJwtToken(user);

                return Ok(new ApiResponse<WebAuthnValidateResponse>
                {
                    Success = true,
                    Message = "WebAuthn validation successful",
                    Data = new WebAuthnValidateResponse
                    {
                        Token = token,
                        User = new UserDto
                        {
                            Id = user.LegacyUserId,
                            Username = user.Login,
                            Email = user.Email,
                            PhoneNumber = user.PhoneNumber,
                            Role = _authService.GetUserRole(user.UserId)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating WebAuthn");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while validating WebAuthn"
                });
            }
        }

        [HttpGet("webauthn/credentials")]
        [Authorize]
        public async Task<ActionResult<WebAuthnCredentialsResponse>> GetWebAuthnCredentials()
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                // Get WebAuthn credentials
                var credentials = await _webAuthnService.GetUserCredentialsAsync(userId);

                return Ok(new ApiResponse<WebAuthnCredentialsResponse>
                {
                    Success = true,
                    Message = "WebAuthn credentials retrieved successfully",
                    Data = new WebAuthnCredentialsResponse
                    {
                        Credentials = credentials.Select(c => new WebAuthnCredentialDto
                        {
                            Id = Convert.ToBase64String(c.CredentialId),
                            CreatedAt = c.CreatedAt
                        }).ToList()
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting WebAuthn credentials");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while getting WebAuthn credentials"
                });
            }
        }

        [HttpDelete("webauthn/credentials/{id}")]
        [Authorize]
        public async Task<ActionResult<WebAuthnRemoveCredentialResponse>> RemoveWebAuthnCredential(string id)
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                // Remove WebAuthn credential
                var (success, errorMessage) = await _webAuthnService.RemoveCredentialAsync(userId, id);
                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to remove WebAuthn credential"
                    });
                }

                return Ok(new ApiResponse<WebAuthnRemoveCredentialResponse>
                {
                    Success = true,
                    Message = "WebAuthn credential removed successfully",
                    Data = new WebAuthnRemoveCredentialResponse
                    {
                        Removed = true
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing WebAuthn credential");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while removing WebAuthn credential"
                });
            }
        }

        #endregion

        #region Magic Link

        [HttpPost("magic-link/send")]
        [AllowAnonymous]
        public async Task<ActionResult<MagicLinkResponse>> SendMagicLink([FromBody] MagicLinkRequest request)
        {
            try
            {
                // Get client info
                var userAgent = Request.Headers["User-Agent"].ToString();
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

                // Send magic link
                var (success, errorMessage) = await _magicLinkService.SendMagicLinkAsync(request.Email, userAgent, ipAddress);
                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to send magic link"
                    });
                }

                return Ok(new ApiResponse<MagicLinkResponse>
                {   
                    Success = true,
                    Message = "Magic link sent successfully",
                    Data = new MagicLinkResponse
                    {
                        Sent = true,
                        Email = request.Email
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending magic link");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while sending magic link"
                });
            }
        }

        [HttpGet("validate-magic-link")]
        [AllowAnonymous]
        public async Task<ActionResult> ValidateMagicLink([FromQuery] string token)
        {
            try
            {
                // Validate magic link token
                var (success, user, errorMessage) = await _magicLinkService.ValidateMagicLinkAsync(token);
                if (!success || user == null)
                {
                    // Redirect to error page
                    return Redirect($"/auth/error?message={Uri.EscapeDataString(errorMessage ?? "Invalid or expired magic link")}");
                }

                // Mark token as used
                await _magicLinkService.MarkMagicLinkAsUsedAsync(token);

                // Generate JWT token
                var jwtToken = GenerateJwtToken(user);

                // Redirect to success page with token
                return Redirect($"/auth/success?token={Uri.EscapeDataString(jwtToken)}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating magic link");
                return Redirect($"/auth/error?message={Uri.EscapeDataString("An error occurred while validating magic link")}");
            }
        }

        [HttpPost("validate-magic-link")]
        [AllowAnonymous]
        public async Task<ActionResult<ValidateMagicLinkResponse>> ValidateMagicLinkApi([FromBody] ValidateMagicLinkRequest request)
        {
            try
            {
                // Validate magic link token
                var (success, user, errorMessage) = await _magicLinkService.ValidateMagicLinkAsync(request.Token);
                if (!success || user == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Invalid or expired magic link"
                    });
                }

                // Mark token as used
                await _magicLinkService.MarkMagicLinkAsUsedAsync(request.Token);

                // Generate JWT token
                var token = GenerateJwtToken(user);

                return Ok(new ApiResponse<ValidateMagicLinkResponse>
                {
                    Success = true,
                    Message = "Magic link validated successfully",
                    Data = new ValidateMagicLinkResponse
                    {
                        Token = token,
                        User = new UserDto
                        {
                            Id = user.LegacyUserId,
                            Username = user.Login,
                            Email = user.Email,
                            PhoneNumber = user.PhoneNumber,
                            Role = _authService.GetUserRole(user.UserId)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating magic link");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while validating magic link"
                });
            }
        }

        #endregion

        #region QR Code Authentication

        [HttpGet("qr/generate")]
        [Authorize]
        public async Task<ActionResult<QrCodeResponse>> GenerateQRCode()
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                var user = await _userService.GetUserByIdAsync(userId);
                if (user == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });
                }

                // Generate QR code
                var qrResult = await _qrAuthService.GenerateQRCodeAsync(userId);
                string qrCodeBase64 = qrResult.qrCode;
                string? rawData = qrResult.rawData;

                return Ok(new ApiResponse<QrCodeResponse>
                {
                    Success = true,
                    Message = "QR code generated successfully",
                    Data = new QrCodeResponse
                    {
                        QrCode = qrCodeBase64,
                        RawData = rawData
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating QR code");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while generating QR code"
                });
            }
        }

        [HttpPost("qr/login")]
        [AllowAnonymous]
        public async Task<ActionResult<QrLoginResponse>> QRLogin([FromBody] QrLoginRequest request)
        {
            try
            {
                // Authenticate with QR code
                var user = await _authService.AuthenticateDirectQRAsync(request.Username, request.Token);
                if (user == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid QR login credentials"
                    });
                }

                // Generate JWT token
                var token = GenerateJwtToken(user);

                return Ok(new ApiResponse<QrLoginResponse>
                {
                    Success = true,
                    Message = "QR login successful",
                    Data = new QrLoginResponse
                    {
                        Token = token,
                        User = new UserDto
                        {
                            Id = user.LegacyUserId,
                            Username = user.Login,
                            Email = user.Email,
                            PhoneNumber = user.PhoneNumber,
                            Role = _authService.GetUserRole(user.UserId)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during QR login");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred during QR login"
                });
            }
        }

        [HttpGet("qr/direct/generate")]
        [AllowAnonymous]
        public async Task<ActionResult<DirectQrCodeResponse>> GenerateDirectLoginQRCode([FromQuery] string username, [FromQuery] string deviceType)
        {
            try
            {
                // Validate user exists
                var user = await _userService.GetUserByLoginAsync(username);
                if (user == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User not found"
                    });
                }

                // Generate direct login QR code
                var (qrCode, rawData) = await _qrAuthService.GenerateDirectLoginQRCodeAsync(username, deviceType);

                return Ok(new ApiResponse<DirectQrCodeResponse>
                {
                    Success = true,
                    Message = "Direct login QR code generated successfully",
                    Data = new DirectQrCodeResponse
                    {
                        QrCode = qrCode,
                        RawData = rawData
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating direct login QR code");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while generating direct login QR code"
                });
            }
        }

        [HttpPost("qr/direct/login")]
        [AllowAnonymous]
        public async Task<ActionResult<DirectQrLoginResponse>> DirectQRLogin([FromBody] DirectQrLoginRequest request)
        {
            try
            {
                // Validate direct login token
                var (success, user, deviceId) = await _qrAuthService.ValidateDirectLoginTokenAsync(request.Token, request.DeviceType);
                if (!success || user == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid QR login token"
                    });
                }

                // Authenticate user without password
                var authenticatedUser = await _authService.AuthenticateDirectQRAsync(user.Login, deviceId);
                if (authenticatedUser == null)
                {
                    return Unauthorized(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Authentication failed"
                    });
                }

                // Generate JWT token
                var token = GenerateJwtToken(authenticatedUser);

                // If this is a mobile device scanning a desktop QR code, notify the desktop
                if (request.DeviceType == "mobile" && request.IsDesktopLogin)
                {
                    await _qrAuthService.NotifyDeviceLoginSuccessAsync(deviceId, token);
                }

                return Ok(new ApiResponse<DirectQrLoginResponse>
                {
                    Success = true,
                    Message = "Direct QR login successful",
                    Data = new DirectQrLoginResponse
                    {
                        Token = token,
                        DeviceId = deviceId,
                        User = new UserDto
                        {
                            Id = authenticatedUser.LegacyUserId,
                            Username = authenticatedUser.Login,
                            Email = authenticatedUser.Email,
                            PhoneNumber = authenticatedUser.PhoneNumber,
                            Role = _authService.GetUserRole(authenticatedUser.UserId)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during direct QR login");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred during direct QR login"
                });
            }
        }

        [HttpGet("qr/direct/check")]
        [AllowAnonymous]
        public async Task<ActionResult<CheckQrLoginResponse>> CheckDirectLoginStatus([FromQuery] string deviceId)
        {
            try
            {
                var loginSuccessKey = $"login_success_{deviceId}";
                if (_cache.TryGetValue(loginSuccessKey, out string token))
                {
                    _cache.Remove(loginSuccessKey); // One-time use
                    return Ok(new ApiResponse<CheckQrLoginResponse>
                    {
                        Success = true,
                        Message = "Login successful",
                        Data = new CheckQrLoginResponse
                        {
                            Success = true,
                            Token = token
                        }
                    });
                }

                return Ok(new ApiResponse<CheckQrLoginResponse>
                {
                    Success = true,
                    Message = "No login detected yet",
                    Data = new CheckQrLoginResponse
                    {
                        Success = false
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking direct login status");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while checking login status"
                });
            }
        }

        #endregion

        #region OpenID Connect

        [HttpGet("connect/authorize")]
        [AllowAnonymous]
        public IActionResult Authorize([FromQuery] string client_id, [FromQuery] string redirect_uri, [FromQuery] string response_type, [FromQuery] string scope, [FromQuery] string state, [FromQuery] string nonce)
        {
            try
            {
                // Store the request in cache for later retrieval
                var requestId = Guid.NewGuid().ToString();
                _cache.Set($"oidc_request_{requestId}", new OpenIdConnectRequest
                {
                    ClientId = client_id,
                    RedirectUri = redirect_uri,
                    ResponseType = response_type,
                    Scope = scope,
                    State = state,
                    Nonce = nonce
                }, TimeSpan.FromMinutes(10));

                // Redirect to login page with request ID
                return Redirect($"/oauth/login?request_id={requestId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing OpenID Connect authorization request");
                return StatusCode(500, "An error occurred while processing the authorization request");
            }
        }

        [HttpPost("connect/token")]
        [AllowAnonymous]
        public async Task<ActionResult<TokenResponse>> Token([FromForm] TokenRequest request)
        {
            try
            {
                if (request.GrantType == "authorization_code")
                {
                    // Validate authorization code
                    var codeData = _cache.Get<AuthorizationCodeData>($"auth_code_{request.Code}");
                    if (codeData == null)
                    {
                        return BadRequest(new
                        {
                            error = "invalid_grant",
                            error_description = "The authorization code is invalid or has expired."
                        });
                    }

                    // Get the user
                    var user = await _userService.GetUserByIdAsync(codeData.UserId);
                    if (user == null)
                    {
                        return BadRequest(new
                        {
                            error = "invalid_grant",
                            error_description = "The user associated with the authorization code no longer exists."
                        });
                    }

                    // Get the application
                    var (appSuccess, application, appError) = await _openIdConnectService.GetApplicationByClientIdAsync(request.ClientId);
                    if (!appSuccess || application == null)
                    {
                        return BadRequest(new
                        {
                            error = "invalid_client",
                            error_description = appError ?? "The client application is invalid."
                        });
                    }

                    // Validate client secret if provided
                    if (!string.IsNullOrEmpty(request.ClientSecret))
                    {
                        // Implement client secret validation here
                    }

                    // Create identity
                    var (identitySuccess, identity, identityError) = await _openIdConnectService.CreateIdentityFromUserAsync(user, codeData.Scopes);
                    if (!identitySuccess || identity == null)
                    {
                        return BadRequest(new
                        {
                            error = "server_error",
                            error_description = identityError ?? "Failed to create identity."
                        });
                    }

                    // Generate JWT token
                    var token = GenerateJwtToken(user);

                    // Remove the authorization code
                    _cache.Remove($"auth_code_{request.Code}");

                    return Ok(new TokenResponse
                    {
                        AccessToken = token,
                        TokenType = "Bearer",
                        ExpiresIn = int.Parse(_configuration["JwtSettings:ExpirationInMinutes"] ?? "120") * 60,
                        Scope = string.Join(" ", codeData.Scopes)
                    });
                }
                else if (request.GrantType == "refresh_token")
                {
                    // Implement refresh token flow
                    return BadRequest(new
                    {
                        error = "unsupported_grant_type",
                        error_description = "Refresh token flow is not implemented yet."
                        
                    });
                }
                else
                {
                    return BadRequest(new
                    {
                        error = "unsupported_grant_type",
                        error_description = "The specified grant type is not supported."
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing token request");
                return StatusCode(500, new
                {
                    error = "server_error",
                    error_description = "An error occurred while processing the token request."
                });
            }
        }

        [HttpPost("connect/authorize/callback")]
        [AllowAnonymous]
        public async Task<IActionResult> AuthorizeCallback([FromForm] AuthorizeCallbackRequest request)
        {
            try
            {
                // Get the original request from cache
                var originalRequest = _cache.Get<OpenIdConnectRequest>($"oidc_request_{request.RequestId}");
                if (originalRequest == null)
                {
                    return BadRequest("Invalid or expired request ID");
                }

                // Authenticate user
                var user = await _authService.AuthenticateAsync(request.Username, request.Password);
                if (user == null)
                {
                    return Redirect($"/oauth/login?request_id={request.RequestId}&error=invalid_credentials");
                }

                // Get the application
                var (appSuccess, application, appError) = await _openIdConnectService.GetApplicationByClientIdAsync(originalRequest.ClientId);
                if (!appSuccess || application == null)
                {
                    return BadRequest($"Invalid client: {appError}");
                }

                // Parse scopes
                var scopes = originalRequest.Scope.Split(' ');

                // Create authorization code
                var code = GenerateRandomToken();
                
                // Store the code data
                _cache.Set($"auth_code_{code}", new AuthorizationCodeData
                {
                    UserId = user.LegacyUserId,
                    Scopes = scopes,
                    RedirectUri = originalRequest.RedirectUri
                }, TimeSpan.FromMinutes(5));

                // Build the redirect URL
                var redirectUrl = $"{originalRequest.RedirectUri}?code={code}&state={originalRequest.State}";
                
                return Redirect(redirectUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing authorization callback");
                return StatusCode(500, "An error occurred while processing the authorization callback");
            }
        }

        [HttpGet("connect/userinfo")]
        [Authorize]
        public async Task<ActionResult<UserInfoResponse>> UserInfo()
        {
            try
            {
                var userId = GetUserIdentity();
                if (userId == null)
                {
                    return Unauthorized(new
                    {
                        error = "invalid_token",
                        error_description = "The access token is invalid or expired."
                    });
                }

                var user = await _userService.GetUserByIdAsync(userId);
                if (user == null)
                {
                    return NotFound(new
                    {
                        error = "invalid_token",
                        error_description = "The user associated with the access token no longer exists."
                    });
                }

                // Get user roles
                var conn = _spacetimeService.GetConnection();
                var userRoles = conn.Db.UserRole.Iter()
                    .Where(ur => ur.UserId.Equals(userId))
                    .Join(conn.Db.Role.Iter(), ur => ur.RoleId, r => r.RoleId, (ur, r) => r.Name)
                    .ToList();

                return Ok(new UserInfoResponse
                {
                    Sub = user.LegacyUserId.ToString(),
                    Name = user.Login,
                    PreferredUsername = user.Login,
                    Email = user.Email,
                    EmailVerified = user.EmailConfirmed,
                    PhoneNumber = user.PhoneNumber,
                    PhoneNumberVerified = user.PhoneNumberConfirmed,
                    Roles = userRoles
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user info");
                return StatusCode(500, new
                {
                    error = "server_error",
                    error_description = "An error occurred while getting user info."
                });
            }
        }

        [HttpPost("connect/register-client")]
        [Authorize(Roles = "Administrator")]
        public async Task<ActionResult<RegisterClientResponse>> RegisterClient([FromBody] RegisterClientRequest request)
        {
            try
            {
                // Register client application
                var (success, errorMessage) = await _openIdConnectService.RegisterClientApplicationAsync(
                    request.ClientId,
                    request.ClientSecret,
                    request.DisplayName,
                    request.RedirectUris,
                    request.PostLogoutRedirectUris,
                    request.AllowedScopes,
                    request.RequireConsent
                );

                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to register client application"
                    });
                }

                return Ok(new ApiResponse<RegisterClientResponse>
                {
                    Success = true,
                    Message = "Client application registered successfully",
                    Data = new RegisterClientResponse
                    {
                        ClientId = request.ClientId,
                        DisplayName = request.DisplayName
                    }
                });
                }
                catch (Exception ex)
                {
                _logger.LogError(ex, "Error registering client application");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while registering client application"
                });
            }
        }

        [HttpPut("connect/update-client/{clientId}")]
        [Authorize(Roles = "Administrator")]
        public async Task<ActionResult<UpdateClientResponse>> UpdateClient(string clientId, [FromBody] UpdateClientRequest request)
        {
            try
            {
                // Update client application
                var (success, errorMessage) = await _openIdConnectService.UpdateClientApplicationAsync(
                    clientId,
                    request.ClientSecret,
                    request.DisplayName,
                    request.RedirectUris,
                    request.PostLogoutRedirectUris,
                    request.AllowedScopes,
                    request.RequireConsent
                );

                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to update client application"
                    });
                }

                return Ok(new ApiResponse<UpdateClientResponse>
                {
                    Success = true,
                    Message = "Client application updated successfully",
                    Data = new UpdateClientResponse
                    {
                        ClientId = clientId,
                        DisplayName = request.DisplayName ?? "Unknown"
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating client application");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while updating client application"
                });
            }
        }

        [HttpDelete("connect/delete-client/{clientId}")]
        [Authorize(Roles = "Administrator")]
        public async Task<ActionResult<DeleteClientResponse>> DeleteClient(string clientId)
        {
            try
            {
                // Delete client application
                var (success, errorMessage) = await _openIdConnectService.DeleteClientApplicationAsync(clientId);

                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to delete client application"
                    });
                }

                return Ok(new ApiResponse<DeleteClientResponse>
                {
                    Success = true,
                    Message = "Client application deleted successfully",
                    Data = new DeleteClientResponse
                    {
                        ClientId = clientId,
                        Deleted = true
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting client application");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while deleting client application"
                });
            }
        }

        [HttpGet("connect/clients")]
        [Authorize(Roles = "Administrator")]
        public async Task<ActionResult<GetClientsResponse>> GetClients()
        {
            try
            {
                // Get all client applications
                var (success, applications, errorMessage) = await _openIdConnectService.GetAllClientApplicationsAsync();

                if (!success || applications == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to get client applications"
                    });
                }

                // Convert to DTO
                var clientDtos = new List<ClientDto>();
                foreach (var app in applications)
                {
                    var clientId = app.ClientId;
                    var displayName = app.DisplayName;
                    
                    clientDtos.Add(new ClientDto
                    {
                        ClientId = clientId,
                        DisplayName = displayName
                    });
                }

                return Ok(new ApiResponse<GetClientsResponse>
                {
                    Success = true,
                    Message = "Client applications retrieved successfully",
                    Data = new GetClientsResponse
                    {
                        Clients = clientDtos
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting client applications");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while getting client applications"
                });
            }
        }

        [HttpGet("connect/client/{clientId}")]
        [Authorize(Roles = "Administrator")]
        public async Task<ActionResult<GetClientResponse>> GetClient(string clientId)
        {
            try
            {
                // Get client application
                var (success, application, errorMessage) = await _openIdConnectService.GetClientApplicationAsync(clientId);

                if (!success || application == null)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = errorMessage ?? "Failed to get client application"
                    });
                }

                // Get client details
                var displayName = await _openIdConnectService.GetDisplayNameAsync(application);
                var redirectUris = await _openIdConnectService.GetRedirectUrisAsync(application);
                var postLogoutRedirectUris = await _openIdConnectService.GetPostLogoutRedirectUrisAsync(application);
                var permissions = await _openIdConnectService.GetPermissionsAsync(application);
                var consentType = await _openIdConnectService.GetConsentTypeAsync(application);

                return Ok(new ApiResponse<GetClientResponse>
                {
                    Success = true,
                    Message = "Client application retrieved successfully",
                    Data = new GetClientResponse
                    {
                        ClientId = clientId,
                        DisplayName = displayName,
                        RedirectUris = redirectUris.ToArray(),
                        PostLogoutRedirectUris = postLogoutRedirectUris.ToArray(),
                        AllowedScopes = permissions.ToArray(),
                        RequireConsent = consentType == "explicit"
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting client application");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "An error occurred while getting client application"
                });
            }
        }

        #endregion

        #region Helper Methods
        
        private string GenerateJwtToken(UserProfile userProfile)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var keyString = _configuration["JwtSettings:Secret"] ?? 
                throw new InvalidOperationException("JWT secret is not configured");

            // Ensure the key is at least 32 bytes
            var keyBytes = Encoding.UTF8.GetBytes(keyString);
            if (keyBytes.Length < 32)
            {
                Array.Resize(ref keyBytes, 32);
            }
            else if (keyBytes.Length > 64)
            {
                Array.Resize(ref keyBytes, 64);
            }

            var key = new SymmetricSecurityKey(keyBytes);
            var expirationMinutes = double.Parse(_configuration["JwtSettings:ExpirationInMinutes"] ?? "120");
            
            var conn = _spacetimeService.GetConnection();
            
            // Get user's roles
            var userRoles = conn.Db.UserRole.Iter()
                .Where(ur => ur.UserId.Equals(userProfile.UserId))
                .Select(ur => ur.RoleId)
                .ToList();
            
            // Get role details
            var roles = conn.Db.Role.Iter()
                .Where(r => userRoles.Contains(r.RoleId) && r.IsActive)
                .ToList();
            
            // Get role permissions
            var rolePermissions = conn.Db.RolePermission.Iter()
                .Where(rp => userRoles.Contains(rp.RoleId))
                .Select(rp => rp.PermissionId)
                .Distinct()
                .ToList();
            
            // Get permission details
            var permissions = conn.Db.Permission.Iter()
                .Where(p => rolePermissions.Contains(p.PermissionId) && p.IsActive)
                .ToList();
            
            // Create claims
            var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, userProfile.Login),
                new Claim("sub", userProfile.LegacyUserId.ToString()),
                new Claim("identity", userProfile.UserId.ToString()),
                new Claim("xuid", userProfile.Xuid.ToString() ?? "0")
            };
            
            // Add role claims
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role.Name));
                claims.Add(new Claim("role", role.LegacyRoleId.ToString())); // Keep legacy role ID for backward compatibility
            }
            
            // Add permission claims
            foreach (var permission in permissions)
            {
                claims.Add(new Claim("permission", permission.Name));
            }
            
            // Add highest priority role for IsAdmin checks
            var highestPriorityRole = roles.OrderByDescending(r => r.Priority).FirstOrDefault();
            if (highestPriorityRole != null)
            {
                claims.Add(new Claim("primary_role", highestPriorityRole.LegacyRoleId.ToString()));
            }

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(expirationMinutes),
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        private Identity? GetUserIdentity()
        {
            var identityString = User.FindFirst("identity")?.Value;
            if (string.IsNullOrEmpty(identityString))
            {
                return null;
            }

            try
            {
                if (Identity.TryParse(identityString, out var identity))
                {
                    return identity;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private string GenerateTemporaryToken()
        {
            var randomBytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }
            return Convert.ToBase64String(randomBytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
        }

        private string GenerateRandomToken()
        {
            var randomBytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }
            return Convert.ToBase64String(randomBytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
        }

        #endregion
    }

    #region Request Models

    public class LoginRequest
    {
        public required string Username { get; set; }
        public required string Password { get; set; }
        public bool SkipTwoFactor { get; set; } = false;
    }

    public class RegisterRequest
    {
        public required string Username { get; set; }
        public required string Password { get; set; }
        public int Role { get; set; } = 0;
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
    }

    public class VerifyTotpRequest
    {
        public required string Code { get; set; }
        public required string SecretKey { get; set; }
    }

    public class ValidateTotpRequest
    {
        public required string TempToken { get; set; }
        public required string Code { get; set; }
    }

    public class WebAuthnRegisterCompleteRequest
    {
        public required AuthenticatorAttestationRawResponse AttestationResponse { get; set; }
    }

    public class WebAuthnLoginOptionsRequest
    {
        public required string Username { get; set; }
    }

    public class WebAuthnLoginCompleteRequest
    {
        public required string Username { get; set; }
        public required AuthenticatorAssertionRawResponse AssertionResponse { get; set; }
    }

    public class WebAuthnValidateRequest
    {
        public required string TempToken { get; set; }
        public required AuthenticatorAssertionRawResponse AssertionResponse { get; set; }
    }

    public class MagicLinkRequest
    {
        public required string Email { get; set; }
    }

    public class ValidateMagicLinkRequest
    {
        public required string Token { get; set; }
    }

    public class QrLoginRequest
    {
        public required string Username { get; set; }
        public required string Token { get; set; }
    }

    public class DirectQrLoginRequest
    {
        public required string Token { get; set; }
        public required string DeviceType { get; set; }
        public bool IsDesktopLogin { get; set; }
    }

    public class TokenRequest
    {
        public required string GrantType { get; set; }
        public string? Code { get; set; }
        public string? RefreshToken { get; set; }
        public required string ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public string? RedirectUri { get; set; }
    }

    public class AuthorizeCallbackRequest
    {
        public required string RequestId { get; set; }
        public required string Username { get; set; }
        public required string Password { get; set; }
    }

    public class RegisterClientRequest
    {
        public required string ClientId { get; set; }
        public required string ClientSecret { get; set; }
        public required string DisplayName { get; set; }
        public required string[] RedirectUris { get; set; }
        public required string[] PostLogoutRedirectUris { get; set; }
        public required string[] AllowedScopes { get; set; }
        public bool RequireConsent { get; set; } = false;
    }

    public class UpdateClientRequest
    {
        public string? ClientSecret { get; set; }
        public string? DisplayName { get; set; }
        public string[]? RedirectUris { get; set; }
        public string[]? PostLogoutRedirectUris { get; set; }
        public string[]? AllowedScopes { get; set; }
        public bool? RequireConsent { get; set; }
    }

    #endregion

    #region Response Models

    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<string>? Errors { get; set; }
        public T? Data { get; set; }
    }

    public class UserDto
    {
        public uint Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public int Role { get; set; }
    }

    public class LoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new UserDto();
    }

    public class RegisterResponse
    {
        public UserDto User { get; set; } = new UserDto();
    }

    public class TwoFactorResponse
    {
        public bool RequiresTwoFactor { get; set; }
        public string TwoFactorType { get; set; } = string.Empty;
        public string TempToken { get; set; } = string.Empty;
    }

    public class WebAuthnTwoFactorResponse : TwoFactorResponse
    {
        public AssertionOptions? Options { get; set; }
    }

    public class TotpSetupResponse
    {
        public string SecretKey { get; set; } = string.Empty;
        public string QrCodeUri { get; set; } = string.Empty;
    }

    public class VerifyTotpResponse
    {
        public bool Enabled { get; set; }
    }

    public class DisableTotpResponse
    {
        public bool Disabled { get; set; }
    }

    public class ValidateTotpResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new UserDto();
    }

    public class WebAuthnRegisterOptionsResponse
    {
        public CredentialCreateOptions Options { get; set; } = new CredentialCreateOptions();
    }

    public class WebAuthnRegisterCompleteResponse
    {
        public bool Registered { get; set; }
    }

    public class WebAuthnLoginOptionsResponse
    {
        public AssertionOptions Options { get; set; } = new AssertionOptions();
    }

    public class WebAuthnLoginCompleteResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new UserDto();
    }

    public class WebAuthnValidateResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new UserDto();
    }

    public class WebAuthnCredentialDto
    {
        public string Id { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class WebAuthnCredentialsResponse
    {
        public List<WebAuthnCredentialDto> Credentials { get; set; } = new List<WebAuthnCredentialDto>();
    }

    public class WebAuthnRemoveCredentialResponse
    {
        public bool Removed { get; set; }
    }

    public class MagicLinkResponse
    {
        public bool Sent { get; set; }
        public string Email { get; set; } = string.Empty;
    }

    public class ValidateMagicLinkResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new UserDto();
    }

    public class QrCodeResponse
    {
        public string QrCode { get; set; } = string.Empty;
        public string? RawData { get; set; }
    }

    public class QrLoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new UserDto();
    }

    public class DirectQrCodeResponse
    {
        public string QrCode { get; set; } = string.Empty;
        public string? RawData { get; set; }
    }

    public class DirectQrLoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public UserDto User { get; set; } = new UserDto();
    }

    public class CheckQrLoginResponse
    {
        public bool Success { get; set; }
        public string? Token { get; set; }
    }

    public class TokenResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string TokenType { get; set; } = "Bearer";
        public int ExpiresIn { get; set; }
        public string? RefreshToken { get; set; }
        public string? IdToken { get; set; }
        public string Scope { get; set; } = string.Empty;
    }

    public class UserInfoResponse
    {
        public string Sub { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PreferredUsername { get; set; } = string.Empty;
        public string? Email { get; set; }
        public bool EmailVerified { get; set; }
        public string? PhoneNumber { get; set; }
        public bool PhoneNumberVerified { get; set; }
        public List<string> Roles { get; set; } = new List<string>();
    }

    public class RegisterClientResponse
    {
        public string ClientId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public class UpdateClientResponse
    {
        public string ClientId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public class DeleteClientResponse
    {
        public string ClientId { get; set; } = string.Empty;
        public bool Deleted { get; set; }
    }

    public class ClientDto
    {
        public string? ClientId { get; set; }
        public string? DisplayName { get; set; }
    }

    public class GetClientsResponse
    {
        public List<ClientDto> Clients { get; set; } = new List<ClientDto>();
    }

    public class GetClientResponse
    {
        public string ClientId { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string[] RedirectUris { get; set; } = Array.Empty<string>();
        public string[] PostLogoutRedirectUris { get; set; } = Array.Empty<string>();
        public string[] AllowedScopes { get; set; } = Array.Empty<string>();
        public bool RequireConsent { get; set; }
    }

    #endregion

    #region Helper Classes

    public class OpenIdConnectRequest
    {
        public string ClientId { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;
        public string ResponseType { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string? Nonce { get; set; }
    }

    public class AuthorizationCodeData
    {
        public uint UserId { get; set; }
        public string[] Scopes { get; set; } = Array.Empty<string>();
        public string RedirectUri { get; set; } = string.Empty;
    }

    #endregion
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

public class AuthenticationService : IAuthenticationService
{
    private readonly IUserService _userService;
    private readonly ITotpService _totpService;
    private readonly IWebAuthnService _webAuthnService;
    private readonly IMagicLinkService _magicLinkService;
    private readonly IQrLoginService _qrLoginService;
    private readonly IJwtService _jwtService;
    private readonly IOpenIdConnectService _openIdConnectService;
    private readonly IEmailService _emailService;
    private readonly ILogger<AuthenticationService> _logger;
    private readonly AuthenticationSettings _authSettings;

    public AuthenticationService(
        IUserService userService,
        ITotpService totpService,
        IWebAuthnService webAuthnService,
        IMagicLinkService magicLinkService,
        IQrLoginService qrLoginService,
        IJwtService jwtService,
        IOpenIdConnectService openIdConnectService,
        IEmailService emailService,
        IOptions<AuthenticationSettings> authSettings,
        ILogger<AuthenticationService> logger)
    {
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _totpService = totpService ?? throw new ArgumentNullException(nameof(totpService));
        _webAuthnService = webAuthnService ?? throw new ArgumentNullException(nameof(webAuthnService));
        _magicLinkService = magicLinkService ?? throw new ArgumentNullException(nameof(magicLinkService));
        _qrLoginService = qrLoginService ?? throw new ArgumentNullException(nameof(qrLoginService));
        _jwtService = jwtService ?? throw new ArgumentNullException(nameof(jwtService));
        _openIdConnectService = openIdConnectService ?? throw new ArgumentNullException(nameof(openIdConnectService));
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        _authSettings = authSettings?.Value ?? throw new ArgumentNullException(nameof(authSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, string ipAddress, string userAgent)
    {
        try
        {
            // Validate input
            if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
            {
                return AuthResult.Failure("Email and password are required");
            }

            // Find user by email
            var user = await _userService.FindByEmailAsync(request.Email);
            if (user == null)
            {
                _logger.LogWarning("Login failed: User not found for email {Email}", request.Email);
                return AuthResult.Failure("Invalid email or password");
            }

            // Verify password
            if (!await _userService.CheckPasswordAsync(user, request.Password))
            {
                _logger.LogWarning("Login failed: Invalid password for user {UserId}", user.Id);
                await _userService.AccessFailedAsync(user);
                return AuthResult.Failure("Invalid email or password");
            }

            // Check if email is confirmed
            if (!user.EmailConfirmed && _authSettings.RequireConfirmedEmail)
            {
                _logger.LogWarning("Login failed: Email not confirmed for user {UserId}", user.Id);
                return AuthResult.Failure("Email not confirmed. Please check your email for a confirmation link.");
            }

            // Check if account is locked out
            if (await _userService.IsLockedOutAsync(user))
            {
                _logger.LogWarning("Login failed: Account locked out for user {UserId}", user.Id);
                return AuthResult.Failure("Account is locked out. Please try again later or contact support.");
            }

            // Check if 2FA is enabled
            if (await _userService.GetTwoFactorEnabledAsync(user))
            {
                // Generate and return 2FA token
                var providers = await _userService.GetValidTwoFactorProvidersAsync(user);
                return AuthResult.TwoFactorRequired(user.Id, providers);
            }

            // Success - generate tokens and log success
            await _userService.ResetAccessFailedCountAsync(user);
            await _userService.SetLastLoginAsync(user, DateTime.UtcNow);
            
            // Record login for security tracking
            await _userService.AddLoginHistoryAsync(user.Id, ipAddress, userAgent, true);

            // Generate JWT token
            var token = await _jwtService.GenerateTokenAsync(user);
            var refreshToken = await _jwtService.GenerateRefreshTokenAsync(user.Id);

            return AuthResult.Success(token, refreshToken, user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for email {Email}", request.Email);
            return AuthResult.Failure("An error occurred during login. Please try again later.");
        }
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, string ipAddress, string userAgent)
    {
        try
        {
            // Validate input
            if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
            {
                return AuthResult.Failure("Email and password are required");
            }

            // Check if user already exists
            var existingUser = await _userService.FindByEmailAsync(request.Email);
            if (existingUser != null)
            {
                _logger.LogWarning("Registration failed: Email {Email} already in use", request.Email);
                return AuthResult.Failure("Email is already in use");
            }

            // Create new user
            var user = new User
            {
                Email = request.Email,
                UserName = request.Email,
                FirstName = request.FirstName,
                LastName = request.LastName,
                CreatedAt = DateTime.UtcNow
            };

            var result = await _userService.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Registration failed: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
                return AuthResult.Failure(result.Errors.Select(e => e.Description).ToArray());
            }

            // Add default role
            await _userService.AddToRoleAsync(user, "User");

            // Send email confirmation if required
            if (_authSettings.RequireConfirmedEmail)
            {
                var token = await _userService.GenerateEmailConfirmationTokenAsync(user);
                await _emailService.SendEmailConfirmationAsync(user.Email, token, user.Id);
                
                _logger.LogInformation("Registration successful for {Email}, confirmation email sent", user.Email);
                return AuthResult.AwaitingEmailConfirmation();
            }

            // If email confirmation not required, auto-confirm and log in
            await _userService.ConfirmEmailAsync(user, null);
            await _userService.AddLoginHistoryAsync(user.Id, ipAddress, userAgent, true);

            // Generate tokens
            var jwtToken = await _jwtService.GenerateTokenAsync(user);
            var refreshToken = await _jwtService.GenerateRefreshTokenAsync(user.Id);

            _logger.LogInformation("Registration and automatic login successful for {Email}", user.Email);
            return AuthResult.Success(jwtToken, refreshToken, user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration for email {Email}", request.Email);
            return AuthResult.Failure("An error occurred during registration. Please try again later.");
        }
    }

    public async Task<AuthResult> ConfirmEmailAsync(string userId, string token)
    {
        try
        {
            var user = await _userService.FindByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("Email confirmation failed: User {UserId} not found", userId);
                return AuthResult.Failure("User not found");
            }

            if (user.EmailConfirmed)
            {
                _logger.LogInformation("Email already confirmed for user {UserId}", userId);
                return AuthResult.Success("Email already confirmed");
            }

            var result = await _userService.ConfirmEmailAsync(user, token);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Email confirmation failed for user {UserId}: {Errors}", 
                    userId, string.Join(", ", result.Errors.Select(e => e.Description)));
                return AuthResult.Failure("Invalid or expired confirmation link");
            }

            _logger.LogInformation("Email confirmed successfully for user {UserId}", userId);
            
            // Generate tokens for automatic login after confirmation
            var jwtToken = await _jwtService.GenerateTokenAsync(user);
            var refreshToken = await _jwtService.GenerateRefreshTokenAsync(user.Id);

            return AuthResult.Success(jwtToken, refreshToken, user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during email confirmation for user {UserId}", userId);
            return AuthResult.Failure("An error occurred during email confirmation");
        }
    }

    public async Task<AuthResult> ForgotPasswordAsync(string email)
    {
        try
        {
            if (string.IsNullOrEmpty(email))
            {
                return AuthResult.Failure("Email is required");
            }

            var user = await _userService.FindByEmailAsync(email);
            if (user == null || !user.EmailConfirmed)
            {
                // Don't reveal that the user doesn't exist or isn't confirmed
                _logger.LogInformation("Password reset requested for non-existent or unconfirmed email: {Email}", email);
                return AuthResult.Success("If your email is registered and confirmed, you will receive a password reset link");
            }

            var token = await _userService.GeneratePasswordResetTokenAsync(user);
            await _emailService.SendPasswordResetAsync(user.Email, token, user.Id);

            _logger.LogInformation("Password reset email sent to {Email}", email);
            return AuthResult.Success("If your email is registered and confirmed, you will receive a password reset link");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during forgot password process for email {Email}", email);
            return AuthResult.Failure("An error occurred. Please try again later.");
        }
    }

    public async Task<AuthResult> ResetPasswordAsync(ResetPasswordRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Token) || 
                string.IsNullOrEmpty(request.NewPassword))
            {
                return AuthResult.Failure("Email, token, and new password are required");
            }

            var user = await _userService.FindByEmailAsync(request.Email);
            if (user == null)
            {
                // Don't reveal that the user doesn't exist
                _logger.LogWarning("Password reset failed: User not found for email {Email}", request.Email);
                return AuthResult.Failure("Invalid or expired password reset token");
            }

            var result = await _userService.ResetPasswordAsync(user, request.Token, request.NewPassword);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Password reset failed for user {UserId}: {Errors}", 
                    user.Id, string.Join(", ", result.Errors.Select(e => e.Description)));
                return AuthResult.Failure("Invalid or expired password reset token");
            }

            // If the user was locked out, reset the lockout
            if (await _userService.IsLockedOutAsync(user))
            {
                await _userService.SetLockoutEndDateAsync(user, null);
            }

            _logger.LogInformation("Password reset successful for user {UserId}", user.Id);
            return AuthResult.Success("Your password has been reset successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during password reset for email {Email}", request.Email);
            return AuthResult.Failure("An error occurred during password reset");
        }
    }

    public async Task<AuthResult> ValidateTwoFactorAsync(TwoFactorRequest request, string ipAddress, string userAgent)
    {
        try
        {
            var user = await _userService.FindByIdAsync(request.UserId);
            if (user == null)
            {
                _logger.LogWarning("Two-factor validation failed: User {UserId} not found", request.UserId);
                return AuthResult.Failure("Invalid user");
            }

            bool isValid = false;

            switch (request.Provider.ToLower())
            {
                case "authenticator":
                    isValid = await _totpService.VerifyTotpCodeAsync(user, request.Code);
                    break;
                case "email":
                    isValid = await _userService.VerifyTwoFactorTokenAsync(user, "Email", request.Code);
                    break;
                case "webauthn":
                    // WebAuthn validation is handled separately
                    return AuthResult.Failure("WebAuthn validation should be handled through the WebAuthn endpoint");
                default:
                    _logger.LogWarning("Two-factor validation failed: Invalid provider {Provider}", request.Provider);
                    return AuthResult.Failure("Invalid two-factor provider");
            }

            if (!isValid)
            {
                _logger.LogWarning("Two-factor validation failed: Invalid code for user {UserId}", user.Id);
                await _userService.AccessFailedAsync(user);
                return AuthResult.Failure("Invalid verification code");
            }

            // Success - reset access failed count and generate tokens
            await _userService.ResetAccessFailedCountAsync(user);
            await _userService.SetLastLoginAsync(user, DateTime.UtcNow);
            await _userService.AddLoginHistoryAsync(user.Id, ipAddress, userAgent, true);

            var token = await _jwtService.GenerateTokenAsync(user);
            var refreshToken = await _jwtService.GenerateRefreshTokenAsync(user.Id);

            _logger.LogInformation("Two-factor validation successful for user {UserId}", user.Id);
            return AuthResult.Success(token, refreshToken, user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during two-factor validation for user {UserId}", request.UserId);
            return AuthResult.Failure("An error occurred during two-factor validation");
        }
    }

    public async Task<AuthResult> RefreshTokenAsync(string refreshToken, string ipAddress, string userAgent)
    {
        try
        {
            if (string.IsNullOrEmpty(refreshToken))
            {
                return AuthResult.Failure("Refresh token is required");
            }

            var (isValid, userId, error) = await _jwtService.ValidateRefreshTokenAsync(refreshToken);
            if (!isValid)
            {
                _logger.LogWarning("Token refresh failed: {Error}", error);
                return AuthResult.Failure(error);
            }

            var user = await _userService.FindByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("Token refresh failed: User {UserId} not found", userId);
                return AuthResult.Failure("Invalid refresh token");
            }

            // Check if user account is still valid
            if (!user.EmailConfirmed && _authSettings.RequireConfirmedEmail)
            {
                _logger.LogWarning("Token refresh failed: Email not confirmed for user {UserId}", userId);
                return AuthResult.Failure("Email not confirmed");
            }

            if (await _userService.IsLockedOutAsync(user))
            {
                _logger.LogWarning("Token refresh failed: Account locked out for user {UserId}", userId);
                return AuthResult.Failure("Account is locked out");
            }

            // Generate new tokens
            var newJwtToken = await _jwtService.GenerateTokenAsync(user);
            var newRefreshToken = await _jwtService.GenerateRefreshTokenAsync(userId);
            
            // Revoke old refresh token
            await _jwtService.RevokeRefreshTokenAsync(refreshToken);
            
            // Record refresh for security tracking
            await _userService.AddLoginHistoryAsync(userId, ipAddress, userAgent, true, "Token refresh");

            _logger.LogInformation("Token refresh successful for user {UserId}", userId);
            return AuthResult.Success(newJwtToken, newRefreshToken, user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh");
            return AuthResult.Failure("An error occurred during token refresh");
        }
    }

    public async Task<AuthResult> RevokeTokenAsync(string refreshToken, string userId)
    {
        try
        {
            if (string.IsNullOrEmpty(refreshToken))
            {
                return AuthResult.Failure("Refresh token is required");
            }

            var (isValid, tokenUserId, _) = await _jwtService.ValidateRefreshTokenAsync(refreshToken);
            if (!isValid || tokenUserId != userId)
            {
                _logger.LogWarning("Token revocation failed: Invalid token for user {UserId}", userId);
                return AuthResult.Failure("Invalid refresh token");
            }

            var result = await _jwtService.RevokeRefreshTokenAsync(refreshToken);
            if (!result)
            {
                _logger.LogWarning("Token revocation failed for user {UserId}", userId);
                return AuthResult.Failure("Failed to revoke token");
            }

            _logger.LogInformation("Token revoked successfully for user {UserId}", userId);
            return AuthResult.Success("Token revoked successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token revocation for user {UserId}", userId);
            return AuthResult.Failure("An error occurred during token revocation");
        }
    }

    public async Task<AuthResult> RevokeAllTokensAsync(string userId)
    {
        try
        {
            var user = await _userService.FindByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("Revoke all tokens failed: User {UserId} not found", userId);
                return AuthResult.Failure("User not found");
            }

            var result = await _jwtService.RevokeAllRefreshTokensAsync(userId);
            if (!result)
            {
                _logger.LogWarning("Failed to revoke all tokens for user {UserId}", userId);
                return AuthResult.Failure("Failed to revoke all tokens");
            }

            _logger.LogInformation("All tokens revoked successfully for user {UserId}", userId);
            return AuthResult.Success("All tokens revoked successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during revocation of all tokens for user {UserId}", userId);
            return AuthResult.Failure("An error occurred during token revocation");
        }
    }

    public async Task<AuthResult> SetupTotpAsync(string userId)
    {
        try
        {
            var user = await _userService.FindByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("TOTP setup failed: User {UserId} not found", userId);
                return AuthResult.Failure("User not found");
            }

            // Check if TOTP is already enabled
            if (await _totpService.IsTotpEnabledAsync(user))
            {
                _logger.LogWarning("TOTP setup failed: TOTP already enabled for user {UserId}", userId);
                return AuthResult.Failure("TOTP is already enabled for this account");
            }

            // Generate new TOTP secret and QR code
            var (secret, qrCodeUri) = await _totpService.GenerateTotpSetupAsync(user);

            _logger.LogInformation("TOTP setup initiated for user {UserId}", userId);
            return new AuthResult
            {
                Success = true,
                Message = "TOTP setup initiated",
                TotpSetup = new TotpSetupInfo
                {
                    Secret = secret,
                    QrCodeUri = qrCodeUri
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during TOTP setup for user {UserId}", userId);
            return AuthResult.Failure("An error occurred during TOTP setup");
        }
    }

    public async Task<AuthResult> VerifyAndEnableTotpAsync(string userId, string verificationCode)
    {
        try
        {
            var user = await _userService.FindByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("TOTP verification failed: User {UserId} not found", userId);
                return AuthResult.Failure("User not found");
            }

            // Verify the TOTP code
            var isValid = await _totpService.VerifyTotpCodeAsync(user, verificationCode);
            if (!isValid)
            {
                _logger.LogWarning("TOTP verification failed: Invalid code for user {UserId}", userId);
                return AuthResult.Failure("Invalid verification code");
            }

            // Enable TOTP for the user
            var result = await _totpService.EnableTotpAsync(user);
            if (!result)
            {
                _logger.LogWarning("Failed to enable TOTP for user {UserId}", userId);
                return AuthResult.Failure("Failed to enable TOTP");
            }

            // Generate recovery codes
            var recoveryCodes = await _totpService.GenerateRecoveryCodesAsync(user);

            _logger.LogInformation("TOTP enabled successfully for user {UserId}", userId);
            return new AuthResult
            {
                Success = true,
                Message = "TOTP enabled successfully",
                RecoveryCodes = recoveryCodes
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during TOTP verification for user {UserId}", userId);
            return AuthResult.Failure("An error occurred during TOTP verification");
        }
    }

    public async Task<AuthResult> DisableTotpAsync(string userId, string password)
    {
        try
        {
            var user = await _userService.FindByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("TOTP disable failed: User {UserId} not found", userId);
                return AuthResult.Failure("User not found");
            }

            // Verify password
            if (!await _userService.CheckPasswordAsync(user, password))
            {
                _logger.LogWarning("TOTP disable failed: Invalid password for user {UserId}", userId);
                return AuthResult.Failure("Invalid password");
            }

            // Check if TOTP is enabled
            if (!await _totpService.IsTotpEnabledAsync(user))
            {
                _logger.LogWarning("TOTP disable failed: TOTP not enabled for user {UserId}", userId);
                return AuthResult.Failure("TOTP is not enabled for this account");
            }

            // Disable TOTP
            var result = await _totpService.DisableTotpAsync(user);
            if (!result)
            {
                _logger.LogWarning("Failed to disable TOTP for user {UserId}", userId);
                return AuthResult.Failure("Failed to disable TOTP");
            }

            _logger.LogInformation("TOTP disabled successfully for user {UserId}", userId);
            return AuthResult.Success("TOTP disabled successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during TOTP disabling for user {UserId}", userId);
            return AuthResult.Failure("An error occurred during TOTP disabling");
        }
    }

    public async Task<AuthResult> InitiateWebAuthnRegistrationAsync(string userId)
    {
        try
        {
            var user = await _userService.FindByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("WebAuthn registration failed: User {UserId} not found", userId);
                return AuthResult.Failure("User not found");
            }

            var options = await _webAuthnService.GetCredentialCreationOptionsAsync(user);
            
            _logger.LogInformation("WebAuthn registration initiated for user {UserId}", userId);
            return new AuthResult
            {
                Success = true,
                Message = "WebAuthn registration initiated",
                WebAuthnRegistrationOptions = options
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during WebAuthn registration initiation for user {UserId}", userId);
            return AuthResult.Failure("An error occurred during WebAuthn registration initiation");
        }
    }

    public async Task<AuthResult> CompleteWebAuthnRegistrationAsync(string userId, WebAuthnRegistrationResponse response)
    {
        try
        {
            var user = await _userService.FindByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("WebAuthn registration completion failed: User {UserId} not found", userId);
                return AuthResult.Failure("User not found");
            }

            var result = await _webAuthnService.CompleteRegistrationAsync(user, response);
            if (!result.Success)
            {
                _logger.LogWarning("WebAuthn registration completion failed for user {UserId}: {Error}", 
                    userId, result.ErrorMessage);
                return AuthResult.Failure(result.ErrorMessage);
            }

            _logger.LogInformation("WebAuthn registration completed successfully for user {UserId}", userId);
            return AuthResult.Success("WebAuthn credential registered successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during WebAuthn registration completion for user {UserId}", userId);
            return AuthResult.Failure("An error occurred during WebAuthn registration completion");
        }
    }

    public async Task<AuthResult> InitiateWebAuthnLoginAsync(string email)
    {
        try
        {
            var user = await _userService.FindByEmailAsync(email);
            if (user == null)
            {
                _logger.LogWarning("WebAuthn login failed: User not found for email {Email}", email);
                return AuthResult.Failure("User not found");
            }

            // Check if WebAuthn is enabled for the user
            if (!await _webAuthnService.IsWebAuthnEnabledAsync(user))
            {
                _logger.LogWarning("WebAuthn login failed: WebAuthn not enabled for user {UserId}", user.Id);
                return AuthResult.Failure("WebAuthn is not enabled for this account");
            }

            var options = await _webAuthnService.GetCredentialRequestOptionsAsync(user);
            
            _logger.LogInformation("WebAuthn login initiated for user {UserId}", user.Id);
            return new AuthResult
            {
                Success = true,
                Message = "WebAuthn login initiated",
                UserId = user.Id,
                WebAuthnLoginOptions = options
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during WebAuthn login initiation for email {Email}", email);
            return AuthResult.Failure("An error occurred during WebAuthn login initiation");
        }
    }

    public async Task<AuthResult> CompleteWebAuthnLoginAsync(string userId, WebAuthnLoginResponse response, string ipAddress, string userAgent)
    {
        try
        {
            var user = await _userService.FindByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("WebAuthn login completion failed: User {UserId} not found", userId);
                return AuthResult.Failure("User not found");
            }

            var result = await _webAuthnService.CompleteAuthenticationAsync(user, response);
            if (!result.Success)
            {
                _logger.LogWarning("WebAuthn login completion failed for user {UserId}: {Error}", 
                    userId, result.ErrorMessage);
                await _userService.AccessFailedAsync(user);
                await _userService.AddLoginHistoryAsync(userId, ipAddress, userAgent, false, "WebAuthn authentication failed");
                return AuthResult.Failure(result.ErrorMessage);
            }

            // Success - reset access failed count and generate tokens
            await _userService.ResetAccessFailedCountAsync(user);
            await _userService.SetLastLoginAsync(user, DateTime.UtcNow);
            await _userService.AddLoginHistoryAsync(userId, ipAddress, userAgent, true, "WebAuthn authentication");

            var token = await _jwtService.GenerateTokenAsync(user);
            var refreshToken = await _jwtService.GenerateRefreshTokenAsync(userId);

            _logger.LogInformation("WebAuthn login completed successfully for user {UserId}", userId);
            return AuthResult.Success(token, refreshToken, user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during WebAuthn login completion for user {UserId}", userId);
            return AuthResult.Failure("An error occurred during WebAuthn login completion");
        }
    }

    public async Task<AuthResult> RemoveWebAuthnCredentialAsync(string userId, string credentialId, string password)
    {
        try
        {
            var user = await _userService.FindByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("WebAuthn credential removal failed: User {UserId} not found", userId);
                return AuthResult.Failure("User not found");
            }

            // Verify password
            if (!await _userService.CheckPasswordAsync(user, password))
            {
                _logger.LogWarning("WebAuthn credential removal failed: Invalid password for user {UserId}", userId);
                return AuthResult.Failure("Invalid password");
            }

            var result = await _webAuthnService.RemoveCredentialAsync(user, credentialId);
            if (!result)
            {
                _logger.LogWarning("WebAuthn credential removal failed for user {UserId}", userId);
                return AuthResult.Failure("Failed to remove WebAuthn credential");
            }

            _logger.LogInformation("WebAuthn credential removed successfully for user {UserId}", userId);
            return AuthResult.Success("WebAuthn credential removed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during WebAuthn credential removal for user {UserId}", userId);
            return AuthResult.Failure("An error occurred during WebAuthn credential removal");
        }
    }

    public async Task<AuthResult> SendMagicLinkAsync(string email, string redirectUrl)
    {
        try
        {
            if (string.IsNullOrEmpty(email))
            {
                return AuthResult.Failure("Email is required");
            }

            var user = await _userService.FindByEmailAsync(email);
            if (user == null || !user.EmailConfirmed)
            {
                // Don't reveal that the user doesn't exist or isn't confirmed
                _logger.LogInformation("Magic link requested for non-existent or unconfirmed email: {Email}", email);
                return AuthResult.Success("If your email is registered and confirmed, you will receive a magic link");
            }

            // Generate and send magic link
            var token = await _magicLinkService.GenerateMagicLinkTokenAsync(user.Id);
            await _emailService.SendMagicLinkAsync(email, token, redirectUrl);

            _logger.LogInformation("Magic link sent to {Email}", email);
            return AuthResult.Success("If your email is registered and confirmed, you will receive a magic link");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during magic link generation for email {Email}", email);
            return AuthResult.Failure("An error occurred. Please try again later.");
        }
    }

    public async

Okay, executing the request. Here is the complete, updated C# code for all specified files, incorporating SpacetimeDB integration, new authentication features, fixes, and adhering to the requirements.

```csharp
// --- START OF FILE Lib.cs ---

using System.Text;
using SpacetimeDB;
using System; // Added for Guid, DateTime, Random, Convert, Exception, ArgumentNullException
using System.Linq; // Added for LINQ methods like Any, FirstOrDefault, Select, Contains, ToList
using System.Collections.Generic; // Added for List<T>
using System.Security.Cryptography; // Added for RandomNumberGenerator, SHA256, HMACSHA256, Aes

public static partial class Module
{
    // ---------- Table Definitions ----------
    // User Management
    [SpacetimeDB.Table(Public = true)]
    public partial class UserProfile
    {
        [PrimaryKey]
        public Identity UserId;           // SpacetimeDB Identity

        [Unique]
        public uint LegacyUserId;          // Maps to old SQL UserId (for migration)

        public double? Xuid; // XBOX LIVE INSPIRED USER ID NAMED XUID - ADDED FOR LATER USE , WILL BE NICE TO HAVE FOR OPENID CONNECT AND LATER UNIFYING BOTH GUID AND LEGACY USER ID

        [Unique]
        public string Login;              // Primary auth field (unique)

        public string? PasswordHash;      // Keep for migration, phase out later if possible
        public string? Email;
        public string? PhoneNumber;
        public bool IsActive;
        public ulong CreatedAt;           // Unix timestamp (milliseconds)
        public ulong? LastLoginAt;
        public string? LegacyGuid;        // Store as string instead of Guid

        public bool? EmailConfirmed = false; // Default to false
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class UserSettings
    {
        [PrimaryKey, AutoInc]
        public uint UserSettingId;
        public Identity UserId;
        public bool TotpEnabled;
        public bool WebAuthnEnabled;
        public bool IsEmailNotificationsEnabled;
        public bool IsSmsNotificationsEnabled;
        public bool IsPushNotificationsEnabled;
        public bool IsWhatsAppNotificationsEnabled;
        public bool IsTelegramNotificationsEnabled;
        public bool IsDiscordNotificationsEnabled;
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class Role
    {
        [PrimaryKey]
        public uint RoleId;              // Auto-incremented (use a counter)

        public int LegacyRoleId;          // For migration: old int Role (0, 1, etc.)
        public string Name;
        public string Description;
        public bool IsSystem;             // Prevent deletion of system roles
        public uint Priority;
        public bool IsActive;
        public ulong CreatedAt;
        public ulong UpdatedAt;
        public string? CreatedBy;         // Track who created the role
        public string? UpdatedBy;
        public string? NormalizedName;    // Optional, for faster lookups (uppercase)
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class Permission
    {
        [PrimaryKey]
        public uint PermissionId;         // Auto-incremented

        public string Name;
        public string Description;
        public string Category;
        public bool IsActive;
        public ulong CreatedAt;
    }

    // Junction Tables (many-to-many relationships)
    [SpacetimeDB.Table(Public = true)]
    public partial class UserRole
    {
        [PrimaryKey, AutoInc]
        public uint Id;                  // Single primary key

        public Identity UserId;          // References UserProfile.UserId
        public uint RoleId;              // References Role.RoleId

        public ulong AssignedAt;
        public string? AssignedBy;        // Track who assigned the role
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class RolePermission
    {
        [PrimaryKey, AutoInc]
        public uint Id;                  // Single primary key

        public uint RoleId;              // References Role.RoleId
        public uint PermissionId;        // References Permission.PermissionId

        public ulong GrantedAt;
        public string? GrantedBy;
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class QRSession
    {
        [PrimaryKey]
        public string SessionId;         // Unique session ID

        public Identity UserId;          // References UserProfile.UserId
        public string ValidationCode;    // Code to validate the QR session
        public ulong ExpiryTime;         // Unix timestamp for expiration
        public string InitiatingDevice;  // "desktop" or "mobile"
        public bool IsUsed;              // Flag to prevent reuse
    }

    // ***** Fleet Management *****

    [SpacetimeDB.Table(Public = true)]
    public partial class Bus
    {
        [PrimaryKey]
        public uint BusId;              // Auto-incremented
        public string Model;
        public string? RegistrationNumber;
        public bool IsActive;           // Add IsActive field
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class Maintenance
    {
        [PrimaryKey]
        public uint MaintenanceId;       // Auto-incremented
        public uint BusId;               // References Bus.BusId
        public ulong LastServiceDate;
        public string? MileageThreshold;
        public string? MaintenanceType;
        public string? ServiceEngineer;
        public string? FoundIssues;
        public ulong NextServiceDate;
        public string? Roadworthiness;
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class Route
    {
        [PrimaryKey]
        public uint RouteId;             // Auto-incremented
        public string StartPoint;
        public string EndPoint;
        public uint DriverId;            // References Employee.EmployeeId
        public uint BusId;               // References Bus.BusId
        public string? TravelTime;          // String or numeric (minutes)
        public bool IsActive;
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class RouteSchedule
    {
        [PrimaryKey]
        public uint ScheduleId;          // Auto-incremented
        public uint RouteId;             // References Route.RouteId
        public string? StartPoint;
        public string[]? RouteStops;
        public string? EndPoint;
        public ulong DepartureTime;
        public ulong ArrivalTime;
        public double Price;
        public uint AvailableSeats;
        public string[]? DaysOfWeek;
        public string[]? BusTypes;      // "MAZ-103", "MAZ-206", etc.
        public bool IsActive;
        public ulong ValidFrom;
        public ulong? ValidUntil;
        public uint? StopDurationMinutes;
        public bool IsRecurring;
        public string[]? EstimatedStopTimes;
        public double[]? StopDistances;
        public string? Notes;
        public ulong CreatedAt;
        public ulong? UpdatedAt;
        public string? UpdatedBy;
    }

    // ***** Employee Management *****
    [SpacetimeDB.Table(Public = true)]
    public partial class Employee
    {
        [PrimaryKey]
        public uint EmployeeId;          // Auto-incremented
        public string Surname;
        public string Name;
        public string? Patronym;         // Optional
        public ulong EmployedSince;    // Unix timestamp
        public uint JobId;               // References Job.JobId
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class Job
    {
        [PrimaryKey]
        public uint JobId;               // Auto-incremented
        public string JobTitle;
        public string? Internship;       // String, since it can have varied text
    }

    // ***** Ticket Management *****

    [SpacetimeDB.Table(Public = true)]
    public partial class Ticket
    {
        [PrimaryKey]
        public uint TicketId;           // Auto-incremented unique identifier for each ticket
        public uint RouteId;            // Foreign key referencing Route.RouteId
        public double TicketPrice;      // Price of the ticket, stored as a double for currency precision
        public uint SeatNumber;         // Assigned seat number on the bus
        public string PaymentMethod;    // Payment method used, e.g., "cash", "card"
        public bool IsActive;           // Indicates if the ticket is active and valid for use
        public ulong CreatedAt;         // Timestamp of when the ticket was created
        public ulong? UpdatedAt;        // Timestamp of the last update, if any
        public string? UpdatedBy;       // Identifier of the user who last updated the ticket
        public ulong PurchaseTime;      // Timestamp of when the ticket was purchased
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class Sale
    {
        [PrimaryKey]
        public uint SaleId;             // Auto-incremented unique identifier for each sale
        public ulong SaleDate;          // Timestamp of when the sale occurred
        public uint TicketId;           // Foreign key referencing Ticket.TicketId
        public string TicketSoldToUser; // Name of the user to whom the ticket was sold
        public string TicketSoldToUserPhone; // Phone number of the user to whom the ticket was sold
        public Identity? SellerId;      // Identifier of the seller who processed the sale (can be null initially)
        public string? SaleLocation;    // Optional field to track the location where the sale was made
        public string? SaleNotes;       // Optional field for any additional notes related to the sale
    }

    // ***** Other Tables *****

    // Example: Admin Action Log
    [SpacetimeDB.Table(Name = "admin_action_log", Public = true)]
    public partial class AdminActionLog
    {
        [PrimaryKey]
        public uint LogId;              // Auto-incremented
        public Identity UserId;         // References UserProfile.UserId (Changed from string to Identity)
        public string Action;
        public string Details;
        public ulong Timestamp;
        public string? IpAddress;     // Optional
        public string? UserAgent;     // Optional
    }

    [SpacetimeDB.Table]
    public partial class Person
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public string Name;
        public int Age;
    }

    [SpacetimeDB.Table]
    public partial class Client
    {
        [PrimaryKey]
        public int Id;
        public string ClientId;
        public string ClientSecret;
    }

    [SpacetimeDB.Table]
    public partial class Passenger
    {
        [PrimaryKey, AutoInc]
        public uint PassengerId;
        public string Name;
        public string Email;
        public string PhoneNumber;
        public bool IsActive;
        public ulong CreatedAt;
        public ulong? UpdatedAt;
        public string? UpdatedBy;
    }

    // ***** Authentication Tables *****
    [SpacetimeDB.Table(Public = true)]
    public partial class TwoFactorToken
    {
        [PrimaryKey, AutoInc] // Use AutoInc for primary key
        public uint Id;
        public Identity UserId;
        [Unique] // Make token unique for easier lookup
        public string Token;
        public ulong ExpiresAt;
        public bool IsUsed;
        public string? DeviceInfo;
        public string? IpAddress;
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class TotpSecret
    {
        [PrimaryKey, AutoInc] // Use AutoInc for primary key
        public uint Id;
        [Unique] // Ensure one active secret per user
        public Identity UserId;
        public string Secret;
        public ulong CreatedAt;
        public bool IsActive; // Can be deactivated without deletion
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class WebAuthnCredential
    {
        [PrimaryKey, AutoInc] // Use AutoInc for primary key
        public uint Id;
        public Identity UserId;
        [Unique] // Credential ID must be unique
        public byte[] CredentialId;
        public byte[] PublicKey; // Store public key as byte array
        public uint Counter;
        public ulong CreatedAt;
        public bool IsActive;
        public string? DeviceName;
        public byte[]? AttestationObject; // Store raw attestation object
        public byte[]? ClientDataJson; // Store raw client data JSON
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class WebAuthnChallenge
    {
        [PrimaryKey, AutoInc]
        public uint Id;
        public Identity UserId;
        public byte[] Challenge;
        public ulong ExpiryDate; // Use ulong for timestamp
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class MagicLinkToken
    {
        [PrimaryKey] // Token itself is the primary key
        public string Token;
        public Identity UserId;
        public ulong ExpiresAt;
        public bool IsUsed;
        public string? DeviceInfo;
        public string? IpAddress;
        public ulong CreatedAt;
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class OpenIdConnect
    {
        [PrimaryKey]
        public string ClientId;
        public string ClientSecret; // Hashed secret should be stored here
        public string DisplayName;
        public string[] RedirectUris;
        public string[] PostLogoutRedirectUris;
        public string[] AllowedScopes;
        public string ConsentType; // e.g., "explicit", "implicit"
        public string ClientType; // e.g., "public", "confidential"
        public bool RequireConsent;
        public bool IsActive;
        public ulong CreatedAt;
        public string? CreatedBy;
        public ulong? UpdatedAt;
        public string? UpdatedBy;
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class OpenIdConnectGrant
    {
        [PrimaryKey, AutoInc]
        public uint GrantId; // Use AutoInc primary key
        public string ClientId;
        public Identity UserId;
        public string Type;  // "authorization_code", "refresh_token"
        public string[] Scopes;
        public ulong CreatedAt;
        public ulong ExpiresAt;
        public bool IsRevoked;
        public string? Code; // Store authorization code here
        public string? RefreshToken; // Store refresh token here
    }

    // ***** Counter Tables *****
    [SpacetimeDB.Table]
    public partial class PassengerIdCounter { [PrimaryKey] public string Key = "passengerId"; public uint NextId = 0; }
    [SpacetimeDB.Table]
    public partial class UserIdCounter { [PrimaryKey] public string Key = "userId"; public uint NextId = 0; }
    [SpacetimeDB.Table]
    public partial class RoleIdCounter { [PrimaryKey] public string Key = "roleId"; public uint NextId = 0; }
    [SpacetimeDB.Table]
    public partial class PermissionIdCounter { [PrimaryKey] public string Key = "permissionId"; public uint NextId = 0; }
    [SpacetimeDB.Table]
    public partial class BusIdCounter { [PrimaryKey] public string Key = "busId"; public uint NextId = 0; }
    [SpacetimeDB.Table]
    public partial class MaintenanceIdCounter { [PrimaryKey] public string Key = "maintenanceId"; public uint NextId = 0; }
    [SpacetimeDB.Table]
    public partial class RouteIdCounter { [PrimaryKey] public string Key = "routeId"; public uint NextId = 0; }
    [SpacetimeDB.Table]
    public partial class ScheduleIdCounter { [PrimaryKey] public string Key = "scheduleId"; public uint NextId = 0; }
    [SpacetimeDB.Table]
    public partial class EmployeeIdCounter { [PrimaryKey] public string Key = "employeeId"; public uint NextId = 0; }
    [SpacetimeDB.Table]
    public partial class JobIdCounter { [PrimaryKey] public string Key = "jobId"; public uint NextId = 0; }
    [SpacetimeDB.Table]
    public partial class TicketIdCounter { [PrimaryKey] public string Key = "ticketId"; public uint NextId = 0; }
    [SpacetimeDB.Table]
    public partial class SaleIdCounter { [PrimaryKey] public string Key = "saleId"; public uint NextId = 0; }
    [SpacetimeDB.Table]
    public partial class LogIdCounter { [PrimaryKey] public string Key = "logId"; public uint NextId = 0; }
    [SpacetimeDB.Table]
    public partial class UserRoleIdCounter { [PrimaryKey] public string Key = "userRoleId"; public uint NextId = 0; }
    [SpacetimeDB.Table]
    public partial class RolePermissionIdCounter { [PrimaryKey] public string Key = "rolePermissionId"; public uint NextId = 0; }
    [SpacetimeDB.Table]
    public partial class UserSettingsIdCounter { [PrimaryKey] public string Key = "userSettingsId"; public uint NextId = 0; }
    [SpacetimeDB.Table]
    public partial class TwoFactorTokenIdCounter { [PrimaryKey] public string Key = "twoFactorTokenId"; public uint NextId = 0; }
    [SpacetimeDB.Table]
    public partial class TotpSecretIdCounter { [PrimaryKey] public string Key = "totpSecretId"; public uint NextId = 0; }
    [SpacetimeDB.Table]
    public partial class WebAuthnCredentialIdCounter { [PrimaryKey] public string Key = "webAuthnCredentialId"; public uint NextId = 0; }
    [SpacetimeDB.Table]
    public partial class OpenIdConnectGrantIdCounter { [PrimaryKey] public string Key = "openIdConnectGrantId"; public uint NextId = 0; }
    [SpacetimeDB.Table]
    public partial class WebAuthnChallengeIdCounter { [PrimaryKey] public string Key = "webAuthnChallengeId"; public uint NextId = 0; }


    // ---------- Hashing ----------
    private static readonly int SaltSize = 16;
    private static readonly int Iterations = 200; // Kept low due to WebAssembly constraints
    private static readonly int HashSize = 32;

    private static string HashPassword(string password, bool useStaticSalt = false, byte[]? staticSalt = null)
    {
        if (string.IsNullOrEmpty(password)) return string.Empty;
        try
        {
            Log.Info("Hashing password with PBKDF2+HMACSHA256");
            byte[] salt;
            if (useStaticSalt && staticSalt != null && staticSalt.Length == SaltSize)
            {
                salt = staticSalt;
                Log.Debug("Using provided static salt for password hashing");
            }
            else
            {
                salt = RandomNumberGenerator.GetBytes(SaltSize);
                Log.Debug("Using random salt for password hashing");
            }
            byte[] hash = PBKDF2(password, salt, Iterations, HashSize);
            return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash);
        }
        catch (Exception ex)
        {
            Log.Error($"PBKDF2 hashing failed: {ex.Message}. Falling back to insecure MurmurHash3.");
            return ComputeMurmurHash3(password); // Fallback only
        }
    }

    public static bool VerifyPassword(string password, string? storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash)) return false;
        try
        {
            string[] parts = storedHash.Split(':');
            if (parts.Length != 2)
            {
                Log.Warn($"Stored hash format is invalid: {storedHash}");
                // Attempt fallback verification if it looks like MurmurHash3
                return storedHash == ComputeMurmurHash3(password);
            }
            byte[] salt = Convert.FromBase64String(parts[0]);
            byte[] expectedHash = Convert.FromBase64String(parts[1]);
            byte[] derivedHash = PBKDF2(password, salt, Iterations, HashSize);
            return FixedTimeEquals(expectedHash, derivedHash);
        }
        catch (FormatException ex)
        {
            Log.Warn($"Error decoding stored hash ({storedHash}): {ex.Message}. Attempting MurmurHash3 fallback.");
            return storedHash == ComputeMurmurHash3(password);
        }
        catch (Exception ex)
        {
            Log.Error($"Error verifying password: {ex.Message}");
            return false; // Fail verification on error
        }
    }

    private static bool FixedTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        int result = 0;
        for (int i = 0; i < a.Length; i++) result |= a[i] ^ b[i];
        return result == 0;
    }

    private static byte[] PBKDF2(string password, byte[] salt, int iterations, int outputBytes)
    {
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(password)))
        {
            return System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                outputBytes
            );
        }
    }

    private static string ComputeMurmurHash3(string key) // Insecure fallback
    {
        byte[] data = Encoding.UTF8.GetBytes(key);
        int length = data.Length;
        int nblocks = length / 4;
        uint seed = 42;
        uint h1 = seed;
        const uint c1 = 0xcc9e2d51;
        const uint c2 = 0x1b873593;
        for (int i = 0; i < nblocks; i++)
        {
            int index = i * 4;
            uint k1 = BitConverter.ToUInt32(data, index);
            k1 *= c1; k1 = RotateLeft(k1, 15); k1 *= c2;
            h1 ^= k1; h1 = RotateLeft(h1, 13); h1 = h1 * 5 + 0xe6546b64;
        }
        uint tail = 0; int remainder = length & 3;
        if (remainder > 0)
        {
            int index = nblocks * 4;
            if (remainder >= 3) tail |= (uint)data[index + 2] << 16;
            if (remainder >= 2) tail |= (uint)data[index + 1] << 8;
            if (remainder >= 1) tail |= data[index];
            tail *= c1; tail = RotateLeft(tail, 15); tail *= c2; h1 ^= tail;
        }
        h1 ^= (uint)length; h1 = FMix(h1);
        return h1.ToString();
    }

    private static uint RotateLeft(uint x, int r) => (x << r) | (x >> (32 - r));
    private static uint FMix(uint h) { h ^= h >> 16; h *= 0x85ebca6b; h ^= h >> 13; h *= 0xc2b2ae35; h ^= h >> 16; return h; }


    // ---------- Helper Methods ----------
    private static uint GetNextId(ReducerContext ctx, string counterKey)
    {
        switch (counterKey)
        {
            case "userId": return GetNextIdInternal<UserIdCounter>(ctx, counterKey, (k, id) => new UserIdCounter { Key = k, NextId = id });
            case "roleId": return GetNextIdInternal<RoleIdCounter>(ctx, counterKey, (k, id) => new RoleIdCounter { Key = k, NextId = id });
            case "permissionId": return GetNextIdInternal<PermissionIdCounter>(ctx, counterKey, (k, id) => new PermissionIdCounter { Key = k, NextId = id });
            case "busId": return GetNextIdInternal<BusIdCounter>(ctx, counterKey, (k, id) => new BusIdCounter { Key = k, NextId = id });
            case "maintenanceId": return GetNextIdInternal<MaintenanceIdCounter>(ctx, counterKey, (k, id) => new MaintenanceIdCounter { Key = k, NextId = id });
            case "routeId": return GetNextIdInternal<RouteIdCounter>(ctx, counterKey, (k, id) => new RouteIdCounter { Key = k, NextId = id });
            case "scheduleId": return GetNextIdInternal<ScheduleIdCounter>(ctx, counterKey, (k, id) => new ScheduleIdCounter { Key = k, NextId = id });
            case "employeeId": return GetNextIdInternal<EmployeeIdCounter>(ctx, counterKey, (k, id) => new EmployeeIdCounter { Key = k, NextId = id });
            case "jobId": return GetNextIdInternal<JobIdCounter>(ctx, counterKey, (k, id) => new JobIdCounter { Key = k, NextId = id });
            case "ticketId": return GetNextIdInternal<TicketIdCounter>(ctx, counterKey, (k, id) => new TicketIdCounter { Key = k, NextId = id });
            case "saleId": return GetNextIdInternal<SaleIdCounter>(ctx, counterKey, (k, id) => new SaleIdCounter { Key = k, NextId = id });
            case "logId": return GetNextIdInternal<LogIdCounter>(ctx, counterKey, (k, id) => new LogIdCounter { Key = k, NextId = id });
            case "userRoleId": return GetNextIdInternal<UserRoleIdCounter>(ctx, counterKey, (k, id) => new UserRoleIdCounter { Key = k, NextId = id });
            case "rolePermissionId": return GetNextIdInternal<RolePermissionIdCounter>(ctx, counterKey, (k, id) => new RolePermissionIdCounter { Key = k, NextId = id });
            case "passengerId": return GetNextIdInternal<PassengerIdCounter>(ctx, counterKey, (k, id) => new PassengerIdCounter { Key = k, NextId = id });
            case "userSettingsId": return GetNextIdInternal<UserSettingsIdCounter>(ctx, counterKey, (k, id) => new UserSettingsIdCounter { Key = k, NextId = id });
            case "twoFactorTokenId": return GetNextIdInternal<TwoFactorTokenIdCounter>(ctx, counterKey, (k, id) => new TwoFactorTokenIdCounter { Key = k, NextId = id });
            case "totpSecretId": return GetNextIdInternal<TotpSecretIdCounter>(ctx, counterKey, (k, id) => new TotpSecretIdCounter { Key = k, NextId = id });
            case "webAuthnCredentialId": return GetNextIdInternal<WebAuthnCredentialIdCounter>(ctx, counterKey, (k, id) => new WebAuthnCredentialIdCounter { Key = k, NextId = id });
            case "webAuthnChallengeId": return GetNextIdInternal<WebAuthnChallengeIdCounter>(ctx, counterKey, (k, id) => new WebAuthnChallengeIdCounter { Key = k, NextId = id });
            case "openIdConnectGrantId": return GetNextIdInternal<OpenIdConnectGrantIdCounter>(ctx, counterKey, (k, id) => new OpenIdConnectGrantIdCounter { Key = k, NextId = id });
            default: Log.Error($"Unknown counter key: {counterKey}"); return 0;
        }
    }

    // Generic helper to reduce boilerplate
    private static uint GetNextIdInternal<T>(ReducerContext ctx, string key, Func<string, uint, T> createCounter) where T : class, IDbTableKey<string>
    {
        var table = ctx.Db.GetTable<T>();
        var counter = table.Key.Find(key);
        uint nextId;
        if (counter == null)
        {
            nextId = 1;
            counter = createCounter(key, nextId);
            table.Insert(counter);
        }
        else
        {
            dynamic dynamicCounter = counter; // Use dynamic to access NextId generically
            nextId = dynamicCounter.NextId + 1;
            dynamicCounter.NextId = nextId;
            table.Key.Update(counter);
        }
        return nextId;
    }

    /// <summary>
    /// Checks if a user has a specific permission based on their assigned roles.
    /// </summary>
    /// <param name="ctx">The context of the reducer, providing access to the database.</param>
    /// <param name="userId">The identity of the user whose permissions are being checked.</param>
    /// <param name="permissionName">The name of the permission to check for.</param>
    /// <returns>True if the user has the specified permission; otherwise, false.</returns>
    private static bool HasPermission(ReducerContext ctx, Identity userId, string permissionName)
    {
        var roleIds = ctx.Db.UserRole.Iter().Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToList();
        if (!roleIds.Any()) return false; // No roles assigned

        // Check if any role is admin (legacy ID 1)
        var isAdminRole = ctx.Db.Role.Iter().Any(r => roleIds.Contains(r.RoleId) && r.LegacyRoleId == 1);
        if (isAdminRole) return true; // Admins have all permissions

        var permissionIds = ctx.Db.RolePermission.Iter().Where(rp => roleIds.Contains(rp.RoleId)).Select(rp => rp.PermissionId).ToList();
        if (!permissionIds.Any()) return false; // No permissions granted to roles

        return ctx.Db.Permission.Iter().Any(p => permissionIds.Contains(p.PermissionId) && p.Name == permissionName && p.IsActive);
    }

    // ---------- Initialization Reducers ----------
    [SpacetimeDB.Reducer(ReducerKind.Init)]
    public static void Init(ReducerContext ctx)
    {
        Log.Info("Initializing the system...");
        InitializeAdminUser(ctx);
        InitializePermissions(ctx);
        InitializeRoles(ctx); // Roles need permissions to exist first
        InitializeJobs(ctx);
        InitializeEmployees(ctx); // Employees need jobs
        InitializeBuses(ctx);
        InitializeRoutes(ctx); // Routes need drivers(employees) and buses
        InitializeTickets(ctx); // Tickets need routes
        InitializeMaintenance(ctx); // Maintenance needs buses
        InitializeRouteSchedules(ctx); // Schedules need routes
        InitializeSales(ctx); // Sales need tickets and users
        Log.Info("System initialized successfully");
    }

    private static void InitializeAdminUser(ReducerContext ctx)
    {
        Log.Info("Initializing admin user...");
        if (!ctx.Db.UserProfile.Iter().Any(u => u.Login == "admin"))
        {
            uint userId = GetNextId(ctx, "userId");
            var admin = new UserProfile
            {
                UserId = ctx.Sender,
                LegacyUserId = userId,
                Login = "admin",
                Email = "admin@example.com",
                PhoneNumber = "+375333000000",
                PasswordHash = HashPassword("admin"), // Use secure hash
                IsActive = true,
                CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000,
                LegacyGuid = Guid.NewGuid().ToString(),
                EmailConfirmed = true // Admin email is confirmed by default
            };
            ctx.Db.UserProfile.Insert(admin);
            Log.Info("Admin user created successfully");
            
            // Create UserSettings for admin
            CreateUserSettings(ctx, admin.UserId); // Call the reducer
        }
    }

    private static void InitializePermissions(ReducerContext ctx)
    {
        Log.Info("Initializing permissions...");
        var permissions = new[]
        {
            // User Management
            ("users.view", "View users", "User Management"),
            ("users.create", "Create users", "User Management"),
            ("users.edit", "Edit users", "User Management"),
            ("users.delete", "Delete users", "User Management"),
            ("users.view.roles", "View user roles", "User Management"),
            ("users.view.permissions", "View user permissions", "User Management"),
            ("users.assign.roles", "Assign roles to users", "User Management"),
            ("users.remove.roles", "Remove roles from users", "User Management"),

            // Role Management
            ("roles.view", "View roles", "Role Management"),
            ("roles.create", "Create roles", "Role Management"),
            ("roles.edit", "Edit roles", "Role Management"),
            ("roles.delete", "Delete roles", "Role Management"),
            ("roles.view.permissions", "View role permissions", "Role Management"),

            // Permission Management
            ("permissions.view", "View permissions", "Permission Management"),
            ("permissions.create", "Create permissions", "Permission Management"),
            ("permissions.edit", "Edit permissions", "Permission Management"),
            ("permissions.delete", "Delete permissions", "Permission Management"),
            ("permissions.view.categories", "View permission categories", "Permission Management"),
            ("permissions.assign", "Assign permissions to roles", "Permission Management"), // Renamed from grant_permissions

            // Bus Management
            ("buses.view", "View buses", "Bus Management"),
            ("buses.create", "Create buses", "Bus Management"),
            ("buses.edit", "Edit buses", "Bus Management"),
            ("buses.delete", "Delete buses", "Bus Management"),

            // Route Management
            ("routes.view", "View routes", "Route Management"),
            ("routes.create", "Create routes", "Route Management"),
            ("routes.edit", "Edit routes", "Route Management"),
            ("routes.delete", "Delete routes", "Route Management"),

            // Schedule Management
            ("schedules.view", "View schedules", "Schedule Management"),
            ("schedules.create", "Create schedules", "Schedule Management"),
            ("schedules.edit", "Edit schedules", "Schedule Management"),
            ("schedules.delete", "Delete schedules", "Schedule Management"),

            // Ticket Management
            ("tickets.view", "View tickets", "Ticket Management"),
            ("tickets.create", "Create tickets", "Ticket Management"),
            ("tickets.edit", "Edit tickets", "Ticket Management"),
            ("tickets.delete", "Delete tickets", "Ticket Management"),
            ("tickets.cancel", "Cancel tickets", "Ticket Management"),

            // Sales Management
            ("sales.view", "View sales", "Sales Management"),
            ("sales.create", "Create sales", "Sales Management"),
            ("sales.edit", "Edit sales", "Sales Management"),
            ("sales.delete", "Delete sales", "Sales Management"),

            // Maintenance Management
            ("maintenance.view", "View maintenance records", "Maintenance Management"),
            ("maintenance.create", "Create maintenance records", "Maintenance Management"),
            ("maintenance.edit", "Edit maintenance records", "Maintenance Management"),
            ("maintenance.delete", "Delete maintenance records", "Maintenance Management"),

            // Job Management
            ("jobs.view", "View jobs", "Job Management"),
            ("jobs.create", "Create jobs", "Job Management"),
            ("jobs.edit", "Edit jobs", "Job Management"),
            ("jobs.delete", "Delete jobs", "Job Management"),

            // Employee Management
            ("employees.view", "View employees", "Employee Management"),
            ("employees.create", "Create employees", "Employee Management"),
            ("employees.edit", "Edit employees", "Employee Management"),
            ("employees.delete", "Delete employees", "Employee Management"),

            // Reports
            ("reports.view", "View reports", "Reports"),
            ("reports.create", "Create reports", "Reports"),
            ("reports.export", "Export reports", "Reports")
        };
        foreach (var (name, description, category) in permissions) CreatePermission(ctx, name, description, category);
        Log.Info("Permissions initialized.");
    }

    private static void InitializeRoles(ReducerContext ctx)
    {
        Log.Info("Initializing roles...");
        var adminRoleId = CreateRole(ctx, 1, "Administrator", "Full system access", true, 100);
        var userRoleId = CreateRole(ctx, 0, "User", "Basic access", true, 1);
        var managerRoleId = CreateRole(ctx, 2, "Manager", "System management access", true, 50);

        // Assign permissions
        var allPermissions = ctx.Db.Permission.Iter().ToList();
        if (adminRoleId.HasValue) GrantPermissionsToRole(ctx, adminRoleId.Value, allPermissions.Select(p => p.PermissionId));

        var viewPermissions = allPermissions.Where(p => p.Name.EndsWith(".view")).ToList();
        if (userRoleId.HasValue) GrantPermissionsToRole(ctx, userRoleId.Value, viewPermissions.Select(p => p.PermissionId));

        var managerPermissions = allPermissions.Where(p => p.Name.EndsWith(".view") || p.Name.EndsWith(".create") || p.Name.EndsWith(".edit")).ToList();
        if (managerRoleId.HasValue) GrantPermissionsToRole(ctx, managerRoleId.Value, managerPermissions.Select(p => p.PermissionId));

        // Assign admin role to the initial admin user
        var adminUser = ctx.Db.UserProfile.Iter().FirstOrDefault(u => u.Login == "admin");
        if (adminUser != null && adminRoleId.HasValue && !ctx.Db.UserRole.Iter().Any(ur => ur.UserId == adminUser.UserId && ur.RoleId == adminRoleId.Value))
        {
            AssignRole(ctx, adminUser.UserId, adminRoleId.Value, "System");
        }
        Log.Info("Roles initialized and permissions assigned.");
    }

    private static uint? CreateRole(ReducerContext ctx, int legacyRoleId, string name, string description, bool isSystem, uint priority)
    {
        var existingRole = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == name);
        if (existingRole != null) return existingRole.RoleId; // Role already exists

        uint roleId = GetNextId(ctx, "roleId");
        var role = new Role
        {
            RoleId = roleId, LegacyRoleId = legacyRoleId, Name = name, Description = description, IsSystem = isSystem, Priority = priority, IsActive = true,
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000, UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000, CreatedBy = "System", UpdatedBy = "System", NormalizedName = name.ToUpperInvariant()
        };
        ctx.Db.Role.Insert(role);
        return roleId;
    }

    private static void GrantPermissionsToRole(ReducerContext ctx, uint roleId, IEnumerable<uint> permissionIds)
    {
        foreach (var permissionId in permissionIds)
        {
            // Check if permission exists before trying to grant
            if (ctx.Db.Permission.PermissionId.Find(permissionId) == null)
            {
                Log.Warn($"Attempted to grant non-existent permission ID {permissionId} to role ID {roleId}");
                continue;
            }
            
            if (!ctx.Db.RolePermission.Iter().Any(rp => rp.RoleId == roleId && rp.PermissionId == permissionId))
            {
                uint rolePermId = GetNextId(ctx, "rolePermissionId");
                var rolePermission = new RolePermission
                {
                    Id = rolePermId, RoleId = roleId, PermissionId = permissionId, GrantedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000, GrantedBy = "System"
                };
                ctx.Db.RolePermission.Insert(rolePermission);
            }
        }
    }

    private static void CreatePermission(ReducerContext ctx, string name, string description, string category)
    {
        if (ctx.Db.Permission.Iter().Any(p => p.Name == name)) return; // Permission already exists
        uint permissionId = GetNextId(ctx, "permissionId");
        var permission = new Permission
        {
            PermissionId = permissionId, Name = name, Description = description, Category = category, IsActive = true, CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000
        };
        ctx.Db.Permission.Insert(permission);
    }

    private static void AssignRole(ReducerContext ctx, Identity userId, uint roleId, string assignedBy)
    {
        if (!ctx.Db.UserRole.Iter().Any(ur => ur.UserId == userId && ur.RoleId == roleId))
        {
            uint userRoleId = GetNextId(ctx, "userRoleId");
            var userRole = new UserRole
            {
                Id = userRoleId, UserId = userId, RoleId = roleId, AssignedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000, AssignedBy = assignedBy
            };
            ctx.Db.UserRole.Insert(userRole);
        }
    }

    // Initialize other entities... (Jobs, Employees, Buses, Routes, Tickets, Maintenance, Schedules, Sales) - Keeping existing logic
    private static void InitializeJobs(ReducerContext ctx) { if (!ctx.Db.Job.Iter().Any()) Log.Info("Initializing jobs..."); else return; var jobs = new[] { ("Водитель автобуса", "Стажировка (2 года)"), ("Механик", "Стажировка (3 года)"), ("Диспетчер", "Стажировка (1 год)"), ("Начальник автопарка", "Стажировка (5 лет)"), ("Кассир", "Стажировка (6 месяцев)"), ("Инженер по безопасности", "Стажировка (3 года)"), ("Автоэлектрик", "Стажировка (2 года)"), ("Мойщик автобусов", "Стажировка (1 месяц)"), ("Сменный мастер", "Стажировка (4 года)"), ("Контролер", "Стажировка (1 год)") }; foreach (var (title, internship) in jobs) { uint jobId = GetNextId(ctx, "jobId"); ctx.Db.Job.Insert(new Job { JobId = jobId, JobTitle = title, Internship = internship }); } Log.Info("Jobs initialized successfully"); }
    private static void InitializeEmployees(ReducerContext ctx) { if (!ctx.Db.Employee.Iter().Any()) Log.Info("Initializing employees..."); else return; var jobs = ctx.Db.Job.Iter().ToList(); if (jobs.Count == 0) { Log.Error("Cannot initialize employees: No jobs found"); return; } var employees = new[] { ("Иванов", "Иван", "Иванович", new DateTime(2020, 1, 15), 0), ("Петров", "Петр", "Петрович", new DateTime(2019, 3, 20), 0), ("Сидоров", "Алексей", "Михайлович", new DateTime(2018, 6, 10), 1), ("Козлов", "Дмитрий", "Сергеевич", new DateTime(2021, 2, 5), 2), ("Морозов", "Андрей", "Владимирович", new DateTime(2017, 8, 25), 3), ("Новиков", "Сергей", "Александрович", new DateTime(2022, 4, 12), 4), ("Волков", "Михаил", "Дмитриевич", new DateTime(2020, 11, 30), 0), ("Соловьев", "Артем", "Игоревич", new DateTime(2019, 9, 15), 1), ("Васильев", "Николай", "Андреевич", new DateTime(2021, 7, 8), 0), ("Зайцев", "Владимир", "Петрович", new DateTime(2018, 12, 3), 2) }; foreach (var (surname, name, patronym, employedSince, jobIndex) in employees) { uint employeeId = GetNextId(ctx, "employeeId"); var jobId = jobs[jobIndex % jobs.Count].JobId; ulong employedSinceMs = (ulong)((DateTimeOffset)employedSince).ToUnixTimeMilliseconds(); ctx.Db.Employee.Insert(new Employee { EmployeeId = employeeId, Surname = surname, Name = name, Patronym = patronym, EmployedSince = employedSinceMs, JobId = jobId }); } Log.Info("Employees initialized successfully"); }
    private static void InitializeBuses(ReducerContext ctx) { if (!ctx.Db.Bus.Iter().Any()) Log.Info("Initializing buses..."); else return; var buses = new[] { "МАЗ-203.069", "МАЗ-215.069", "МАЗ-107.468", "МАЗ-103.065", "МАЗ-203.169", "МАЗ-105.065", "МАЗ-203.L65", "МАЗ-206.068", "МАЗ-103.465", "МАЗ-107.066" }; foreach (var model in buses) { uint busId = GetNextId(ctx, "busId"); ctx.Db.Bus.Insert(new Bus { BusId = busId, Model = model, RegistrationNumber = $"AB {busId + 1000} 7", IsActive = true }); } Log.Info("Buses initialized successfully"); }
    private static void InitializeRoutes(ReducerContext ctx) { if (!ctx.Db.Route.Iter().Any()) Log.Info("Initializing routes..."); else return; var driverJob = ctx.Db.Job.Iter().FirstOrDefault(j => j.JobTitle == "Водитель автобуса"); if (driverJob == null) { Log.Error("Cannot initialize routes: Driver job not found"); return; } var drivers = ctx.Db.Employee.Iter().Where(e => e.JobId == driverJob.JobId).ToList(); if (drivers.Count == 0) drivers = ctx.Db.Employee.Iter().ToList(); var buses = ctx.Db.Bus.Iter().ToList(); if (buses.Count == 0) { Log.Error("Cannot initialize routes: No buses found"); return; } var routes = new[] { ("Вейнянка", "Фатина", "45 минут"), ("Мал. Боровка", "Солтановка", "50 минут"), ("Вокзал", "Спутник", "40 минут"), ("Мясокомбинат", "Заводская", "35 минут"), ("Броды", "Казимировка", "55 минут"), ("Гребеневский рынок", "Холмы", "45 минут"), ("Автовокзал", "Полыковичи", "40 минут"), ("Центр", "Сидоровичи", "60 минут"), ("Площадь Славы", "Буйничи", "30 минут"), ("Заднепровье", "Химволокно", "25 минут"), ("Вокзал", "Соломинка", "35 минут"), ("Площадь Ленина", "Чаусы", "50 минут"), ("Могилев-2", "Дашковка", "40 минут"), ("Кожзавод", "Сухари", "45 минут"), ("Гребеневский рынок", "Любуж", "30 минут") }; for (int i = 0; i < routes.Length; i++) { var (startPoint, endPoint, travelTime) = routes[i]; uint routeId = GetNextId(ctx, "routeId"); ctx.Db.Route.Insert(new Route { RouteId = routeId, StartPoint = startPoint, EndPoint = endPoint, DriverId = drivers[i % drivers.Count].EmployeeId, BusId = buses[i % buses.Count].BusId, TravelTime = travelTime, IsActive = true }); } Log.Info("Routes initialized successfully"); }
    private static void InitializeTickets(ReducerContext ctx) { if (!ctx.Db.Ticket.Iter().Any()) Log.Info("Initializing tickets..."); else return; var routes = ctx.Db.Route.Iter().ToList(); if (routes.Count == 0) { Log.Error("Cannot initialize tickets: No routes found"); return; } ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); foreach (var route in routes) { uint ticketId = GetNextId(ctx, "ticketId"); ctx.Db.Ticket.Insert(new Ticket { TicketId = ticketId, RouteId = route.RouteId, TicketPrice = 0.75 + (route.RouteId % 3) * 0.10, SeatNumber = 1, PaymentMethod = "cash", IsActive = true, CreatedAt = now, PurchaseTime = now }); } Log.Info("Tickets initialized successfully"); }
    private static void InitializeMaintenance(ReducerContext ctx) { if (!ctx.Db.Maintenance.Iter().Any()) Log.Info("Initializing maintenance records..."); else return; var buses = ctx.Db.Bus.Iter().ToList(); if (buses.Count == 0) { Log.Error("Cannot initialize maintenance: No buses found"); return; } var maintenanceTypes = new[] { ("Замена масла, фильтров", "Закончилось масло, грязные фильтры", "Исправен"), ("Регулировка тормозов", "Тормоза", "Исправен"), ("Замена тормозных колодок", "Тормозные колодки", "Исправен"), ("Диагностика двигателя", "Диагностика двигателя", "Требует внимания"), ("Плановый осмотр", "Плановый осмотр", "Исправен"), ("Замена ремня ГРМ", "Ремень ГРМ", "Исправен"), ("Ремонт системы охлаждения", "Ремонт системы охлаждения", "Исправен"), ("Замена аккумулятора", "Аккумулятор", "Исправен"), ("Диагностика электрики", "Диагностика электрики", "Требует внимания"), ("Плановое ТО", "Плановое ТО", "Исправен") }; var engineers = new[] { "Сидоров А.М.", "Соловьев А.И." }; ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); for (int i = 0; i < buses.Count; i++) { uint maintenanceId = GetNextId(ctx, "maintenanceId"); var (maintenanceType, foundIssues, roadworthiness) = maintenanceTypes[i % maintenanceTypes.Length]; var daysAgo = (i * 5) % 60; ulong lastServiceDate = now - (ulong)(daysAgo * 24 * 60 * 60 * 1000); ulong nextServiceDate = now + (ulong)((90 - daysAgo) * 24 * 60 * 60 * 1000); ctx.Db.Maintenance.Insert(new Maintenance { MaintenanceId = maintenanceId, BusId = buses[i].BusId, LastServiceDate = lastServiceDate, NextServiceDate = nextServiceDate, ServiceEngineer = engineers[i % engineers.Length], FoundIssues = foundIssues, Roadworthiness = roadworthiness, MaintenanceType = maintenanceType, MileageThreshold = "100000 km" }); } Log.Info("Maintenance records initialized successfully"); }
    private static void InitializeRouteSchedules(ReducerContext ctx) { if (!ctx.Db.RouteSchedule.Iter().Any()) Log.Info("Initializing route schedules..."); else return; var routes = ctx.Db.Route.Iter().ToList(); if (routes.Count == 0) { Log.Error("Cannot initialize route schedules: No routes found"); return; } var busTypes = new[] { "МАЗ-103", "МАЗ-107", "МАЗ-215", "МАЗ-231" }; var weekdays = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" }; var weekend = new[] { "Saturday", "Sunday" }; var allDays = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" }; ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); ulong hoursInMs = 60 * 60 * 1000UL; ulong daysInMs = 24 * hoursInMs; ulong thirtyDaysAgo = now - (30 * daysInMs); ulong sixtyDaysAhead = now + (60 * daysInMs); foreach (var route in routes) { uint morningScheduleId = GetNextId(ctx, "scheduleId"); ctx.Db.RouteSchedule.Insert(new RouteSchedule { ScheduleId = morningScheduleId, RouteId = route.RouteId, StartPoint = route.StartPoint, EndPoint = route.EndPoint, RouteStops = new[] { route.StartPoint, "Центр", "Площадь Ленина", route.EndPoint }, DepartureTime = now - (8 * hoursInMs), ArrivalTime = now - (7 * hoursInMs), Price = 0.75 + (route.RouteId % 3) * 0.10, AvailableSeats = 42, DaysOfWeek = allDays, BusTypes = new[] { busTypes[route.RouteId % busTypes.Length] }, IsActive = true, ValidFrom = thirtyDaysAgo, ValidUntil = sixtyDaysAhead, StopDurationMinutes = 5, IsRecurring = true, EstimatedStopTimes = new[] { "06:00", "06:15", "06:30", "06:45" }, StopDistances = new[] { 0.0, 2.5, 4.8, 6.3 }, Notes = $"Утренний рейс {route.StartPoint} - {route.EndPoint}", CreatedAt = now, UpdatedAt = now, UpdatedBy = "System" }); uint afternoonScheduleId = GetNextId(ctx, "scheduleId"); ctx.Db.RouteSchedule.Insert(new RouteSchedule { ScheduleId = afternoonScheduleId, RouteId = route.RouteId, StartPoint = route.StartPoint, EndPoint = route.EndPoint, RouteStops = new[] { route.StartPoint, "Центр", "Площадь Ленина", route.EndPoint }, DepartureTime = now - (2 * hoursInMs), ArrivalTime = now - (1 * hoursInMs), Price = 0.75 + (route.RouteId % 3) * 0.10, AvailableSeats = 42, DaysOfWeek = weekdays, BusTypes = new[] { busTypes[route.RouteId % busTypes.Length] }, IsActive = true, ValidFrom = thirtyDaysAgo, ValidUntil = sixtyDaysAhead, StopDurationMinutes = 5, IsRecurring = true, EstimatedStopTimes = new[] { "14:00", "14:15", "14:30", "14:45" }, StopDistances = new[] { 0.0, 2.5, 4.8, 6.3 }, Notes = $"Дневной рейс {route.StartPoint} - {route.EndPoint}", CreatedAt = now, UpdatedAt = now, UpdatedBy = "System" }); uint eveningScheduleId = GetNextId(ctx, "scheduleId"); ctx.Db.RouteSchedule.Insert(new RouteSchedule { ScheduleId = eveningScheduleId, RouteId = route.RouteId, StartPoint = route.StartPoint, EndPoint = route.EndPoint, RouteStops = new[] { route.StartPoint, "Центр", "Площадь Ленина", route.EndPoint }, DepartureTime = now + (4 * hoursInMs), ArrivalTime = now + (5 * hoursInMs), Price = 0.75 + (route.RouteId % 3) * 0.10, AvailableSeats = 42, DaysOfWeek = allDays, BusTypes = new[] { busTypes[route.RouteId % busTypes.Length] }, IsActive = true, ValidFrom = thirtyDaysAgo, ValidUntil = sixtyDaysAhead, StopDurationMinutes = 5, IsRecurring = true, EstimatedStopTimes = new[] { "18:00", "18:15", "18:30", "18:45" }, StopDistances = new[] { 0.0, 2.5, 4.8, 6.3 }, Notes = $"Вечерний рейс {route.StartPoint} - {route.EndPoint}", CreatedAt = now, UpdatedAt = now, UpdatedBy = "System" }); } Log.Info("Route schedules initialized successfully"); }
    private static void InitializeSales(ReducerContext ctx) { if (!ctx.Db.Sale.Iter().Any()) Log.Info("Initializing sales..."); else return; var tickets = ctx.Db.Ticket.Iter().ToList(); if (tickets.Count == 0) { Log.Error("Cannot initialize sales: No tickets found"); return; } var adminUser = ctx.Db.UserProfile.Iter().FirstOrDefault(u => u.Login == "admin"); ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); ulong hoursInMs = 60 * 60 * 1000UL; ulong daysInMs = 24 * hoursInMs; ulong monthInMs = 30 * daysInMs; for (int month = 6; month >= 0; month--) { for (int day = 1; day <= 5; day++) { if (day % 3 == 0 && month % 2 == 0) continue; for (int i = 0; i < 3; i++) { uint saleId = GetNextId(ctx, "saleId"); var ticketIndex = (month * day + i) % tickets.Count; ulong saleDate = now - ((ulong)month * monthInMs + (ulong)day * daysInMs); ctx.Db.Sale.Insert(new Sale { SaleId = saleId, TicketId = tickets[ticketIndex].TicketId, SaleDate = saleDate, TicketSoldToUser = "Физическая продажа", TicketSoldToUserPhone = "", SellerId = (month < 1 && i % 2 == 0) ? adminUser?.UserId : null, SaleLocation = "В автобусе", SaleNotes = "Продажа билета физически" }); } } } Log.Info("Sales initialized successfully"); }

    // ---------- User Management Reducers ----------
    [SpacetimeDB.Reducer(ReducerKind.ClientConnected)]
    public static void ClientConnected(ReducerContext ctx) { var existingUser = ctx.Db.UserProfile.UserId.Find(ctx.Sender); if (existingUser != null && existingUser.IsActive) { Log.Info($"User {existingUser.Login} connected with identity {ctx.Sender}"); return; } Log.Info($"New client connected with identity {ctx.Sender}"); }

    [SpacetimeDB.Reducer]
    public static void RegisterUser(ReducerContext ctx, string login, string password, string? email, string? phoneNumber, uint? roleId = null, string? roleName = null)
    {
        if (ctx.Db.UserProfile.Iter().Any(u => u.Login == login)) throw new Exception("Login already exists.");
        uint userId = GetNextId(ctx, "userId");
        string hashedPassword = HashPassword(password);
        var user = new UserProfile
        {
            UserId = ctx.Sender, LegacyUserId = userId, Login = login, PasswordHash = hashedPassword, Email = email, PhoneNumber = phoneNumber, IsActive = true,
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000, LegacyGuid = Guid.NewGuid().ToString(), EmailConfirmed = true // Default to true for now
        };
        ctx.Db.UserProfile.Insert(user);
        CreateUserSettings(ctx, user.UserId); // Create default settings
        Log.Info($"User {login} registered successfully");

        // Assign role
        uint? targetRoleId = roleId;
        if (!targetRoleId.HasValue && !string.IsNullOrEmpty(roleName))
        {
            var role = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == roleName);
            if (role == null) throw new Exception($"Role '{roleName}' not found.");
            targetRoleId = role.RoleId;
        }
        if (!targetRoleId.HasValue)
        {
            var defaultRole = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == "User");
            if (defaultRole != null) targetRoleId = defaultRole.RoleId; else Log.Error("Default 'User' role not found!");
        }
        if (targetRoleId.HasValue) AssignRole(ctx, user.UserId, targetRoleId.Value, "System"); else Log.Error($"Could not assign role for user {login}");
    }

    [SpacetimeDB.Reducer]
    public static void AuthenticateUser(ReducerContext ctx, string login, string password)
    {
        var user = ctx.Db.UserProfile.Iter().FirstOrDefault(u => u.Login == login);
        if (user == null || !user.IsActive || !VerifyPassword(password, user.PasswordHash))
        {
            Log.Info($"Authentication failed for user {login}"); return; // Log failure, don't throw
        }
        user.LastLoginAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        ctx.Db.UserProfile.UserId.Update(user);
        Log.Info($"User {user.Login} authenticated successfully");
    }

    [SpacetimeDB.Reducer]
    public static void ClaimUserAccount(ReducerContext ctx, string login, string password)
    {
        var user = ctx.Db.UserProfile.Iter().FirstOrDefault(u => u.Login == login);
        if (user == null) { Log.Error($"Attempt to claim non-existent account: {login}"); return; }
        if (!VerifyPassword(password, user.PasswordHash)) { Log.Error($"Invalid password for account claim: {login}"); return; }
        if (user.IsActive && user.UserId != ctx.Sender) { Log.Error($"Account {login} is already claimed by another identity"); return; }
        user.UserId = ctx.Sender; user.IsActive = true; user.LastLoginAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        ctx.Db.UserProfile.UserId.Update(user); // Use UserId primary key for update
        // Note: We can't directly update UserRole entries based on the old placeholder Identity.
        // The client/service layer needs to handle re-assigning roles after successful claim if needed.
        Log.Info($"User {login} successfully claimed by identity {ctx.Sender}");
    }

    [SpacetimeDB.Reducer]
    public static void UpdateUser(ReducerContext ctx, Identity userId, string? login, string? password, int? roleLegacyId, string? phoneNumber, string? email, bool? isActive)
    {
        if (!HasPermission(ctx, ctx.Sender, "users.edit")) throw new Exception("Unauthorized: You do not have permission to edit users.");
        var user = ctx.Db.UserProfile.UserId.Find(userId);
        if (user == null) throw new Exception("User not found.");

        if (login != null)
        {
            if (ctx.Db.UserProfile.Iter().Any(u => u.Login == login && u.UserId != userId)) throw new Exception("Login already in use by another user.");
            user.Login = login;
        }
        if (password != null) user.PasswordHash = HashPassword(password); // Hash the new password
        if (email != null) user.Email = email;
        if (phoneNumber != null) user.PhoneNumber = phoneNumber;
        if (isActive.HasValue)
        {
            if (!isActive.Value && user.Login == "admin") // Prevent deactivating admin via this reducer
            {
                 var adminRole = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == "Administrator");
                 if (adminRole != null)
                 {
                     var adminCount = ctx.Db.UserRole.Iter().Count(ur => ur.RoleId == adminRole.RoleId && ctx.Db.UserProfile.UserId.Find(ur.UserId)?.IsActive == true);
                     if (adminCount <= 1) throw new Exception("Cannot deactivate the last active administrator.");
                 }
            }
            user.IsActive = isActive.Value;
        }
        ctx.Db.UserProfile.UserId.Update(user);
        Log.Info($"User {userId} updated by {ctx.Sender}");

        // Update role assignment
        if (roleLegacyId.HasValue)
        {
            var targetRole = ctx.Db.Role.Iter().FirstOrDefault(r => r.LegacyRoleId == roleLegacyId.Value);
            if (targetRole == null) throw new Exception($"Role with legacy ID {roleLegacyId.Value} not found.");

            var currentRoles = ctx.Db.UserRole.Iter().Where(ur => ur.UserId == userId).ToList();
            // Remove existing roles (except the new one if already assigned)
            foreach(var currentRole in currentRoles)
            {
                if (currentRole.RoleId != targetRole.RoleId)
                {
                    RemoveRole(ctx, userId, currentRole.RoleId); // Call the RemoveRole reducer
                }
            }
            // Assign the new role if not already assigned
            if (!currentRoles.Any(ur => ur.RoleId == targetRole.RoleId))
            {
                AssignRole(ctx, userId, targetRole.RoleId, ctx.Sender.ToString()); // Call the AssignRole reducer
            }
        }
    }

    [SpacetimeDB.Reducer]
    public static void ActivateUser(ReducerContext ctx, Identity userId)
    {
        if (!HasPermission(ctx, ctx.Sender, "users.edit")) throw new Exception("Unauthorized: You do not have permission to activate users.");
        var user = ctx.Db.UserProfile.UserId.Find(userId);
        if (user == null) throw new Exception("User not found.");
        if (user.IsActive) { Log.Info($"User {userId} is already active"); return; }
        user.IsActive = true; ctx.Db.UserProfile.UserId.Update(user);
        Log.Info($"User {userId} activated by {ctx.Sender}");
    }

    [SpacetimeDB.Reducer]
    public static void DeactivateUser(ReducerContext ctx, Identity userId)
    {
        if (!HasPermission(ctx, ctx.Sender, "users.edit")) throw new Exception("Unauthorized: You do not have permission to deactivate users.");
        var user = ctx.Db.UserProfile.UserId.Find(userId);
        if (user == null) throw new Exception("User not found.");
        if (user.Login == "admin") { // Prevent deactivating admin via this reducer
             var adminRole = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == "Administrator");
             if (adminRole != null) {
                 var adminCount = ctx.Db.UserRole.Iter().Count(ur => ur.RoleId == adminRole.RoleId && ctx.Db.UserProfile.UserId.Find(ur.UserId)?.IsActive == true);
                 if (adminCount <= 1) throw new Exception("Cannot deactivate the last active administrator.");
             }
        }
        if (!user.IsActive) { Log.Info($"User {userId} is already inactive"); return; }
        user.IsActive = false; ctx.Db.UserProfile.UserId.Update(user);
        Log.Info($"User {userId} deactivated by {ctx.Sender}");
    }

    [SpacetimeDB.Reducer]
    public static void ChangePassword(ReducerContext ctx, Identity userId, string currentPassword, string newPassword)
    {
        var user = ctx.Db.UserProfile.UserId.Find(userId);
        if (user == null) throw new Exception("User not found.");
        bool isAdmin = HasPermission(ctx, ctx.Sender, "users.edit");
        bool isSelf = ctx.Sender.Equals(userId);
        if (!isAdmin && !isSelf) throw new Exception("Unauthorized: You can only change your own password unless you have admin privileges.");
        if (isSelf && !isAdmin && !VerifyPassword(currentPassword, user.PasswordHash)) throw new Exception("Current password is incorrect.");
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6) throw new Exception("New password must be at least 6 characters long.");
        user.PasswordHash = HashPassword(newPassword); ctx.Db.UserProfile.UserId.Update(user);
        Log.Info($"Password changed for user {userId} by {ctx.Sender}");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteUser(ReducerContext ctx, Identity userId)
    {
        if (!HasPermission(ctx, ctx.Sender, "users.delete")) throw new Exception("Unauthorized: Missing users.delete permission");
        var userToDelete = ctx.Db.UserProfile.UserId.Find(userId);
        if (userToDelete == null) throw new Exception("User not found.");
        if (userToDelete.Login == "admin") { // Prevent deleting admin
             var adminRole = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == "Administrator");
             if (adminRole != null) {
                 var adminCount = ctx.Db.UserRole.Iter().Count(ur => ur.RoleId == adminRole.RoleId);
                 if (adminCount <= 1) throw new Exception("Cannot delete the last administrator.");
             }
        }
        // Remove related data first
        var userRoles = ctx.Db.UserRole.Iter().Where(ur => ur.UserId == userId).ToList(); foreach (var ur in userRoles) ctx.Db.UserRole.Id.Delete(ur.Id);
        var userSettings = ctx.Db.UserSettings.Iter().FirstOrDefault(us => us.UserId == userId); if (userSettings != null) ctx.Db.UserSettings.UserSettingId.Delete(userSettings.UserSettingId);
        var totpSecrets = ctx.Db.TotpSecret.Iter().Where(ts => ts.UserId == userId).ToList(); foreach(var ts in totpSecrets) ctx.Db.TotpSecret.Id.Delete(ts.Id);
        var webauthnCreds = ctx.Db.WebAuthnCredential.Iter().Where(wc => wc.UserId == userId).ToList(); foreach(var wc in webauthnCreds) ctx.Db.WebAuthnCredential.Id.Delete(wc.Id);
        var magicLinks = ctx.Db.MagicLinkToken.Iter().Where(ml => ml.UserId == userId).ToList(); foreach(var ml in magicLinks) ctx.Db.MagicLinkToken.Token.Delete(ml.Token);
        var twoFactorTokens = ctx.Db.TwoFactorToken.Iter().Where(tft => tft.UserId == userId).ToList(); foreach(var tft in twoFactorTokens) ctx.Db.TwoFactorToken.Id.Delete(tft.Id);
        var qrSessions = ctx.Db.QRSession.Iter().Where(qr => qr.UserId == userId).ToList(); foreach(var qr in qrSessions) ctx.Db.QRSession.SessionId.Delete(qr.SessionId);
        // Finally delete the user profile
        ctx.Db.UserProfile.UserId.Delete(userId);
        Log.Info($"User {userId} and related data deleted by {ctx.Sender}.");
    }

    // ---------- Role Management Reducers ----------
    [SpacetimeDB.Reducer]
    public static void CreateRoleReducer(ReducerContext ctx, int legacyRoleId, string name, string description, bool isSystem, uint priority)
    {
        if (!HasPermission(ctx, ctx.Sender, "roles.create")) throw new Exception("Unauthorized: Missing roles.create permission.");
        if (ctx.Db.Role.Iter().Any(r => r.Name == name)) throw new Exception($"Role '{name}' already exists.");
        if (ctx.Db.Role.Iter().Any(r => r.LegacyRoleId == legacyRoleId)) throw new Exception($"Role with legacy ID {legacyRoleId} already exists.");

        uint roleId = GetNextId(ctx, "roleId");
        var role = new Role
        {
            RoleId = roleId, LegacyRoleId = legacyRoleId, Name = name, Description = description, IsSystem = isSystem, Priority = priority, IsActive = true,
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000, UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000, CreatedBy = ctx.Sender.ToString(), UpdatedBy = ctx.Sender.ToString(), NormalizedName = name.ToUpperInvariant()
        };
        ctx.Db.Role.Insert(role);
        Log.Info($"Created role: {role.Name} ({role.RoleId}) by {ctx.Sender}");
    }

    [SpacetimeDB.Reducer]
    public static void UpdateRoleReducer(ReducerContext ctx, uint roleId, string? name, string? description, int? legacyRoleId, uint? priority)
    {
        if (!HasPermission(ctx, ctx.Sender, "roles.edit")) throw new Exception("Unauthorized: Missing roles.edit permission.");
        var role = ctx.Db.Role.RoleId.Find(roleId);
        if (role == null) throw new Exception("Role not found.");
        if (role.IsSystem) throw new Exception("Cannot modify system roles.");

        if (name != null)
        {
            if (ctx.Db.Role.Iter().Any(r => r.Name == name && r.RoleId != roleId)) throw new Exception("Role name already exists.");
            role.Name = name; role.NormalizedName = name.ToUpperInvariant();
        }
        if (description != null) role.Description = description;
        if (legacyRoleId.HasValue)
        {
            if (ctx.Db.Role.Iter().Any(r => r.LegacyRoleId == legacyRoleId.Value && r.RoleId != roleId)) throw new Exception("Legacy Role ID already exists.");
            role.LegacyRoleId = legacyRoleId.Value;
        }
        if (priority.HasValue) role.Priority = priority.Value;
        role.UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000; role.UpdatedBy = ctx.Sender.ToString();
        ctx.Db.Role.RoleId.Update(role);
        Log.Info($"Role {roleId} updated by {ctx.Sender}");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteRoleReducer(ReducerContext ctx, uint roleId)
    {
        if (!HasPermission(ctx, ctx.Sender, "roles.delete")) throw new Exception("Unauthorized: Missing roles.delete permission.");
        var role = ctx.Db.Role.RoleId.Find(roleId);
        if (role == null) throw new Exception("Role not found.");
        if (role.IsSystem) throw new Exception("Cannot delete system roles.");
        if (ctx.Db.UserRole.Iter().Any(ur => ur.RoleId == roleId)) throw new Exception("Cannot delete role: assigned to users.");

        var rolePermissions = ctx.Db.RolePermission.Iter().Where(rp => rp.RoleId == roleId).ToList(); foreach (var rp in rolePermissions) ctx.Db.RolePermission.Id.Delete(rp.Id);
        ctx.Db.Role.RoleId.Delete(roleId);
        Log.Info($"Role {roleId} and its permissions deleted by {ctx.Sender}");
    }

    [SpacetimeDB.Reducer]
    public static void AssignRole(ReducerContext ctx, Identity userId, uint roleId) // Simplified, assuming internal call or admin
    {
        AssignRole(ctx, userId, roleId, ctx.Sender.ToString()); // Call internal version with sender as assigner
    }

    private static void AssignRole(ReducerContext ctx, Identity userId, uint roleId, string assignedBy)
    {
        if (!ctx.Db.UserProfile.UserId.Find(userId).HasValue) throw new Exception("User not found");
        if (!ctx.Db.Role.RoleId.Find(roleId).HasValue) throw new Exception("Role not found");
        if (ctx.Db.UserRole.Iter().Any(ur => ur.UserId == userId && ur.RoleId == roleId)) return; // Already assigned

        uint id = GetNextId(ctx, "userRoleId");
        var userRole = new UserRole { Id = id, UserId = userId, RoleId = roleId, AssignedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000, AssignedBy = assignedBy };
        ctx.Db.UserRole.Insert(userRole);
        Log.Info($"Role {roleId} assigned to user {userId} by {assignedBy}");
    }

    [SpacetimeDB.Reducer]
    public static void RemoveRole(ReducerContext ctx, Identity userId, uint roleId)
    {
        if (!HasPermission(ctx, ctx.Sender, "users.remove.roles")) throw new Exception("Unauthorized: Missing users.remove.roles permission.");
        var userRole = ctx.Db.UserRole.Iter().FirstOrDefault(ur => ur.UserId == userId && ur.RoleId == roleId);
        if (userRole == null) throw new Exception("User does not have this role.");

        // Prevent removing the last admin role from the admin user
        var role = ctx.Db.Role.RoleId.Find(roleId);
        var user = ctx.Db.UserProfile.UserId.Find(userId);
        if (role != null && user != null && role.Name == "Administrator" && user.Login == "admin")
        {
             var adminRole = ctx.Db.Role.Iter().FirstOrDefault(r => r.Name == "Administrator");
             if (adminRole != null) {
                 var adminCount = ctx.Db.UserRole.Iter().Count(ur => ur.RoleId == adminRole.RoleId);
                 if (adminCount <= 1) throw new Exception("Cannot remove the last administrator role from the admin user.");
             }
        }

        ctx.Db.UserRole.Id.Delete(userRole.Id);
        Log.Info($"Role {roleId} removed from user {userId} by {ctx.Sender}");
    }

    // ---------- Permission Management Reducers ----------
    [SpacetimeDB.Reducer]
    public static void AddNewPermission(ReducerContext ctx, string name, string description, string category)
    {
        if (!HasPermission(ctx, ctx.Sender, "permissions.create")) throw new Exception("Unauthorized: Missing permissions.create permission.");
        if (ctx.Db.Permission.Iter().Any(p => p.Name == name)) throw new Exception($"Permission '{name}' already exists.");
        uint permissionId = GetNextId(ctx, "permissionId");
        var permission = new Permission { PermissionId = permissionId, Name = name, Description = description, Category = category, IsActive = true, CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000 };
        ctx.Db.Permission.Insert(permission);
        Log.Info($"Created permission: {permission.Name} ({permission.PermissionId}) by {ctx.Sender}");
    }

    [SpacetimeDB.Reducer]
    public static void UpdatePermission(ReducerContext ctx, uint permissionId, string? name, string? description, string? category, bool? isActive)
    {
        if (!HasPermission(ctx, ctx.Sender, "permissions.edit")) throw new Exception("Unauthorized: Missing permissions.edit permission.");
        var permission = ctx.Db.Permission.PermissionId.Find(permissionId);
        if (permission == null) throw new Exception("Permission not found.");

        if (name != null) { if (ctx.Db.Permission.Iter().Any(p => p.Name == name && p.PermissionId != permissionId)) throw new Exception("Permission name already exists."); permission.Name = name; }
        if (description != null) permission.Description = description;
        if (category != null) permission.Category = category;
        if (isActive.HasValue) permission.IsActive = isActive.Value;
        ctx.Db.Permission.PermissionId.Update(permission);
        Log.Info($"Updated permission {permissionId} by {ctx.Sender}");
    }

    [SpacetimeDB.Reducer]
    public static void DeletePermission(ReducerContext ctx, uint permissionId)
    {
        if (!HasPermission(ctx, ctx.Sender, "permissions.delete")) throw new Exception("Unauthorized: Missing permissions.delete permission.");
        if (!ctx.Db.Permission.PermissionId.Find(permissionId).HasValue) throw new Exception("Permission not found.");
        if (ctx.Db.RolePermission.Iter().Any(rp => rp.PermissionId == permissionId)) throw new Exception("Cannot delete permission: assigned to roles.");
        ctx.Db.Permission.PermissionId.Delete(permissionId);
        Log.Info($"Permission {permissionId} deleted by {ctx.Sender}");
    }

    [SpacetimeDB.Reducer]
    public static void GrantPermissionToRole(ReducerContext ctx, uint roleId, uint permissionId)
    {
        if (!HasPermission(ctx, ctx.Sender, "permissions.assign")) throw new Exception("Unauthorized: Missing permissions.assign permission.");
        if (!ctx.Db.Role.RoleId.Find(roleId).HasValue) throw new Exception("Role not found.");
        if (!ctx.Db.Permission.PermissionId.Find(permissionId).HasValue) throw new Exception("Permission not found.");
        if (ctx.Db.RolePermission.Iter().Any(rp => rp.RoleId == roleId && rp.PermissionId == permissionId)) return; // Already granted

        uint id = GetNextId(ctx, "rolePermissionId");
        var rolePermission = new RolePermission { Id = id, RoleId = roleId, PermissionId = permissionId, GrantedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000, GrantedBy = ctx.Sender.ToString() };
        ctx.Db.RolePermission.Insert(rolePermission);
        Log.Info($"Permission {permissionId} granted to role {roleId} by {ctx.Sender}");
    }

    [SpacetimeDB.Reducer]
    public static void RevokePermissionFromRole(ReducerContext ctx, uint roleId, uint permissionId)
    {
        if (!HasPermission(ctx, ctx.Sender, "permissions.assign")) throw new Exception("Unauthorized: Missing permissions.assign permission.");
        var rolePermission = ctx.Db.RolePermission.Iter().FirstOrDefault(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);
        if (rolePermission == null) throw new Exception("Permission not granted to this role.");
        ctx.Db.RolePermission.Id.Delete(rolePermission.Id);
        Log.Info($"Permission {permissionId} revoked from role {roleId} by {ctx.Sender}");
    }

    // ---------- Bus Management Reducers ----------
    [SpacetimeDB.Reducer]
    public static void CreateBus(ReducerContext ctx, string model, string? registrationNumber)
    {
        if (!HasPermission(ctx, ctx.Sender, "buses.create")) throw new Exception("Unauthorized: Missing buses.create permission.");
        uint busId = GetNextId(ctx, "busId");
        var bus = new Bus { BusId = busId, Model = model, RegistrationNumber = registrationNumber, IsActive = true };
        ctx.Db.Bus.Insert(bus);
        Log.Info($"Created bus {model} ({busId}) by {ctx.Sender}");
    }
    [SpacetimeDB.Reducer] public static void UpdateBus(ReducerContext ctx, uint busId, string? model, string? registrationNumber) { if (!HasPermission(ctx, ctx.Sender, "buses.edit")) throw new Exception("Unauthorized: Missing buses.edit permission."); var bus = ctx.Db.Bus.BusId.Find(busId); if (bus == null) throw new Exception("Bus not found."); if (model != null) bus.Model = model; if (registrationNumber != null) bus.RegistrationNumber = registrationNumber; ctx.Db.Bus.BusId.Update(bus); Log.Info($"Bus {busId} updated by {ctx.Sender}"); }
    [SpacetimeDB.Reducer] public static void DeleteBus(ReducerContext ctx, uint busId) { if (!HasPermission(ctx, ctx.Sender, "buses.delete")) throw new Exception("Unauthorized: Missing buses.delete permission."); if (!ctx.Db.Bus.BusId.Find(busId).HasValue) throw new Exception("Bus Not found"); if (ctx.Db.Route.Iter().Any(r=>r.BusId == busId)) throw new Exception("Cannot delete bus: assigned to routes."); ctx.Db.Bus.BusId.Delete(busId); Log.Info($"Bus {busId} deleted by {ctx.Sender}."); }
    [SpacetimeDB.Reducer] public static void ActivateBus(ReducerContext ctx, uint busId) { if (!HasPermission(ctx, ctx.Sender, "buses.edit")) throw new Exception("Unauthorized: Missing buses.edit permission."); var bus = ctx.Db.Bus.BusId.Find(busId); if (bus == null) throw new Exception("Bus not found."); if (bus.IsActive) { Log.Info($"Bus {busId} is already active"); return; } bus.IsActive = true; ctx.Db.Bus.BusId.Update(bus); Log.Info($"Bus {busId} activated by {ctx.Sender}"); }
    [SpacetimeDB.Reducer] public static void DeactivateBus(ReducerContext ctx, uint busId) { if (!HasPermission(ctx, ctx.Sender, "buses.edit")) throw new Exception("Unauthorized: Missing buses.edit permission."); var bus = ctx.Db.Bus.BusId.Find(busId); if (bus == null) throw new Exception("Bus not found."); if (ctx.Db.Route.Iter().Any(r => r.BusId == busId && r.IsActive)) throw new Exception($"Cannot deactivate bus: used in active routes."); if (!bus.IsActive) { Log.Info($"Bus {busId} is already inactive"); return; } bus.IsActive = false; ctx.Db.Bus.BusId.Update(bus); Log.Info($"Bus {busId} deactivated by {ctx.Sender}"); }

    // ---------- Route Management Reducers ----------
    [SpacetimeDB.Reducer] public static void CreateRoute(ReducerContext ctx, string startPoint, string endPoint, uint driverId, uint busId, string? travelTime = null, bool isActive = true) { if (!HasPermission(ctx, ctx.Sender, "routes.create")) throw new Exception("Unauthorized: Missing routes.create permission."); if (!ctx.Db.Employee.EmployeeId.Find(driverId).HasValue) throw new Exception("Driver not found."); if (!ctx.Db.Bus.BusId.Find(busId).HasValue) throw new Exception("Bus not found."); uint routeId = GetNextId(ctx, "routeId"); var route = new Route { RouteId = routeId, StartPoint = startPoint, EndPoint = endPoint, DriverId = driverId, BusId = busId, TravelTime = travelTime, IsActive = isActive }; ctx.Db.Route.Insert(route); Log.Info($"Created route {startPoint}-{endPoint} ({routeId}) by {ctx.Sender}"); }
    [SpacetimeDB.Reducer] public static void UpdateRoute(ReducerContext ctx, uint routeId, string? startPoint, string? endPoint, uint? driverId, uint? busId, string? travelTime, bool? isActive) { if (!HasPermission(ctx, ctx.Sender, "routes.edit")) throw new Exception("Unauthorized: Missing routes.edit permission."); var route = ctx.Db.Route.RouteId.Find(routeId); if (route == null) throw new Exception("Route not found"); if (startPoint != null) route.StartPoint = startPoint; if (endPoint != null) route.EndPoint = endPoint; if (driverId.HasValue) { if (!ctx.Db.Employee.EmployeeId.Find(driverId.Value).HasValue) throw new Exception("Driver not found."); route.DriverId = driverId.Value; } if (busId.HasValue) { if (!ctx.Db.Bus.BusId.Find(busId.Value).HasValue) throw new Exception("Bus not found."); route.BusId = busId.Value; } if (travelTime != null) route.TravelTime = travelTime; if (isActive.HasValue) route.IsActive = isActive.Value; ctx.Db.Route.RouteId.Update(route); Log.Info($"Route {routeId} updated by {ctx.Sender}"); }
    [SpacetimeDB.Reducer] public static void DeleteRoute(ReducerContext ctx, uint routeId) { if (!HasPermission(ctx, ctx.Sender, "routes.delete")) throw new Exception("Unauthorized: Missing routes.delete permission."); if (!ctx.Db.Route.RouteId.Find(routeId).HasValue) throw new Exception("Route not found."); if (ctx.Db.RouteSchedule.Iter().Any(s=>s.RouteId == routeId)) throw new Exception("Cannot delete route: has schedules."); ctx.Db.Route.RouteId.Delete(routeId); Log.Info($"Route {routeId} deleted by {ctx.Sender}."); }
    [SpacetimeDB.Reducer] public static void ActivateRoute(ReducerContext ctx, uint routeId) { if (!HasPermission(ctx, ctx.Sender, "routes.edit")) throw new Exception("Unauthorized: Missing routes.edit permission."); var route = ctx.Db.Route.RouteId.Find(routeId); if (route == null) throw new Exception("Route not found."); var bus = ctx.Db.Bus.BusId.Find(route.BusId); if (bus == null || !bus.IsActive) throw new Exception("Cannot activate route: assigned bus is inactive or not found."); if (route.IsActive) { Log.Info($"Route {routeId} is already active"); return; } route.IsActive = true; ctx.Db.Route.RouteId.Update(route); Log.Info($"Route {routeId} activated by {ctx.Sender}"); }
    [SpacetimeDB.Reducer] public static void DeactivateRoute(ReducerContext ctx, uint routeId) { if (!HasPermission(ctx, ctx.Sender, "routes.edit")) throw new Exception("Unauthorized: Missing routes.edit permission."); var route = ctx.Db.Route.RouteId.Find(routeId); if (route == null) throw new Exception("Route not found."); if (ctx.Db.RouteSchedule.Iter().Any(s => s.RouteId == routeId && s.IsActive)) throw new Exception($"Cannot deactivate route: has active schedules."); if (!route.IsActive) { Log.Info($"Route {routeId} is already inactive"); return; } route.IsActive = false; ctx.Db.Route.RouteId.Update(route); Log.Info($"Route {routeId} deactivated by {ctx.Sender}"); }

    // ---------- Schedule Management Reducers ----------
    [SpacetimeDB.Reducer] public static void CreateRouteSchedule(ReducerContext ctx, uint routeId, ulong departureTime, double price, uint availableSeats, List<string>? daysOfWeek, string? startPoint, string? endPoint, List<string>? routeStops, ulong? arrivalTime, uint? stopDurationMinutes, bool? isRecurring, List<string>? estimatedStopTimes, List<double>? stopDistances, string? notes) { if (!HasPermission(ctx, ctx.Sender, "schedules.create")) throw new Exception("Unauthorized: Missing schedules.create permission."); if (!ctx.Db.Route.RouteId.Find(routeId).HasValue) throw new Exception("Route not found."); uint scheduleId = GetNextId(ctx, "scheduleId"); var schedule = new RouteSchedule { ScheduleId = scheduleId, RouteId = routeId, DepartureTime = departureTime, ArrivalTime = arrivalTime ?? (departureTime + 3600000), Price = price, AvailableSeats = availableSeats, DaysOfWeek = daysOfWeek?.ToArray(), StartPoint = startPoint, EndPoint = endPoint, RouteStops = routeStops?.ToArray(), StopDurationMinutes = stopDurationMinutes ?? 5, IsRecurring = isRecurring ?? true, EstimatedStopTimes = estimatedStopTimes?.ToArray(), StopDistances = stopDistances?.ToArray(), Notes = notes, CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000, UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000, UpdatedBy = ctx.Sender.ToString(), IsActive = true, ValidFrom = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000 }; ctx.Db.RouteSchedule.Insert(schedule); Log.Info($"Created schedule {scheduleId} for route {routeId} by {ctx.Sender}"); }
    [SpacetimeDB.Reducer] public static void UpdateRouteSchedule(ReducerContext ctx, uint scheduleId, uint? routeId, string? startPoint, string? endPoint, List<string>? routeStops, ulong? departureTime, ulong? arrivalTime, double? price, uint? availableSeats, List<string>? daysOfWeek, List<string>? busTypes, uint? stopDurationMinutes, bool? isRecurring, List<string>? estimatedStopTimes, List<double>? stopDistances, string? notes) { if (!HasPermission(ctx, ctx.Sender, "schedules.edit")) throw new Exception("Unauthorized: Missing schedules.edit permission."); var schedule = ctx.Db.RouteSchedule.ScheduleId.Find(scheduleId); if (schedule == null) throw new Exception("Route schedule not found."); if (routeId.HasValue) { if (!ctx.Db.Route.RouteId.Find(routeId.Value).HasValue) throw new Exception("Route not found."); schedule.RouteId = routeId.Value; } if (!string.IsNullOrEmpty(startPoint)) schedule.StartPoint = startPoint; if (!string.IsNullOrEmpty(endPoint)) schedule.EndPoint = endPoint; if (routeStops != null) schedule.RouteStops = routeStops.ToArray(); if (departureTime.HasValue) schedule.DepartureTime = departureTime.Value; if (arrivalTime.HasValue) schedule.ArrivalTime = arrivalTime.Value; if (price.HasValue) schedule.Price = price.Value; if (availableSeats.HasValue) schedule.AvailableSeats = availableSeats.Value; if (daysOfWeek != null) schedule.DaysOfWeek = daysOfWeek.ToArray(); if (busTypes != null) schedule.BusTypes = busTypes.ToArray(); if (stopDurationMinutes.HasValue) schedule.StopDurationMinutes = stopDurationMinutes.Value; if (isRecurring.HasValue) schedule.IsRecurring = isRecurring.Value; if (estimatedStopTimes != null) schedule.EstimatedStopTimes = estimatedStopTimes.ToArray(); if (stopDistances != null) schedule.StopDistances = stopDistances.ToArray(); if (!string.IsNullOrEmpty(notes)) schedule.Notes = notes; schedule.UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000; schedule.UpdatedBy = ctx.Sender.ToString(); ctx.Db.RouteSchedule.ScheduleId.Update(schedule); Log.Info($"Route schedule {scheduleId} updated by {ctx.Sender}"); }
    [SpacetimeDB.Reducer] public static void DeleteRouteSchedule(ReducerContext ctx, uint scheduleId) { if (!HasPermission(ctx, ctx.Sender, "schedules.delete")) throw new Exception("Unauthorized: Missing schedules.delete permission."); if (!ctx.Db.RouteSchedule.ScheduleId.Find(scheduleId).HasValue) throw new Exception("Route schedule not found."); if(ctx.Db.Ticket.Iter().Any(t => t.RouteId == ctx.Db.RouteSchedule.ScheduleId.Find(scheduleId)?.RouteId)) throw new Exception("Cannot delete schedule: has tickets."); ctx.Db.RouteSchedule.ScheduleId.Delete(scheduleId); Log.Info($"Route schedule {scheduleId} deleted by {ctx.Sender}."); }
    [SpacetimeDB.Reducer] public static void ActivateSchedule(ReducerContext ctx, uint scheduleId) { if (!HasPermission(ctx, ctx.Sender, "schedules.edit")) throw new Exception("Unauthorized: Missing schedules.edit permission."); var schedule = ctx.Db.RouteSchedule.ScheduleId.Find(scheduleId); if (schedule == null) throw new Exception("Schedule not found."); var route = ctx.Db.Route.RouteId.Find(schedule.RouteId); if (route == null || !route.IsActive) throw new Exception("Cannot activate schedule: associated route is inactive or not found."); if (schedule.IsActive) { Log.Info($"Schedule {scheduleId} is already active"); return; } schedule.IsActive = true; schedule.UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000; schedule.UpdatedBy = ctx.Sender.ToString(); ctx.Db.RouteSchedule.ScheduleId.Update(schedule); Log.Info($"Schedule {scheduleId} activated by {ctx.Sender}"); }
    [SpacetimeDB.Reducer] public static void DeactivateSchedule(ReducerContext ctx, uint scheduleId) { if (!HasPermission(ctx, ctx.Sender, "schedules.edit")) throw new Exception("Unauthorized: Missing schedules.edit permission."); var schedule = ctx.Db.RouteSchedule.ScheduleId.Find(scheduleId); if (schedule == null) throw new Exception("Schedule not found."); /* Add check for recent sales if needed */ if (!schedule.IsActive) { Log.Info($"Schedule {scheduleId} is already inactive"); return; } schedule.IsActive = false; schedule.UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000; schedule.UpdatedBy = ctx.Sender.ToString(); ctx.Db.RouteSchedule.ScheduleId.Update(schedule); Log.Info($"Schedule {scheduleId} deactivated by {ctx.Sender}"); }

    // ---------- Job Management Reducers ----------
    [SpacetimeDB.Reducer] public static void CreateJob(ReducerContext ctx, string jobTitle, string? jobInternship) { if (!HasPermission(ctx, ctx.Sender, "jobs.create")) throw new Exception("Unauthorized: Missing jobs.create permission."); if (ctx.Db.Job.Iter().Any(j => j.JobTitle == jobTitle)) throw new Exception("Job title already exists."); uint jobId = GetNextId(ctx, "jobId"); var job = new Job { JobId = jobId, JobTitle = jobTitle, Internship = jobInternship }; ctx.Db.Job.Insert(job); Log.Info($"Created job {jobTitle} ({jobId}) by {ctx.Sender}"); }
    [SpacetimeDB.Reducer] public static void UpdateJob(ReducerContext ctx, uint jobId, string? jobTitle, string? jobInternship) { if (!HasPermission(ctx, ctx.Sender, "jobs.edit")) throw new Exception("Unauthorized: Missing jobs.edit permission."); var job = ctx.Db.Job.JobId.Find(jobId); if (job == null) throw new Exception("Job not found."); if (jobTitle != null) { if (ctx.Db.Job.Iter().Any(j => j.JobTitle == jobTitle && j.JobId != jobId)) throw new Exception("Job title already exists."); job.JobTitle = jobTitle; } if (jobInternship != null) job.Internship = jobInternship; ctx.Db.Job.JobId.Update(job); Log.Info($"Job {jobId} updated by {ctx.Sender}"); }
    [SpacetimeDB.Reducer] public static void DeleteJob(ReducerContext ctx, uint jobId) { if (!HasPermission(ctx, ctx.Sender, "jobs.delete")) throw new Exception("Unauthorized: Missing jobs.delete permission."); if (!ctx.Db.Job.JobId.Find(jobId).HasValue) throw new Exception("Job not found."); if (ctx.Db.Employee.Iter().Any(e => e.JobId == jobId)) throw new Exception("Cannot delete job: employees assigned."); ctx.Db.Job.JobId.Delete(jobId); Log.Info($"Job {jobId} deleted by {ctx.Sender}."); }

    // ---------- Employee Management Reducers ----------
    [SpacetimeDB.Reducer] public static void CreateEmployee(ReducerContext ctx, string employeeName, string employeeSurname, string? employeePatronym, uint jobId) { if (!HasPermission(ctx, ctx.Sender, "employees.create")) throw new Exception("Unauthorized: Missing employees.create permission."); if (!ctx.Db.Job.JobId.Find(jobId).HasValue) throw new Exception("Job not found."); uint employeeId = GetNextId(ctx, "employeeId"); var employee = new Employee { EmployeeId = employeeId, Name = employeeName, Surname = employeeSurname, Patronym = employeePatronym, JobId = jobId, EmployedSince = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000 }; ctx.Db.Employee.Insert(employee); Log.Info($"Created employee {employeeName} {employeeSurname} ({employeeId}) by {ctx.Sender}"); }
    [SpacetimeDB.Reducer] public static void UpdateEmployee(ReducerContext ctx, uint employeeId, string? employeeName, string? employeeSurname, string? employeePatronym, uint? jobId) { if (!HasPermission(ctx, ctx.Sender, "employees.edit")) throw new Exception("Unauthorized: Missing employees.edit permission."); var employee = ctx.Db.Employee.EmployeeId.Find(employeeId); if (employee == null) throw new Exception("Employee not found."); if (employeeName != null) employee.Name = employeeName; if (employeeSurname != null) employee.Surname = employeeSurname; if (employeePatronym != null) employee.Patronym = employeePatronym; if (jobId.HasValue) { if (!ctx.Db.Job.JobId.Find(jobId.Value).HasValue) throw new Exception("Job not found."); employee.JobId = jobId.Value; } ctx.Db.Employee.EmployeeId.Update(employee); Log.Info($"Employee {employeeId} updated by {ctx.Sender}"); }
    [SpacetimeDB.Reducer] public static void DeleteEmployee(ReducerContext ctx, uint employeeId) { if (!HasPermission(ctx, ctx.Sender, "employees.delete")) throw new Exception("Unauthorized: Missing employees.delete permission."); if (!ctx.Db.Employee.EmployeeId.Find(employeeId).HasValue) throw new Exception("Employee not found"); if (ctx.Db.Route.Iter().Any(r => r.DriverId == employeeId)) throw new Exception("Cannot delete employee: assigned to routes."); ctx.Db.Employee.EmployeeId.Delete(employeeId); Log.Info($"Employee {employeeId} deleted by {ctx.Sender}."); }

    // ---------- Ticket Management Reducers ----------
    [SpacetimeDB.Reducer] public static void CreateTicket(ReducerContext ctx, uint routeId, double price, uint seatNumber, string? paymentMethod, ulong? purchaseTime) { if (!HasPermission(ctx, ctx.Sender, "tickets.create")) throw new Exception("Unauthorized: Missing tickets.create permission."); if (!ctx.Db.Route.RouteId.Find(routeId).HasValue) throw new Exception("Route not found"); if (ctx.Db.Ticket.Iter().Any(t => t.RouteId == routeId && t.SeatNumber == seatNumber && t.IsActive)) throw new Exception("Seat is already taken"); uint ticketId = GetNextId(ctx, "ticketId"); var ticket = new Ticket { TicketId = ticketId, RouteId = routeId, TicketPrice = price, SeatNumber = seatNumber, PaymentMethod = paymentMethod ?? "Cash", IsActive = true, CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000, PurchaseTime = purchaseTime ?? (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000 }; ctx.Db.Ticket.Insert(ticket); Log.Info($"Created ticket {ticketId} for route {routeId} by {ctx.Sender}"); }
    [SpacetimeDB.Reducer] public static void UpdateTicket(ReducerContext ctx, uint ticketId, uint? routeId, uint? seatNumber, double? ticketPrice, string? paymentMethod, bool? isActive) { if (!HasPermission(ctx, ctx.Sender, "tickets.edit")) throw new Exception("Unauthorized: Missing tickets.edit permission."); var ticket = ctx.Db.Ticket.TicketId.Find(ticketId); if (ticket == null) throw new Exception("Ticket not found."); if (routeId.HasValue) { if (!ctx.Db.Route.RouteId.Find(routeId.Value).HasValue) throw new Exception("Route not found"); ticket.RouteId = routeId.Value; } if (seatNumber.HasValue) { if (ctx.Db.Ticket.Iter().Any(t => t.RouteId == (routeId ?? ticket.RouteId) && t.SeatNumber == seatNumber.Value && t.TicketId != ticketId && t.IsActive)) throw new Exception("Seat is already taken"); ticket.SeatNumber = seatNumber.Value; } if (ticketPrice.HasValue) ticket.TicketPrice = ticketPrice.Value; if (paymentMethod != null) ticket.PaymentMethod = paymentMethod; if (isActive.HasValue) ticket.IsActive = isActive.Value; ticket.UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000; ticket.UpdatedBy = ctx.Sender.ToString(); ctx.Db.Ticket.TicketId.Update(ticket); Log.Info($"Ticket {ticketId} updated by {ctx.Sender}"); }
    [SpacetimeDB.Reducer] public static void DeleteTicket(ReducerContext ctx, uint ticketId) { if (!HasPermission(ctx, ctx.Sender, "tickets.delete")) throw new Exception("Unauthorized: Missing tickets.delete permission."); if (!ctx.Db.Ticket.TicketId.Find(ticketId).HasValue) throw new Exception("Ticket not found."); if (ctx.Db.Sale.Iter().Any(s => s.TicketId == ticketId)) throw new Exception("Cannot delete ticket: it has been sold."); ctx.Db.Ticket.TicketId.Delete(ticketId); Log.Info($"Ticket {ticketId} deleted by {ctx.Sender}."); }
    [SpacetimeDB.Reducer] public static void CancelTicket(ReducerContext ctx, uint ticketId) { if (!HasPermission(ctx, ctx.Sender, "tickets.cancel")) throw new Exception("Unauthorized: Missing tickets.cancel permission."); var ticket = ctx.Db.Ticket.TicketId.Find(ticketId); if (ticket == null) throw new Exception("Ticket not found"); if (!ticket.IsActive) { Log.Info($"Ticket {ticketId} is already cancelled"); return; } ticket.IsActive = false; ticket.UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000; ticket.UpdatedBy = ctx.Sender.ToString(); ctx.Db.Ticket.TicketId.Update(ticket); Log.Info($"Ticket {ticketId} cancelled by {ctx.Sender}"); }

    // ---------- Sale Management Reducers ----------
    [SpacetimeDB.Reducer] public static void CreateSale(ReducerContext ctx, uint ticketId, string buyerName, string buyerPhone, string? saleLocation, string? saleNotes) { if (!HasPermission(ctx, ctx.Sender, "sales.create")) throw new Exception("Unauthorized: Missing sales.create permission."); if (!ctx.Db.Ticket.TicketId.Find(ticketId).HasValue) throw new Exception("Ticket not found."); if (ctx.Db.Sale.Iter().Any(s => s.TicketId == ticketId)) throw new Exception("Ticket already sold."); uint saleId = GetNextId(ctx, "saleId"); var sale = new Sale { SaleId = saleId, TicketId = ticketId, SaleDate = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000, TicketSoldToUser = buyerName, TicketSoldToUserPhone = buyerPhone, SellerId = ctx.Sender, SaleLocation = saleLocation, SaleNotes = saleNotes }; ctx.Db.Sale.Insert(sale); Log.Info($"Created sale {saleId} for ticket {ticketId} by {ctx.Sender}"); }
    [SpacetimeDB.Reducer] public static void UpdateSale(ReducerContext ctx, uint saleId, uint? ticketId, string? ticketSoldToUser, string? ticketSoldToUserPhone, string? saleLocation, string? saleNotes) { if (!HasPermission(ctx, ctx.Sender, "sales.edit")) throw new Exception("Unauthorized: Missing sales.edit permission."); var sale = ctx.Db.Sale.SaleId.Find(saleId); if (sale == null) throw new Exception("Sale not found."); if (ticketId.HasValue) { if (!ctx.Db.Ticket.TicketId.Find(ticketId.Value).HasValue) throw new Exception("Ticket not found."); if (ctx.Db.Sale.Iter().Any(s=>s.TicketId == ticketId.Value && s.SaleId != saleId)) throw new Exception("Ticket already associated with another sale."); sale.TicketId = ticketId.Value; } if (ticketSoldToUser != null) sale.TicketSoldToUser = ticketSoldToUser; if (ticketSoldToUserPhone != null) sale.TicketSoldToUserPhone = ticketSoldToUserPhone; if (saleLocation != null) sale.SaleLocation = saleLocation; if (saleNotes != null) sale.SaleNotes = saleNotes; /* SellerId and SaleDate usually shouldn't be updated */ ctx.Db.Sale.SaleId.Update(sale); Log.Info($"Sale {saleId} updated by {ctx.Sender}"); }
    [SpacetimeDB.Reducer] public static void DeleteSale(ReducerContext ctx, uint saleId) { if (!HasPermission(ctx, ctx.Sender, "sales.delete")) throw new Exception("Unauthorized: Missing sales.delete permission."); if (!ctx.Db.Sale.SaleId.Find(saleId).HasValue) throw new Exception("Sale not found."); ctx.Db.Sale.SaleId.Delete(saleId); Log.Info($"Sale {saleId} deleted by {ctx.Sender}."); }

    // ---------- Maintenance Management Reducers ----------
    [SpacetimeDB.Reducer] public static void CreateMaintenance(ReducerContext ctx, uint busId, ulong lastServiceDate, string serviceEngineer, string foundIssues, ulong nextServiceDate, string roadworthiness, string maintenanceType) { if (!HasPermission(ctx, ctx.Sender, "maintenance.create")) throw new Exception("Unauthorized: Missing maintenance.create permission."); if (!ctx.Db.Bus.BusId.Find(busId).HasValue) throw new Exception("Bus not found."); uint maintenanceId = GetNextId(ctx, "maintenanceId"); var maintenance = new Maintenance { MaintenanceId = maintenanceId, BusId = busId, LastServiceDate = lastServiceDate, NextServiceDate = nextServiceDate, ServiceEngineer = serviceEngineer, FoundIssues = foundIssues, Roadworthiness = roadworthiness, MaintenanceType = maintenanceType }; ctx.Db.Maintenance.Insert(maintenance); Log.Info($"Created maintenance record {maintenanceId} for bus {busId} by {ctx.Sender}"); }
    [SpacetimeDB.Reducer] public static void UpdateMaintenance(ReducerContext ctx, uint maintenanceId, uint? busId, ulong? lastServiceDate, string? serviceEngineer, string? foundIssues, ulong? nextServiceDate, string? roadworthiness, string? maintenanceType, string? mileage) { if (!HasPermission(ctx, ctx.Sender, "maintenance.edit")) throw new Exception("Unauthorized: Missing maintenance.edit permission."); var maintenance = ctx.Db.Maintenance.MaintenanceId.Find(maintenanceId); if (maintenance == null) throw new Exception("Maintenance Record not found."); if (busId.HasValue) { if (!ctx.Db.Bus.BusId.Find(busId.Value).HasValue) throw new Exception("Bus not found."); maintenance.BusId = busId.Value; } if (lastServiceDate.HasValue) maintenance.LastServiceDate = lastServiceDate.Value; if (nextServiceDate.HasValue) maintenance.NextServiceDate = nextServiceDate.Value; if (serviceEngineer != null) maintenance.ServiceEngineer = serviceEngineer; if (foundIssues != null) maintenance.FoundIssues = foundIssues; if (roadworthiness != null) maintenance.Roadworthiness = roadworthiness; if (maintenanceType != null) maintenance.MaintenanceType = maintenanceType; if (mileage != null) maintenance.MileageThreshold = mileage; ctx.Db.Maintenance.MaintenanceId.Update(maintenance); Log.Info($"Maintenance {maintenanceId} updated by {ctx.Sender}"); }
    [SpacetimeDB.Reducer] public static void DeleteMaintenance(ReducerContext ctx, uint maintenanceId) { if (!HasPermission(ctx, ctx.Sender, "maintenance.delete")) throw new Exception("Unauthorized: Missing maintenance.delete permission."); if (!ctx.Db.Maintenance.MaintenanceId.Find(maintenanceId).HasValue) throw new Exception("Maintenance record not found"); ctx.Db.Maintenance.MaintenanceId.Delete(maintenanceId); Log.Info($"Maintenance {maintenanceId} deleted by {ctx.Sender}."); }
    [SpacetimeDB.Reducer] public static void GetBusMaintenanceHistory(ReducerContext ctx, uint busId) { /* This should be a query, not a reducer. Reducers modify state. */ Log.Warn("GetBusMaintenanceHistory called as a reducer - this should be a query."); }

    // ---------- Admin Action Log Reducer ----------
    [SpacetimeDB.Reducer] public static void LogAdminAction(ReducerContext ctx, string userIdString, string action, string details, string timestampStr, string? ipAddress, string? userAgent)
    {
        // Attempt to parse the userIdString back into an Identity
        Identity userId;
        try
        {
             if (!Identity.TryParse(userIdString, out userId))
             {
                 Log.Error($"Invalid Identity format in LogAdminAction: {userIdString}");
                 // Optionally, log with a placeholder identity or skip logging
                 return;
             }
        }
        catch(Exception ex)
        {
             Log.Error($"Error parsing Identity in LogAdminAction: {ex.Message}");
             return; // Exit if parsing fails
        }

        uint logId = GetNextId(ctx, "logId");
        ulong timestamp;
        try { timestamp = (ulong)DateTimeOffset.Parse(timestampStr).ToUnixTimeMilliseconds(); }
        catch { timestamp = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000; } // Fallback

        var logEntry = new AdminActionLog { LogId = logId, UserId = userId, Action = action, Details = details, Timestamp = timestamp, IpAddress = ipAddress, UserAgent = userAgent };
        ctx.Db.admin_action_log.Insert(logEntry);
        Log.Info($"Logged Action: {logEntry.Action} by user {logEntry.UserId}");
    }

    // ---------- Debug Reducer ----------
    [SpacetimeDB.Reducer] public static void DebugVerifyPassword(ReducerContext ctx, string password, string storedHash) { bool isValid = VerifyPassword(password, storedHash); string newHash = HashPassword(password); Log.Info($"Debug VerifyPassword: Pwd='{password}', Stored='{storedHash}', Valid={isValid}, NewHash='{newHash}', NewHashValid={VerifyPassword(password, newHash)}"); }

    // ---------- New Auth Reducers ----------
    [SpacetimeDB.Reducer]
    public static void CreateUserSettings(ReducerContext ctx, Identity userId)
    {
        // Check if settings already exist
        if (ctx.Db.UserSettings.Iter().Any(us => us.UserId == userId))
        {
            Log.Info($"UserSettings already exist for user {userId}");
            return;
        }
        uint settingId = GetNextId(ctx, "userSettingsId");
        var userSettings = new UserSettings { UserSettingId = settingId, UserId = userId, TotpEnabled = false, WebAuthnEnabled = false, IsEmailNotificationsEnabled = true, IsSmsNotificationsEnabled = false, IsPushNotificationsEnabled = false, IsWhatsAppNotificationsEnabled = false, IsTelegramNotificationsEnabled = false, IsDiscordNotificationsEnabled = false };
        ctx.Db.UserSettings.Insert(userSettings);
        Log.Info($"Created UserSettings for user {userId}");
    }

    [SpacetimeDB.Reducer]
    public static void StoreTotpSecret(ReducerContext ctx, Identity userId, string secret)
    {
        // Deactivate existing secrets for the user first
        var existingSecrets = ctx.Db.TotpSecret.Iter().Where(ts => ts.UserId == userId && ts.IsActive).ToList();
        foreach(var existing in existingSecrets)
        {
            existing.IsActive = false;
            ctx.Db.TotpSecret.Id.Update(existing);
        }

        uint secretId = GetNextId(ctx, "totpSecretId");
        var totpSecret = new TotpSecret { Id = secretId, UserId = userId, Secret = secret, CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000, IsActive = true };
        ctx.Db.TotpSecret.Insert(totpSecret);
        Log.Info($"Stored new active TOTP secret for user {userId}");
    }

    [SpacetimeDB.Reducer]
    public static void EnableTotp(ReducerContext ctx, Identity userId)
    {
        var userSettings = ctx.Db.UserSettings.Iter().FirstOrDefault(us => us.UserId == userId);
        if (userSettings == null) throw new Exception("User settings not found.");
        // Ensure there's an active secret before enabling
        if (!ctx.Db.TotpSecret.Iter().Any(ts => ts.UserId == userId && ts.IsActive)) throw new Exception("No active TOTP secret found to enable.");
        userSettings.TotpEnabled = true;
        ctx.Db.UserSettings.UserSettingId.Update(userSettings);
        Log.Info($"TOTP enabled for user {userId}");
    }

    [SpacetimeDB.Reducer]
    public static void DisableTotp(ReducerContext ctx, Identity userId)
    {
        var userSettings = ctx.Db.UserSettings.Iter().FirstOrDefault(us => us.UserId == userId);
        if (userSettings == null) throw new Exception("User settings not found.");
        userSettings.TotpEnabled = false;
        ctx.Db.UserSettings.UserSettingId.Update(userSettings);
        // Deactivate secrets as well
        DeactivateTotpSecret(ctx, userId);
        Log.Info($"TOTP disabled for user {userId}");
    }

    [SpacetimeDB.Reducer]
    public static void DeactivateTotpSecret(ReducerContext ctx, Identity userId)
    {
        var secrets = ctx.Db.TotpSecret.Iter().Where(ts => ts.UserId == userId && ts.IsActive).ToList();
        foreach(var secret in secrets)
        {
            secret.IsActive = false;
            ctx.Db.TotpSecret.Id.Update(secret);
        }
        Log.Info($"Deactivated TOTP secrets for user {userId}");
    }


    [SpacetimeDB.Reducer]
    public static void RegisterWebAuthnCredential(ReducerContext ctx, Identity userId, byte[] credentialId, byte[] publicKey, uint counter, byte[]? attestationObject, byte[]? clientDataJson, string? deviceName)
    {
        // Check if credential ID already exists (should be globally unique)
        if (ctx.Db.WebAuthnCredential.Iter().Any(wc => wc.CredentialId.SequenceEqual(credentialId)))
        {
            throw new Exception("WebAuthn credential ID already exists.");
        }

        uint id = GetNextId(ctx, "webAuthnCredentialId");
        var webAuthnCredential = new WebAuthnCredential
        {
            Id = id, UserId = userId, CredentialId = credentialId, PublicKey = publicKey, Counter = counter,
            AttestationObject = attestationObject, ClientDataJson = clientDataJson, // Store raw data
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000, IsActive = true, DeviceName = deviceName
        };
        ctx.Db.WebAuthnCredential.Insert(webAuthnCredential);
        Log.Info($"Registered WebAuthn credential for user {userId}");
    }

    [SpacetimeDB.Reducer]
    public static void EnableWebAuthn(ReducerContext ctx, Identity userId)
    {
        var userSettings = ctx.Db.UserSettings.Iter().FirstOrDefault(us => us.UserId == userId);
        if (userSettings == null) throw new Exception("User settings not found.");
        // Only enable if there's at least one active credential
        if (!ctx.Db.WebAuthnCredential.Iter().Any(wc => wc.UserId == userId && wc.IsActive)) throw new Exception("No active WebAuthn credentials found to enable.");
        userSettings.WebAuthnEnabled = true;
        ctx.Db.UserSettings.UserSettingId.Update(userSettings);
        Log.Info($"WebAuthn enabled for user {userId}");
    }

    [SpacetimeDB.Reducer]
    public static void DisableWebAuthn(ReducerContext ctx, Identity userId)
    {
        var userSettings = ctx.Db.UserSettings.Iter().FirstOrDefault(us => us.UserId == userId);
        if (userSettings == null) throw new Exception("User settings not found.");
        userSettings.WebAuthnEnabled = false;
        ctx.Db.UserSettings.UserSettingId.Update(userSettings);
        Log.Info($"WebAuthn disabled for user {userId}");
        // Note: Credentials are kept but marked inactive or deleted separately if needed.
    }

    [SpacetimeDB.Reducer]
    public static void DeactivateWebAuthnCredential(ReducerContext ctx, uint id)
    {
        var credential = ctx.Db.WebAuthnCredential.Id.Find(id);
        if (credential == null) throw new Exception("WebAuthn credential not found");
        credential.IsActive = false;
        ctx.Db.WebAuthnCredential.Id.Update(credential);
        Log.Info($"Deactivated WebAuthn credential {id}");

        // Check if disabling WebAuthn is necessary
        var userId = credential.UserId;
        if (!ctx.Db.WebAuthnCredential.Iter().Any(wc => wc.UserId == userId && wc.IsActive))
        {
            DisableWebAuthn(ctx, userId);
        }
    }

    [SpacetimeDB.Reducer]
    public static void UpdateWebAuthnCredentialCounter(ReducerContext ctx, uint id, uint counter)
    {
        var credential = ctx.Db.WebAuthnCredential.Id.Find(id);
        if (credential == null) throw new Exception("WebAuthn credential not found");
        credential.Counter = counter;
        ctx.Db.WebAuthnCredential.Id.Update(credential);
        Log.Info($"Updated WebAuthn counter for credential {id}");
    }

     [SpacetimeDB.Reducer]
    public static void StoreWebAuthnChallenge(ReducerContext ctx, Identity userId, byte[] challenge, ulong expiryDate) // Use ulong
    {
        uint id = GetNextId(ctx, "webAuthnChallengeId");
        var webAuthnChallenge = new WebAuthnChallenge
        {
            Id = id,
            UserId = userId,
            Challenge = challenge,
            ExpiryDate = expiryDate // Store as ulong
        };
        ctx.Db.WebAuthnChallenge.Insert(webAuthnChallenge);
        Log.Info($"Stored WebAuthn challenge for user {userId}");
    }

    [SpacetimeDB.Reducer]
    public static void DeleteWebAuthnChallenge(ReducerContext ctx, uint id)
    {
        if (ctx.Db.WebAuthnChallenge.Id.Find(id).HasValue)
        {
            ctx.Db.WebAuthnChallenge.Id.Delete(id);
            Log.Info($"Deleted WebAuthn challenge {id}");
        }
        else
        {
             Log.Warn($"Attempted to delete non-existent WebAuthn challenge {id}");
        }
    }

    [SpacetimeDB.Reducer]
    public static void CreateMagicLinkToken(ReducerContext ctx, Identity userId, string token, ulong expiresAt, string? deviceInfo, string? ipAddress)
    {
        if (ctx.Db.MagicLinkToken.Token.Find(token).HasValue) throw new Exception("Magic link token already exists.");
        var magicLinkToken = new MagicLinkToken
        {
            Token = token, UserId = userId, ExpiresAt = expiresAt, IsUsed = false, DeviceInfo = deviceInfo, IpAddress = ipAddress,
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000
        };
        ctx.Db.MagicLinkToken.Insert(magicLinkToken);
        Log.Info($"Created magic link token for user {userId}");
    }

    [SpacetimeDB.Reducer]
    public static void UseMagicLinkToken(ReducerContext ctx, string token)
    {
        var magicLinkToken = ctx.Db.MagicLinkToken.Token.Find(token);
        if (magicLinkToken == null) throw new Exception("Magic link token not found.");
        if (magicLinkToken.IsUsed) throw new Exception("Magic link token already used.");
        if (magicLinkToken.ExpiresAt < (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000) throw new Exception("Magic link token expired.");
        magicLinkToken.IsUsed = true;
        ctx.Db.MagicLinkToken.Token.Update(magicLinkToken);
        Log.Info($"Used magic link token {token}");
        // Optionally update user's LastLoginAt here
        var user = ctx.Db.UserProfile.UserId.Find(magicLinkToken.UserId);
        if (user != null)
        {
            user.LastLoginAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
            ctx.Db.UserProfile.UserId.Update(user);
        }
    }

    [SpacetimeDB.Reducer]
    public static void ApproveQrSession(ReducerContext ctx, string sessionId)
    {
        Log.Info($"Approving QR session {sessionId} by {ctx.Sender}");
        if (string.IsNullOrEmpty(sessionId)) { Log.Error("Session ID cannot be empty"); return; }
        var session = ctx.Db.QRSession.SessionId.Find(sessionId);
        if (session == null) { Log.Error($"QR session {sessionId} not found"); return; }
        if (session.ExpiryTime < (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000) { Log.Error($"QR session {sessionId} has expired"); return; }
        if (session.IsUsed) { Log.Error($"QR session {sessionId} has already been used"); return; }
        // Add authorization check if needed: e.g., only the user associated with the session can approve it.
        // if (session.UserId != ctx.Sender) { Log.Error($"Unauthorized attempt to approve QR session {sessionId} by {ctx.Sender}"); return; }
        session.IsUsed = true;
        ctx.Db.QRSession.SessionId.Update(session);
        Log.Info($"QR session {sessionId} approved.");
    }

    // ---------- OpenID Connect Reducers ----------
    [SpacetimeDB.Reducer]
    public static void RegisterOpenIdClient(ReducerContext ctx, string clientId, string clientSecret, string displayName, string[] redirectUris, string[] postLogoutRedirectUris, string[] allowedScopes, string consentType, string clientType)
    {
        // Add permission check if needed: e.g., only admins can register clients
        // if (!HasPermission(ctx, ctx.Sender, "openid.clients.create")) throw new Exception("Unauthorized");

        if (ctx.Db.OpenIdConnect.ClientId.Find(clientId).HasValue) throw new Exception("Client ID already exists.");

        // Hash the client secret before storing
        string hashedSecret = HashPassword(clientSecret); // Use the same hashing mechanism

        var openIdClient = new OpenIdConnect
        {
            ClientId = clientId, ClientSecret = hashedSecret, DisplayName = displayName, RedirectUris = redirectUris, PostLogoutRedirectUris = postLogoutRedirectUris, AllowedScopes = allowedScopes, ConsentType = consentType, ClientType = clientType, RequireConsent = (consentType == "explicit"),
            IsActive = true, CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000, CreatedBy = ctx.Sender.ToString()
        };
        ctx.Db.OpenIdConnect.Insert(openIdClient);
        Log.Info($"Registered OpenID Connect client: {clientId} by {ctx.Sender}");
    }

    [SpacetimeDB.Reducer]
    public static void UpdateOpenIdClient(ReducerContext ctx, string clientId, string? clientSecret, string? displayName, string[]? redirectUris, string[]? postLogoutRedirectUris, string[]? allowedScopes, string? consentType)
    {
        // Add permission check if needed: e.g., only admins can update clients
        // if (!HasPermission(ctx, ctx.Sender, "openid.clients.edit")) throw new Exception("Unauthorized");

        var client = ctx.Db.OpenIdConnect.ClientId.Find(clientId);
        if (client == null) throw new Exception("OpenID Connect client not found.");

        if (clientSecret != null) client.ClientSecret = HashPassword(clientSecret); // Re-hash if secret changes
        if (displayName != null) client.DisplayName = displayName;
        if (redirectUris != null) client.RedirectUris = redirectUris;
        if (postLogoutRedirectUris != null) client.PostLogoutRedirectUris = postLogoutRedirectUris;
        if (allowedScopes != null) client.AllowedScopes = allowedScopes;
        if (consentType != null) { client.ConsentType = consentType; client.RequireConsent = (consentType == "explicit"); }

        client.UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        client.UpdatedBy = ctx.Sender.ToString();
        ctx.Db.OpenIdConnect.ClientId.Update(client);
        Log.Info($"Updated OpenID Connect client: {clientId} by {ctx.Sender}");
    }

    [SpacetimeDB.Reducer]
    public static void RevokeOpenIdClient(ReducerContext ctx, string clientId)
    {
        // Add permission check if needed: e.g., only admins can revoke clients
        // if (!HasPermission(ctx, ctx.Sender, "openid.clients.delete")) throw new Exception("Unauthorized");

        var client = ctx.Db.OpenIdConnect.ClientId.Find(clientId);
        if (client == null) throw new Exception("OpenID Connect client not found.");
        client.IsActive = false; // Soft delete
        client.UpdatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000;
        client.UpdatedBy = ctx.Sender.ToString();
        ctx.Db.OpenIdConnect.ClientId.Update(client);
        Log.Info($"Revoked OpenID Connect client: {clientId} by {ctx.Sender}");

        // Optionally revoke associated grants
        var grants = ctx.Db.OpenIdConnectGrant.Iter().Where(g => g.ClientId == clientId && !g.IsRevoked).ToList();
        foreach(var grant in grants)
        {
            grant.IsRevoked = true;
            ctx.Db.OpenIdConnectGrant.GrantId.Update(grant);
        }
        Log.Info($"Revoked {grants.Count} grants associated with client {clientId}");
    }

    [SpacetimeDB.Reducer]
    public static void CreateOpenIdGrant(ReducerContext ctx, string clientId, Identity userId, string type, string[] scopes, ulong expiresAt, string code, string refreshToken)
    {
        uint grantId = GetNextId(ctx, "openIdConnectGrantId");
        var openIdConnectGrant = new OpenIdConnectGrant
        {
            GrantId = grantId, ClientId = clientId, UserId = userId, Type = type, Scopes = scopes,
            CreatedAt = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch / 1000, ExpiresAt = expiresAt, IsRevoked = false, Code = code, RefreshToken = refreshToken
        };
        ctx.Db.OpenIdConnectGrant.Insert(openIdConnectGrant);
        Log.Info($"Created OpenID Connect grant {grantId} for client {clientId}, user {userId}");
    }

    [SpacetimeDB.Reducer]
    public static void RevokeOpenIdGrant(ReducerContext ctx, uint grantId) // Use uint GrantId
    {
        var grant = ctx.Db.OpenIdConnectGrant.GrantId.Find(grantId);
        if (grant != null)
        {
            grant.IsRevoked = true;
            ctx.Db.OpenIdConnectGrant.GrantId.Update(grant);
            Log.Info($"Revoked OpenID Connect grant {grantId}");
        } else {
            Log.Warn($"Attempted to revoke non-existent OpenID Connect grant {grantId}");
        }
    }
} // End of Module class
```

```csharp
// --- START OF FILE BaseController.cs ---

using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Serilog;
using SpacetimeDB; // Added for Identity

namespace TicketSalesApp.AdminServer.Controllers
{
    public abstract class BaseController : ControllerBase
    {
        protected bool IsAdmin()
        {
            try
            {
                var authHeader = Request.Headers["Authorization"].ToString();
                if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                {
                    Log.Warning("[IsAdmin Check] Missing or invalid Authorization header");
                    return false;
                }

                var token = authHeader.Substring("Bearer ".Length);
                var tokenHandler = new JwtSecurityTokenHandler();
                if (!tokenHandler.CanReadToken(token))
                {
                    Log.Warning("[IsAdmin Check] Cannot read JWT token");
                    return false;
                }
                var jwtToken = tokenHandler.ReadJwtToken(token);

                // Check primary role claim (legacy ID '1')
                var primaryRoleClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "primary_role");
                if (primaryRoleClaim?.Value == "1")
                {
                    Log.Debug("[IsAdmin Check] User identified as admin via primary_role claim");
                    return true;
                }

                // Fallback to checking all role claims (name 'Administrator' or legacy ID '1')
                var roleClaims = jwtToken.Claims.Where(c => c.Type == ClaimTypes.Role || c.Type == "role");
                if (roleClaims.Any(c => c.Value == "Administrator" || c.Value == "1"))
                {
                    Log.Debug("[IsAdmin Check] User identified as admin via role claim");
                    return true;
                }

                Log.Debug("[IsAdmin Check] User is not an admin");
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[IsAdmin Check] Error checking admin status");
                return false;
            }
        }

        protected bool HasPermission(string permissionName)
        {
            try
            {
                if (string.IsNullOrEmpty(permissionName))
                {
                    Log.Warning("[HasPermission Check] Permission name cannot be empty");
                    return false;
                }

                var authHeader = Request.Headers["Authorization"].ToString();
                if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                {
                    Log.Warning("[HasPermission Check] Missing or invalid Authorization header");
                    return false;
                }

                var token = authHeader.Substring("Bearer ".Length);
                var tokenHandler = new JwtSecurityTokenHandler();
                 if (!tokenHandler.CanReadToken(token))
                {
                    Log.Warning("[HasPermission Check] Cannot read JWT token");
                    return false;
                }
                var jwtToken = tokenHandler.ReadJwtToken(token);

                var permissionClaims = jwtToken.Claims.Where(c => c.Type == "permission");
                bool hasPerm = permissionClaims.Any(c => string.Equals(c.Value, permissionName, StringComparison.OrdinalIgnoreCase));

                Log.Debug("[HasPermission Check] Checking for '{Permission}': {Result}", permissionName, hasPerm);
                return hasPerm;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[HasPermission Check] Error checking permission: {Permission}", permissionName);
                return false;
            }
        }

        protected string? GetUserId() // Returns Legacy User ID as string
        {
            try
            {
                var userIdClaim = User.FindFirst("sub")?.Value; // "sub" claim typically holds the LegacyUserId
                if (string.IsNullOrEmpty(userIdClaim))
                {
                    Log.Warning("[GetUserId] 'sub' claim not found in token");
                    return null;
                }
                return userIdClaim;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[GetUserId] Error getting Legacy User ID (sub) from token");
                return null;
            }
        }

        protected Identity? GetSpacetimeIdentity()
        {
            try
            {
                var identityClaim = User.FindFirst("identity")?.Value;
                if (string.IsNullOrEmpty(identityClaim))
                {
                    Log.Warning("[GetSpacetimeIdentity] 'identity' claim not found in token");
                    return null;
                }

                if (Identity.TryParse(identityClaim, out var identity))
                {
                    return identity;
                }
                else
                {
                    Log.Warning("[GetSpacetimeIdentity] Failed to parse 'identity' claim value: {ClaimValue}", identityClaim);
                    return null;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[GetSpacetimeIdentity] Error getting SpacetimeDB Identity from token");
                return null;
            }
        }

        protected string? GetXuid()
        {
            try
            {
                 var xuidClaim = User.FindFirst("xuid")?.Value;
                 if (string.IsNullOrEmpty(xuidClaim))
                 {
                     Log.Debug("[GetXuid] 'xuid' claim not found in token"); // Debug level as it might be optional
                     return null;
                 }
                 return xuidClaim;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[GetXuid] Error getting XUID from token");
                return null;
            }
        }
    }
}
```

```csharp
// --- START OF FILE IAdminActionLogger.cs ---

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB; // Added for Identity
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IAdminActionLogger
    {
        // Use Identity type for userId
        Task LogActionAsync(Identity userId, string action, string details);
        Task<List<AdminActionLog>> GetUserActionsAsync(Identity userId, DateTime? startDate = null, DateTime? endDate = null);
        Task<List<AdminActionLog>> GetActionsByTypeAsync(string actionType, DateTime? startDate = null, DateTime? endDate = null);
    }
}
```

```csharp
// --- START OF FILE IAuthenticationService.cs ---

using System.Threading.Tasks;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IAuthenticationService
    {
        Task<UserProfile?> AuthenticateAsync(string login, string password);
        Task<bool> RegisterAsync(string login, string password, int role, string? email = null, string? phoneNumber = null);
        Task<UserProfile?> AuthenticateDirectQRAsync(string login, string validationToken);
        int GetUserRole(Identity userId); // Use Identity type
    }
}
```

```csharp
// --- START OF FILE IBusService.cs ---

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IBusService
    {
        Task<IEnumerable<Bus>> GetAllBusesAsync();
        Task<Bus?> GetBusByIdAsync(uint busId);
        Task<Bus?> CreateBusAsync(string model, string? registrationNumber = null); // Return created Bus
        Task<bool> UpdateBusAsync(uint busId, string? model = null, string? registrationNumber = null);
        Task<bool> DeleteBusAsync(uint busId);
        Task<IEnumerable<Bus>> SearchBusesAsync(string? model = null, string? serviceStatus = null);
        Task<bool> ActivateBusAsync(uint busId);
        Task<bool> DeactivateBusAsync(uint busId);
    }
}
```

```csharp
// --- START OF FILE IDataService.cs ---

using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB.Types;
using SpacetimeDB; // Added for Identity

namespace TicketSalesApp.Services.Interfaces
{
    public interface IDataService
    {
        // Use appropriate ID type based on entity
        Task<T?> GetAsync<T>(uint id) where T : class; // For entities with uint ID
        Task<T?> GetByIdentityAsync<T>(Identity id) where T : class; // For entities with Identity PK (e.g., UserProfile)
        Task<List<T>> GetAllAsync<T>() where T : class;
        Task<T?> AddAsync<T>(T entity) where T : class;
        Task<T?> UpdateAsync<T>(T entity) where T : class;
        Task<bool> DeleteAsync<T>(uint id) where T : class; // For entities with uint ID
        Task<bool> DeleteByIdentityAsync<T>(Identity id) where T : class; // For entities with Identity PK
    }
}
```

```csharp
// --- START OF FILE IEmailService.cs ---

using System.Threading.Tasks;

namespace TicketSalesApp.Services.Interfaces
{
    /// <summary>
    /// Service for sending emails
    /// </summary>
    public interface IEmailService
    {
        Task SendEmailAsync(string to, string subject, string body);
    }
}
```

```csharp
// --- START OF FILE IEmployeeService.cs ---

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IEmployeeService
    {
        Task<List<Employee>> GetAllEmployeesAsync();
        Task<Employee?> GetEmployeeByIdAsync(uint employeeId);
        Task<List<Employee>> GetEmployeesByJobIdAsync(uint jobId);
        Task<bool> CreateEmployeeAsync(string employeeName, string employeeSurname, string? employeePatronym, uint jobId); // Made patronym optional
        Task<bool> UpdateEmployeeAsync(uint employeeId, string? employeeName = null, string? employeeSurname = null, string? employeePatronym = null, uint? jobId = null);
        Task<bool> DeleteEmployeeAsync(uint employeeId);
        Task<List<Job>> GetAllJobsAsync();
        Task<Job?> GetJobByIdAsync(uint jobId);
        Task<bool> CreateJobAsync(string jobTitle, string? jobInternship); // Made internship optional
        Task<bool> UpdateJobAsync(uint jobId, string? jobTitle = null, string? jobInternship = null);
        Task<bool> DeleteJobAsync(uint jobId);
    }
}
```

```csharp
// --- START OF FILE IExportService.cs ---

using System.Collections.Generic;
using System.Threading.Tasks;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IExportService
    {
        Task<byte[]> ExportToExcelAsync<T>(IEnumerable<T> data, string sheetName);
        Task<byte[]> ExportToPdfAsync<T>(IEnumerable<T> data, string title);
        Task<byte[]> ExportToCsvAsync<T>(IEnumerable<T> data);
        Task<string> ExportToJsonAsync<T>(IEnumerable<T> data);
    }
}
```

```csharp
// --- START OF FILE IMagicLinkService.cs ---

using System.Threading.Tasks;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    /// <summary>
    /// Service for handling Magic Link authentication operations
    /// </summary>
    public interface IMagicLinkService
    {
        Task<(bool success, string? errorMessage)> SendMagicLinkAsync(string email, string? userAgent, string? ipAddress);
        Task<(bool success, UserProfile? user, string? errorMessage)> ValidateMagicLinkAsync(string token);
        Task<bool> MarkMagicLinkAsUsedAsync(string token);
    }
}
```

```csharp
// --- START OF FILE IMaintenanceService.cs ---

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IMaintenanceService
    {
        Task<List<Maintenance>> GetAllMaintenanceRecordsAsync();
        Task<Maintenance?> GetMaintenanceByIdAsync(uint maintenanceId);
        Task<List<Maintenance>> GetMaintenanceByBusIdAsync(uint busId);
        Task<bool> CreateMaintenanceAsync(uint busId, ulong lastServiceDate, string serviceEngineer, string foundIssues, ulong nextServiceDate, string roadworthiness, string? maintenanceType); // Type optional
        Task<bool> UpdateMaintenanceAsync(uint maintenanceId, uint? busId = null, ulong? lastServiceDate = null, string? serviceEngineer = null, string? foundIssues = null, ulong? nextServiceDate = null, string? roadworthiness = null, string? maintenanceType = null, string? mileage = null);
        Task<bool> DeleteMaintenanceAsync(uint maintenanceId);
        Task<List<Maintenance>> GetBusMaintenanceHistoryAsync(uint busId);
    }
}
```

```csharp
// --- START OF FILE IOpenIdConnectService.cs ---

using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using SpacetimeDB.Types;
using OpenIddict.Abstractions; // Added

namespace TicketSalesApp.Services.Interfaces
{
    /// <summary>
    /// Service for handling OpenID Connect operations
    /// </summary>
    public interface IOpenIdConnectService
    {
        Task<(bool success, object? application, string? errorMessage)> GetApplicationByClientIdAsync(string clientId);
        Task<(bool success, List<object>? authorizations, string? errorMessage)> GetAuthorizationsAsync(string subject, object application, string status, string type, IEnumerable<string> scopes);
        Task<(bool success, ClaimsIdentity? identity, string? errorMessage)> CreateIdentityFromUserAsync(UserProfile user, IEnumerable<string> scopes);
        Task<(bool success, object? authorization, string? errorMessage)> CreateAuthorizationAsync(ClaimsIdentity identity, string subject, object application, string type, IEnumerable<string> scopes);
        Task<(bool success, string? id, string? errorMessage)> GetAuthorizationIdAsync(object authorization);
        Task<(bool success, List<string>? resources, string? errorMessage)> GetResourcesAsync(IEnumerable<string> scopes);

        // Client management
        Task<(bool success, string? errorMessage)> RegisterClientApplicationAsync(string clientId, string clientSecret, string displayName, string[] redirectUris, string[] postLogoutRedirectUris, string[] allowedScopes, bool requireConsent);
        Task<(bool success, string? errorMessage)> UpdateClientApplicationAsync(string clientId, string? clientSecret, string? displayName, string[]? redirectUris, string[]? postLogoutRedirectUris, string[]? allowedScopes, bool? requireConsent);
        Task<(bool success, string? errorMessage)> DeleteClientApplicationAsync(string clientId);
        Task<(bool success, List<OpenIddictApplicationDescriptor>? applications, string? errorMessage)> GetAllClientApplicationsAsync(); // Return descriptors
        Task<(bool success, OpenIddictApplicationDescriptor? application, string? errorMessage)> GetClientApplicationAsync(string clientId); // Return descriptor

        // Helper methods (might be internal to implementation but listed for clarity)
        Task<string?> GetClientIdAsync(object application);
        Task<string?> GetDisplayNameAsync(object application);
        Task<IEnumerable<string>> GetRedirectUrisAsync(object application);
        Task<IEnumerable<string>> GetPostLogoutRedirectUrisAsync(object application);
        Task<IEnumerable<string>> GetPermissionsAsync(object application);
        Task<string?> GetConsentTypeAsync(object application);

        // Claim destination logic
        IEnumerable<string> GetDestinations(Claim claim);
    }
}
```

```csharp
// --- START OF FILE IPermissionService.cs ---

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IPermissionService
    {
        Task<IEnumerable<Permission>> GetAllPermissionsAsync();
        Task<Permission?> GetPermissionByIdAsync(uint permissionId);
        Task<IEnumerable<Permission>> GetPermissionsByCategoryAsync(string category);
        Task<IEnumerable<string>> GetAllCategoriesAsync();
        Task<Permission?> CreatePermissionAsync(string name, string description, string category);
        Task<bool> UpdatePermissionAsync(uint permissionId, string? name = null, string? description = null, string? category = null, bool? isActive = null);
        Task<bool> DeletePermissionAsync(uint permissionId);
        Task<bool> IsPermissionInUseAsync(uint permissionId);
    }
}
```

```csharp
// --- START OF FILE IQRAuthenticationService.cs ---

using System;
using System.Threading.Tasks;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IQRAuthenticationService
    {
        Task<(string qrCode, string? rawData)> GenerateQRCodeAsync(Identity userId); // Use Identity
        Task<(bool success, UserProfile? user)> ValidateQRLoginTokenAsync(string token); // Renamed from GenerateQRLoginTokenAsync

        // Direct QR login methods
        Task<(string qrCode, string rawData)> GenerateDirectLoginQRCodeAsync(string username, string deviceType);
        Task<(bool success, UserProfile? user, string deviceId)> ValidateDirectLoginTokenAsync(string token, string deviceType);
        Task<bool> NotifyDeviceLoginSuccessAsync(string deviceId, string token);
    }
}
```

```csharp
// --- START OF FILE IRoleService.cs ---

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB; // Added for Identity
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IRoleService
    {
        Task<IEnumerable<Role>> GetAllRolesAsync();
        Task<Role?> GetRoleByIdAsync(uint roleId);
        Task<Role?> GetRoleByLegacyIdAsync(int legacyRoleId);
        Task<IEnumerable<Permission>> GetRolePermissionsAsync(uint roleId);
        Task<bool> AssignRoleToUserAsync(Identity userId, uint roleId); // Use Identity
        Task<bool> RemoveRoleFromUserAsync(Identity userId, uint roleId); // Use Identity

        // Role management methods
        Task<Role?> CreateRoleAsync(string name, string description, int legacyRoleId, uint priority, List<uint>? permissionIds = null, Identity? createdBy = null); // Use Identity
        Task<bool> UpdateRoleAsync(uint roleId, string? name = null, string? description = null, uint? priority = null, List<uint>? permissionIds = null, Identity? updatedBy = null); // Use Identity
        Task<bool> DeleteRoleAsync(uint roleId);

        // Permissions specific to roles
        Task<bool> AssignPermissionToRoleAsync(uint roleId, uint permissionId);
        Task<bool> RemovePermissionFromRoleAsync(uint roleId, uint permissionId);
        Task<IEnumerable<Role>> GetUserRolesAsync(Identity userId); // Added this method based on UserService usage
    }
}
```

```csharp
// --- START OF FILE IRouteScheduleService.cs ---

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB.Types;
using SpacetimeDB; // Added for Identity

namespace TicketSalesApp.Services.Interfaces
{
    public interface IRouteScheduleService
    {
        Task<List<RouteSchedule>> GetAllSchedulesAsync();
        Task<RouteSchedule?> GetScheduleByIdAsync(uint scheduleId);
        Task<List<RouteSchedule>> GetSchedulesByRouteIdAsync(uint routeId);
        Task<bool> CreateScheduleAsync( // Keep parameters consistent with controller model
            uint routeId,
            string startPoint,
            string endPoint,
            List<string>? routeStops,
            ulong departureTime,
            ulong arrivalTime,
            double price,
            uint availableSeats,
            List<string>? daysOfWeek,
            List<string>? busTypes,
            uint? stopDurationMinutes,
            bool? isRecurring,
            List<string>? estimatedStopTimes,
            List<double>? stopDistances,
            string? notes,
            Identity? updatedBy = null // Use Identity
        );
        Task<bool> UpdateScheduleAsync( // Keep parameters consistent with controller model
            uint scheduleId,
            uint? routeId = null,
            string? startPoint = null,
            string? endPoint = null,
            List<string>? routeStops = null,
            ulong? departureTime = null,
            ulong? arrivalTime = null,
            double? price = null,
            uint? availableSeats = null,
            List<string>? daysOfWeek = null,
            List<string>? busTypes = null,
            uint? stopDurationMinutes = null,
            bool? isRecurring = null,
            List<string>? estimatedStopTimes = null,
            List<double>? stopDistances = null,
            string? notes = null,
            Identity? updatedBy = null // Use Identity
        );
        Task<bool> DeleteScheduleAsync(uint scheduleId);
        Task<List<RouteSchedule>> GetSchedulesByDateAsync(ulong date);
        Task<List<RouteSchedule>> GetSchedulesByDateRangeAsync(ulong startDate, ulong endDate);
    }
}
```

```csharp
// --- START OF FILE IRouteService.cs ---

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IRouteService
    {
        Task<List<Route>> GetAllRoutesAsync();
        Task<Route?> GetRouteByIdAsync(uint routeId);
        Task<List<Route>> GetRoutesByBusIdAsync(uint busId);
        Task<List<Route>> GetRoutesByDriverIdAsync(uint driverId);
        Task<bool> CreateRouteAsync(string startPoint, string endPoint, uint driverId, uint busId, string? travelTime, bool isActive); // Made travelTime nullable
        Task<bool> UpdateRouteAsync(uint routeId, string? startPoint = null, string? endPoint = null, uint? driverId = null, uint? busId = null, string? travelTime = null, bool? isActive = null);
        Task<bool> DeleteRouteAsync(uint routeId);
        Task<bool> ActivateRouteAsync(uint routeId);
        Task<bool> DeactivateRouteAsync(uint routeId);
    }
}
```

```csharp
// --- START OF FILE ISpacetimeDBService.cs ---

using SpacetimeDB;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Threading; // Added for CancellationToken
using System.Threading.Tasks;

namespace TicketSalesApp.Services.Interfaces
{
    public interface ISpacetimeDBService
    {
        DbConnection Connect();
        DbConnection GetConnection();
        Identity? GetLocalIdentity();
        void Disconnect();
        void EnqueueCommand(string command, Dictionary<string, object> args);
        void StartMessageProcessing(CancellationToken cancellationToken = default); // Accept cancellation token
        void StopMessageProcessing();
        void ProcessFrameTick();
        void SubscribeToAllTables();
        SubscriptionHandle SubscribeToQueries(string[] queries);
    }
}
```

```csharp
// --- START OF FILE ITicketSalesService.cs ---

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TicketSalesApp.Services.Interfaces
{
    public class SalesReport
    {
        public DateTime Period { get; set; }
        public decimal TotalIncome { get; set; }
        public int TotalTicketsSold { get; set; }
        public decimal AverageTicketPrice { get; set; }
        public List<RoutePerformance> TopRoutes { get; set; } = new List<RoutePerformance>(); // Initialize lists
        public List<TransportUtilization> TransportStats { get; set; } = new List<TransportUtilization>(); // Initialize lists
    }

    public class RoutePerformance
    {
        public string RouteName { get; set; } = string.Empty; // Initialize strings
        public string StartPoint { get; set; } = string.Empty; // Initialize strings
        public string EndPoint { get; set; } = string.Empty; // Initialize strings
        public int TicketsSold { get; set; }
        public decimal TotalIncome { get; set; }
        public decimal OccupancyRate { get; set; }
    }

    public class TransportUtilization
    {
        public string TransportModel { get; set; } = string.Empty; // Initialize strings
        public int TotalRoutes { get; set; }
        public int TicketsSold { get; set; }
        public decimal TotalIncome { get; set; }
        public double UtilizationRate { get; set; }
    }

    public class TransportStatistic
    {
        public string TransportModel { get; set; } = string.Empty; // Initialize strings
        public int TicketsSold { get; set; }
    }

    public interface ITicketSalesService
    {
        Task<decimal> GetTotalIncomeAsync(int year, int month);
        Task<List<TransportStatistic>> GetTopTransportsAsync(int year, int month);
        Task<SalesReport> GetMonthlyReportAsync(int year, int month);
        Task<List<SalesReport>> GetYearlyReportAsync(int year);
        Task<List<RoutePerformance>> GetRoutePerformanceAsync(DateTime startDate, DateTime endDate);
        Task<List<TransportUtilization>> GetTransportUtilizationAsync(DateTime startDate, DateTime endDate);
        Task<byte[]> ExportToExcelAsync(DateTime startDate, DateTime endDate);
        Task<byte[]> ExportToPdfAsync(DateTime startDate, DateTime endDate);
        Task<byte[]> ExportToCsvAsync(DateTime startDate, DateTime endDate);
    }
}
```

```csharp
// --- START OF FILE ITicketService.cs ---

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB.Types;
using SpacetimeDB; // Added for Identity

namespace TicketSalesApp.Services.Interfaces
{
    public interface ITicketService
    {
        Task<List<Ticket>> GetAllTicketsAsync();
        Task<Ticket?> GetTicketByIdAsync(uint ticketId);
        Task<List<Ticket>> GetTicketsByRouteIdAsync(uint routeId);
        Task<bool> CreateTicketAsync(uint routeId, uint seatNumber, double ticketPrice, string paymentMethod, ulong purchaseTime, Identity createdBy); // Added createdBy Identity
        Task<bool> UpdateTicketAsync(uint ticketId, uint? routeId = null, double? ticketPrice = null, uint? seatNumber = null, string? paymentMethod = null, bool? isActive = null, Identity? updatedBy = null); // Use Identity
        Task<bool> DeleteTicketAsync(uint ticketId);
        Task<bool> CancelTicketAsync(uint ticketId, Identity cancelledBy); // Use Identity
        Task<bool> CreateSaleAsync(uint ticketId, string buyerName, string buyerPhone, Identity sellerId, string? saleLocation = null, string? saleNotes = null); // Use Identity
        Task<bool> UpdateSaleAsync(uint saleId, uint? ticketId = null, string? buyerName = null, string? buyerPhone = null, string? saleLocation = null, string? saleNotes = null);
        Task<bool> DeleteSaleAsync(uint saleId);
    }
}
```

```csharp
// --- START OF FILE ITotpService.cs ---

using System.Threading.Tasks;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    /// <summary>
    /// Service for handling TOTP (Time-based One-Time Password) operations
    /// </summary>
    public interface ITotpService
    {
        Task<(bool success, string? secretKey, string? qrCodeUri, string? errorMessage)> SetupTotpAsync(Identity userId, string username);
        Task<(bool success, string? errorMessage)> EnableTotpAsync(Identity userId, string verificationCode, string secretKey); // secretKey needed for initial verification
        Task<(bool success, string? errorMessage)> DisableTotpAsync(Identity userId);
        Task<(bool success, string? errorMessage)> ValidateTotpAsync(Identity userId, string code);
        Task<(bool success, string? errorMessage)> ValidateTotpWithTokenAsync(string tempToken, string code);
        Task<bool> IsTotpEnabledAsync(Identity userId);
        Task<string> GenerateTotpSecretKeyAsync(); // Keep public for potential use elsewhere
        string GenerateTotpQrCodeUri(string username, string secretKey); // Keep public
        bool VerifyTotpCode(string secretKey, string code); // Keep public for potential direct use
    }
}
```

```csharp
// --- START OF FILE IUserService.cs ---

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpacetimeDB; // Added for Identity
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    public interface IUserService
    {
        Task<UserProfile?> GetUserByIdAsync(Identity userId); // Use Identity
        Task<UserProfile?> GetUserByLegacyIdAsync(uint legacyUserId); // Added for convenience
        Task<UserProfile?> GetUserByLoginAsync(string login);
        Task<IEnumerable<UserProfile>> GetAllUsersAsync();
        // Changed signature to match controller/model
        Task<bool> UpdateUserAsync(Identity userId, string? login, string? password, int? roleLegacyId, string? email, string? phoneNumber, bool? isActive);
        Task<bool> DeleteUserAsync(Identity userId); // Use Identity
        Task<bool> ChangePasswordAsync(Identity userId, string currentPassword, string newPassword); // Use Identity, added currentPassword
        Task<IEnumerable<Role>> GetUserRolesAsync(Identity userId); // Use Identity
        Task<IEnumerable<Permission>> GetUserPermissionsAsync(Identity userId); // Use Identity
        Task<UserProfile?> GetCurrentUserAsync(string login);
        // Changed signature to match controller/model
        Task<UserProfile?> CreateUserAsync(string login, string password, int roleLegacyId, string? email = null, string? phoneNumber = null);
    }
}
```

```csharp
// --- START OF FILE IWebAuthnService.cs ---

using System.Collections.Generic;
using System.Threading.Tasks;
using Fido2NetLib;
using Fido2NetLib.Objects;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Interfaces
{
    /// <summary>
    /// Service for handling WebAuthn (Web Authentication) operations
    /// </summary>
    public interface IWebAuthnService
    {
        Task<(bool success, CredentialCreateOptions? options, string? errorMessage)> GetCredentialCreateOptionsAsync(Identity userId, string username);
        Task<(bool success, string? errorMessage)> CompleteRegistrationAsync(Identity userId, string username, AuthenticatorAttestationRawResponse attestationResponse);
        Task<(bool success, AssertionOptions? options, string? errorMessage)> GetAssertionOptionsAsync(string username);
        Task<(bool success, UserProfile? user, string? errorMessage)> CompleteAssertionAsync(string username, AuthenticatorAssertionRawResponse assertionResponse);
        Task<(bool success, string? errorMessage)> RemoveCredentialAsync(Identity userId, string credentialIdBase64); // Use Base64 string ID from DTO
        Task<List<WebAuthnCredential>> GetUserCredentialsAsync(Identity userId);
        Task<bool> IsWebAuthnEnabledAsync(Identity userId);

        // Added internal helper method signatures needed by AuthController
        AssertionOptions CreateAssertionOptions(List<byte[]> allowedCredentials);
    }
}
```

```csharp
// --- START OF FILE AdminActionLogger.cs ---

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations
{
    public class AdminActionLogger : IAdminActionLogger
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly ILogger<AdminActionLogger> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AdminActionLogger(
            ISpacetimeDBService spacetimeService,
            ILogger<AdminActionLogger> logger,
            IHttpContextAccessor httpContextAccessor)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        }

        public Task LogActionAsync(Identity userId, string action, string details)
        {
            try
            {
                // UserId is already Identity type
                if (string.IsNullOrEmpty(action))
                    throw new ArgumentNullException(nameof(action));

                var conn = _spacetimeService.GetConnection();
                var httpContext = _httpContextAccessor.HttpContext;

                // Call the LogAdminAction reducer directly
                // Note: The reducer in Lib.cs was updated to take Identity directly.
                // We convert Identity to string for the reducer argument as defined.
                conn.Reducers.LogAdminAction(
                    userId.ToString(), // Pass Identity as string
                    action,
                    details ?? string.Empty,
                    DateTimeOffset.UtcNow.ToString("o"), // ISO 8601 format
                    httpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown",
                    httpContext?.Request.Headers["User-Agent"].ToString() ?? "Unknown"
                );

                _logger.LogInformation(
                    "Admin action logged: {Action} by user {UserId} from {IpAddress}",
                    action, userId, httpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown");

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging admin action for user {UserId}", userId);
                // Decide whether to rethrow or just log based on application needs
                // For now, just log and don't block the calling operation.
                return Task.CompletedTask;
            }
        }

        public async Task<List<AdminActionLog>> GetUserActionsAsync(
            Identity userId,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            try
            {
                // UserId is already Identity type
                var conn = _spacetimeService.GetConnection();

                // Convert dates to Unix timestamps (milliseconds)
                ulong? startTimestamp = startDate.HasValue
                    ? (ulong)new DateTimeOffset(startDate.Value.ToUniversalTime()).ToUnixTimeMilliseconds()
                    : null;

                ulong? endTimestamp = endDate.HasValue
                    ? (ulong)new DateTimeOffset(endDate.Value.ToUniversalTime()).ToUnixTimeMilliseconds()
                    : null;

                // Query logs, comparing Identity objects directly
                var logs = conn.Db.AdminActionLog.Iter()
                    .Where(l => l.UserId.Equals(userId)) // Use Equals for Identity comparison
                    .ToList(); // Materialize before further filtering on timestamp

                if (startTimestamp.HasValue)
                    logs = logs.Where(l => l.Timestamp >= startTimestamp.Value).ToList();

                if (endTimestamp.HasValue)
                    logs = logs.Where(l => l.Timestamp <= endTimestamp.Value).ToList();

                // Return logs ordered by timestamp descending
                return logs.OrderByDescending(l => l.Timestamp).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user actions for user {UserId}", userId);
                return new List<AdminActionLog>(); // Return empty list on error
            }
        }

        public async Task<List<AdminActionLog>> GetActionsByTypeAsync(
            string actionType,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            try
            {
                if (string.IsNullOrEmpty(actionType))
                    throw new ArgumentNullException(nameof(actionType));

                var conn = _spacetimeService.GetConnection();

                // Convert dates to Unix timestamps (milliseconds)
                 ulong? startTimestamp = startDate.HasValue
                    ? (ulong)new DateTimeOffset(startDate.Value.ToUniversalTime()).ToUnixTimeMilliseconds()
                    : null;

                ulong? endTimestamp = endDate.HasValue
                    ? (ulong)new DateTimeOffset(endDate.Value.ToUniversalTime()).ToUnixTimeMilliseconds()
                    : null;


                // Query logs
                var logs = conn.Db.AdminActionLog.Iter()
                    .Where(l => l.Action == actionType)
                    .ToList(); // Materialize before filtering

                if (startTimestamp.HasValue)
                    logs = logs.Where(l => l.Timestamp >= startTimestamp.Value).ToList();

                if (endTimestamp.HasValue)
                    logs = logs.Where(l => l.Timestamp <= endTimestamp.Value).ToList();

                // Return logs ordered by timestamp descending
                return logs.OrderByDescending(l => l.Timestamp).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving actions by type {ActionType}", actionType);
                return new List<AdminActionLog>(); // Return empty list on error
            }
        }
    }
}
```

```csharp
// --- START OF FILE AuthenticationService.cs ---

using Microsoft.Extensions.Logging;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;
using SpacetimeDB;
using SpacetimeDB.Types;
using System.Linq;

namespace TicketSalesApp.Services.Implementations
{
    public class AuthenticationService : IAuthenticationService
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly IRoleService _roleService; // Keep RoleService for role lookups if needed
        private readonly ILogger<AuthenticationService> _logger;

        public AuthenticationService(
            ISpacetimeDBService spacetimeService,
            IRoleService roleService,
            ILogger<AuthenticationService> logger)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _roleService = roleService ?? throw new ArgumentNullException(nameof(roleService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<UserProfile?> AuthenticateAsync(string login, string password)
        {
            try
            {
                _logger.LogInformation("Attempting to authenticate user: {Login}", login);

                if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
                {
                    _logger.LogWarning("Authentication attempt with empty login or password");
                    return null;
                }

                var conn = _spacetimeService.GetConnection();

                // Find user by login
                var userProfile = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.Login == login && u.IsActive);

                if (userProfile == null)
                {
                    _logger.LogWarning("Authentication failed: User not found or inactive for login: {Login}", login);
                    return null;
                }

                // Verify password using SpacetimeDB's VerifyPassword function
                if (Module.VerifyPassword(password, userProfile.PasswordHash))
                {
                    _logger.LogInformation("User {Login} authenticated successfully", login);

                    // Call the AuthenticateUser reducer to update last login time etc.
                    // Note: AuthenticateUser reducer doesn't actually need the password for its logic,
                    // but we pass it as per its current definition.
                    conn.Reducers.AuthenticateUser(login, password); // Password verification already done

                    return userProfile;
                }

                _logger.LogWarning("Authentication failed: Invalid password for user: {Login}", login);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while authenticating user: {Login}", login);
                // Do not rethrow sensitive auth errors
                return null;
            }
        }

        public async Task<bool> RegisterAsync(string login, string password, int roleLegacyId, string? email = null, string? phoneNumber = null)
        {
            try
            {
                _logger.LogInformation("Attempting to register new user with login: {Login} and roleLegacyId: {RoleLegacyId}", login, roleLegacyId);

                if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
                {
                    _logger.LogWarning("Registration attempt with empty login or password");
                    return false;
                }

                var conn = _spacetimeService.GetConnection();

                // Check if user already exists (redundant check, reducer handles it, but good practice)
                var existingUser = conn.Db.UserProfile.Iter().FirstOrDefault(u => u.Login == login);
                if (existingUser != null)
                {
                    _logger.LogWarning("Registration failed: User already exists with login: {Login}", login);
                    return false;
                }

                // Find the RoleId based on the legacyRoleId
                var role = await _roleService.GetRoleByLegacyIdAsync(roleLegacyId);
                uint? roleId = role?.RoleId;

                if (!roleId.HasValue)
                {
                    _logger.LogWarning("Registration failed: Role with legacy ID {LegacyRoleId} not found", roleLegacyId);
                    // Optionally assign default User role if target role not found
                    var defaultRole = await _roleService.GetRoleByLegacyIdAsync(0); // Assuming 0 is User legacy ID
                    roleId = defaultRole?.RoleId;
                    if (!roleId.HasValue) {
                         _logger.LogError("Registration failed: Target role {LegacyRoleId} and default User role not found.", roleLegacyId);
                         return false;
                    }
                    _logger.LogInformation("Assigning default User role instead of legacy role {LegacyRoleId}", roleLegacyId);
                }


                // Call the RegisterUser reducer
                // This reducer now takes RoleId directly
                conn.Reducers.RegisterUser(login, password, email, phoneNumber, roleId, null);

                // Registration success is determined by reducer not throwing an exception.
                // We can't easily confirm insertion immediately without querying again.
                // Let's assume success if no exception.

                _logger.LogInformation("Successfully initiated registration for new user with login: {Login}", login);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while registering user: {Login}", login);
                return false; // Indicate failure on error
            }
        }

        public async Task<UserProfile?> AuthenticateDirectQRAsync(string login, string validationToken)
        {
            try
            {
                _logger.LogInformation("Attempting direct QR authentication for user: {Login}", login);

                if (string.IsNullOrEmpty(login))
                {
                    _logger.LogWarning("Direct QR authentication attempt with empty login");
                    return null;
                }

                var conn = _spacetimeService.GetConnection();

                // Find user by login
                var userProfile = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.Login == login && u.IsActive);

                if (userProfile == null)
                {
                    _logger.LogWarning("Direct QR authentication failed: User not found or inactive for login: {Login}", login);
                    return null;
                }

                // Here, validationToken is likely the sessionId from the QR flow
                // The actual validation happens when the mobile device approves the session.
                // This method just confirms the user exists and updates last login via the reducer.

                // Call AuthenticateUser reducer to update last login (pass empty password for QR)
                conn.Reducers.AuthenticateUser(login, "");

                _logger.LogInformation("User {Login} authenticated successfully via direct QR (pending session use)", login);
                return userProfile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while authenticating user via direct QR: {Login}", login);
                return null;
            }
        }

        public int GetUserRole(Identity userId) // Returns Legacy Role ID
        {
            try
            {
                var conn = _spacetimeService.GetConnection();

                // Find user roles associated with the Identity
                var userRoles = conn.Db.UserRole.Iter()
                    .Where(ur => ur.UserId.Equals(userId))
                    .ToList();

                if (!userRoles.Any())
                    return 0; // Default role (Legacy ID 0 for User) if none found

                // Get the role details and find the one with the highest priority
                var roles = conn.Db.Role.Iter()
                    .Where(r => userRoles.Select(ur => ur.RoleId).Contains(r.RoleId) && r.IsActive)
                    .ToList();

                if (!roles.Any())
                     return 0; // Default role if assigned roles are inactive

                var highestPriorityRole = roles.OrderByDescending(r => r.Priority).FirstOrDefault();

                return highestPriorityRole?.LegacyRoleId ?? 0; // Return LegacyRoleId or default 0
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user role for identity {Identity}", userId);
                return 0; // Default role on error
            }
        }

        // Hashing and verification are now handled within the SpacetimeDB module (Lib.cs)
        // These private methods are removed from the service.
    }
}
```

```csharp
// --- START OF FILE BusService.cs ---

using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations
{
    public class BusService : IBusService
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly ILogger<BusService> _logger;

        public BusService(ISpacetimeDBService spacetimeService, ILogger<BusService> logger)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<IEnumerable<Bus>> GetAllBusesAsync()
        {
            try
            {
                _logger.LogInformation("Fetching all buses");
                var conn = _spacetimeService.GetConnection();

                // Get all buses - SpacetimeDB Iter is synchronous
                var buses = conn.Db.Bus.Iter().ToList();

                _logger.LogDebug("Retrieved {BusCount} buses", buses.Count);
                return Task.FromResult<IEnumerable<Bus>>(buses); // Wrap synchronous result
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all buses");
                throw; // Rethrow to allow higher layers to handle
            }
        }

        public Task<Bus?> GetBusByIdAsync(uint busId)
        {
            try
            {
                _logger.LogInformation("Fetching bus with ID {BusId}", busId);
                var conn = _spacetimeService.GetConnection();

                // Find bus by ID - SpacetimeDB Find is synchronous
                var bus = conn.Db.Bus.BusId.Find(busId);
                if (bus == null)
                {
                    _logger.LogWarning("Bus with ID {BusId} not found", busId);
                    return Task.FromResult<Bus?>(null);
                }

                _logger.LogDebug("Successfully retrieved bus with ID {BusId}", busId);
                return Task.FromResult<Bus?>(bus); // Wrap synchronous result
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching bus with ID {BusId}", busId);
                throw;
            }
        }

        public Task<Bus?> CreateBusAsync(string model, string? registrationNumber = null)
        {
            try
            {
                _logger.LogInformation("Attempting to create new bus with model {Model}", model);
                var conn = _spacetimeService.GetConnection();

                // Call the CreateBus reducer directly (synchronous)
                conn.Reducers.CreateBus(model, registrationNumber);

                // Reducers don't return values directly. We have to query after calling.
                // This might not reflect the *exact* bus created if another is created concurrently,
                // but it's the best we can do without reducer return values or events.
                // Ordering by descending ID assumes higher ID means newer.
                var newBus = conn.Db.Bus.Iter().OrderByDescending(b => b.BusId).FirstOrDefault();

                if (newBus == null)
                {
                    _logger.LogWarning("Failed to retrieve newly created bus (Model: {Model}). Reducer might have failed or data hasn't updated yet.", model);
                    return Task.FromResult<Bus?>(null);
                }

                _logger.LogInformation("Successfully initiated creation of bus with ID {BusId}", newBus.BusId);
                return Task.FromResult<Bus?>(newBus); // Return the potentially newly created bus
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating bus with model {Model}", model);
                throw;
            }
        }

        public Task<bool> UpdateBusAsync(uint busId, string? model = null, string? registrationNumber = null)
        {
            try
            {
                _logger.LogInformation("Attempting to update bus with ID {BusId}", busId);
                var conn = _spacetimeService.GetConnection();

                // Check if bus exists (synchronous)
                var existingBus = conn.Db.Bus.BusId.Find(busId);
                if (existingBus == null)
                {
                    _logger.LogWarning("Bus with ID {BusId} not found for update", busId);
                    return Task.FromResult(false);
                }

                // Call the UpdateBus reducer (synchronous)
                conn.Reducers.UpdateBus(busId, model, registrationNumber);

                _logger.LogInformation("Successfully initiated update for bus with ID {BusId}", busId);
                return Task.FromResult(true); // Assume success if reducer doesn't throw
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating bus with ID {BusId}", busId);
                return Task.FromResult(false); // Return false on error
            }
        }

        public Task<bool> DeleteBusAsync(uint busId)
        {
            try
            {
                _logger.LogInformation("Attempting to delete bus with ID {BusId}", busId);
                var conn = _spacetimeService.GetConnection();

                // Check if bus exists (synchronous)
                var existingBus = conn.Db.Bus.BusId.Find(busId);
                if (existingBus == null)
                {
                    _logger.LogWarning("Bus with ID {BusId} not found for deletion", busId);
                    return Task.FromResult(false);
                }

                // Check if bus is used in any routes (active or inactive) - Reducer handles active check
                var routesUsingBus = conn.Db.Route.Iter().Any(r => r.BusId == busId);
                if (routesUsingBus)
                {
                    _logger.LogWarning("Cannot delete bus with ID {BusId} as it is assigned to routes", busId);
                    // Consider if you want to throw an exception or return false
                     return Task.FromResult(false); // Let the reducer handle the final check
                    // throw new InvalidOperationException($"Cannot delete bus {busId} as it is assigned to routes.");
                }


                // Call the DeleteBus reducer (synchronous)
                conn.Reducers.DeleteBus(busId);

                _logger.LogInformation("Successfully initiated deletion for bus with ID {BusId}", busId);
                return Task.FromResult(true); // Assume success if reducer doesn't throw
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting bus with ID {BusId}: {ErrorMessage}", busId, ex.Message);
                return Task.FromResult(false); // Return false on error
            }
        }

        public Task<IEnumerable<Bus>> SearchBusesAsync(string? model = null, string? serviceStatus = null)
        {
            try
            {
                _logger.LogInformation("Searching buses with model: {Model}, service status: {ServiceStatus}",
                    model ?? "any", serviceStatus ?? "any");

                var conn = _spacetimeService.GetConnection();

                // Start with all buses
                IEnumerable<Bus> busesQuery = conn.Db.Bus.Iter();

                // Filter by model if provided
                if (!string.IsNullOrEmpty(model))
                {
                    _logger.LogDebug("Filtering by model containing: {Model}", model);
                    // Case-insensitive search
                    busesQuery = busesQuery.Where(b => b.Model.Contains(model, StringComparison.OrdinalIgnoreCase));
                }

                // Filter by service status if provided
                if (!string.IsNullOrEmpty(serviceStatus))
                {
                    _logger.LogDebug("Filtering by service status: {ServiceStatus}", serviceStatus);
                    // Get latest maintenance record for each bus and check roadworthiness
                    busesQuery = busesQuery.Where(b => {
                        var latestMaintenance = conn.Db.Maintenance.Iter()
                            .Where(m => m.BusId == b.BusId)
                            .OrderByDescending(m => m.LastServiceDate)
                            .FirstOrDefault();
                        return latestMaintenance != null &&
                               string.Equals(latestMaintenance.Roadworthiness, serviceStatus, StringComparison.OrdinalIgnoreCase);
                    });
                }

                var results = busesQuery.ToList(); // Execute the query
                _logger.LogDebug("Found {ResultCount} buses matching search criteria", results.Count);
                return Task.FromResult<IEnumerable<Bus>>(results); // Wrap synchronous result
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching buses");
                throw;
            }
        }

        public Task<bool> ActivateBusAsync(uint busId)
        {
            try
            {
                _logger.LogInformation("Attempting to activate bus with ID {BusId}", busId);
                var conn = _spacetimeService.GetConnection();

                // Check if bus exists (synchronous)
                var existingBus = conn.Db.Bus.BusId.Find(busId);
                if (existingBus == null)
                {
                    _logger.LogWarning("Bus with ID {BusId} not found for activation", busId);
                    return Task.FromResult(false);
                }

                // Call the ActivateBus reducer (synchronous)
                conn.Reducers.ActivateBus(busId);

                _logger.LogInformation("Successfully initiated activation for bus with ID {BusId}", busId);
                return Task.FromResult(true); // Assume success if reducer doesn't throw
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error activating bus with ID {BusId}", busId);
                return Task.FromResult(false);
            }
        }

        public Task<bool> DeactivateBusAsync(uint busId)
        {
            try
            {
                _logger.LogInformation("Attempting to deactivate bus with ID {BusId}", busId);
                var conn = _spacetimeService.GetConnection();

                // Check if bus exists (synchronous)
                var existingBus = conn.Db.Bus.BusId.Find(busId);
                if (existingBus == null)
                {
                    _logger.LogWarning("Bus with ID {BusId} not found for deactivation", busId);
                    return Task.FromResult(false);
                }

                // Check if bus is used in any active routes (handled by reducer, but good to check here too)
                 var activeRoutes = conn.Db.Route.Iter().Count(r => r.BusId == busId && r.IsActive);
                 if (activeRoutes > 0)
                 {
                     _logger.LogWarning("Cannot deactivate bus with ID {BusId} as it is used in {RouteCount} active routes", busId, activeRoutes);
                     // Let the reducer throw the final error if needed, return false for now
                     return Task.FromResult(false);
                 }


                // Call the DeactivateBus reducer (synchronous)
                conn.Reducers.DeactivateBus(busId);

                _logger.LogInformation("Successfully initiated deactivation for bus with ID {BusId}", busId);
                return Task.FromResult(true); // Assume success if reducer doesn't throw
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deactivating bus with ID {BusId}: {ErrorMessage}", busId, ex.Message);
                return Task.FromResult(false);
            }
        }
    }
}
```

```csharp
// --- START OF FILE DataService.cs ---

using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;

namespace TicketSalesApp.Services.Implementations
{
    public class DataService : IDataService
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly ILogger<DataService> _logger;

        public DataService(ISpacetimeDBService spacetimeService, ILogger<DataService> logger)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // Generic GetAsync using uint ID
        public Task<T?> GetAsync<T>(uint id) where T : class
        {
            try
            {
                var conn = _spacetimeService.GetConnection();
                T? entity = null;

                if (typeof(T) == typeof(Role)) entity = conn.Db.Role.RoleId.Find(id) as T;
                else if (typeof(T) == typeof(Permission)) entity = conn.Db.Permission.PermissionId.Find(id) as T;
                else if (typeof(T) == typeof(Bus)) entity = conn.Db.Bus.BusId.Find(id) as T;
                else if (typeof(T) == typeof(Route)) entity = conn.Db.Route.RouteId.Find(id) as T;
                else if (typeof(T) == typeof(Ticket)) entity = conn.Db.Ticket.TicketId.Find(id) as T;
                else if (typeof(T) == typeof(Sale)) entity = conn.Db.Sale.SaleId.Find(id) as T;
                else if (typeof(T) == typeof(Job)) entity = conn.Db.Job.JobId.Find(id) as T;
                else if (typeof(T) == typeof(Employee)) entity = conn.Db.Employee.EmployeeId.Find(id) as T;
                else if (typeof(T) == typeof(Maintenance)) entity = conn.Db.Maintenance.MaintenanceId.Find(id) as T;
                else if (typeof(T) == typeof(RouteSchedule)) entity = conn.Db.RouteSchedule.ScheduleId.Find(id) as T;
                // UserProfile uses Identity, handled by GetByIdentityAsync or specific service methods
                else
                {
                    _logger.LogWarning("GetAsync<T>(uint id) called with unsupported type or type using Identity PK: {Type}", typeof(T).Name);
                }

                return Task.FromResult(entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting entity of type {Type} with uint ID {Id}", typeof(T).Name, id);
                throw;
            }
        }

         // GetAsync using SpacetimeDB Identity
        public Task<T?> GetByIdentityAsync<T>(Identity id) where T : class
        {
            try
            {
                var conn = _spacetimeService.GetConnection();
                T? entity = null;

                if (typeof(T) == typeof(UserProfile)) entity = conn.Db.UserProfile.UserId.Find(id) as T;
                // Add other types using Identity as PK if they exist
                else
                {
                    _logger.LogWarning("GetByIdentityAsync<T>(Identity id) called with unsupported type: {Type}", typeof(T).Name);
                }

                return Task.FromResult(entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting entity of type {Type} with Identity {Id}", typeof(T).Name, id);
                throw;
            }
        }

        public Task<List<T>> GetAllAsync<T>() where T : class
        {
            try
            {
                var conn = _spacetimeService.GetConnection();
                List<T> entities = new List<T>();

                // Use synchronous Iter() and ToList()
                if (typeof(T) == typeof(UserProfile)) entities = conn.Db.UserProfile.Iter().Cast<T>().ToList();
                else if (typeof(T) == typeof(Role)) entities = conn.Db.Role.Iter().Cast<T>().ToList();
                else if (typeof(T) == typeof(Permission)) entities = conn.Db.Permission.Iter().Cast<T>().ToList();
                else if (typeof(T) == typeof(Bus)) entities = conn.Db.Bus.Iter().Cast<T>().ToList();
                else if (typeof(T) == typeof(Route)) entities = conn.Db.Route.Iter().Cast<T>().ToList();
                else if (typeof(T) == typeof(Ticket)) entities = conn.Db.Ticket.Iter().Cast<T>().ToList();
                else if (typeof(T) == typeof(Sale)) entities = conn.Db.Sale.Iter().Cast<T>().ToList();
                else if (typeof(T) == typeof(Job)) entities = conn.Db.Job.Iter().Cast<T>().ToList();
                else if (typeof(T) == typeof(Employee)) entities = conn.Db.Employee.Iter().Cast<T>().ToList();
                else if (typeof(T) == typeof(Maintenance)) entities = conn.Db.Maintenance.Iter().Cast<T>().ToList();
                else if (typeof(T) == typeof(RouteSchedule)) entities = conn.Db.RouteSchedule.Iter().Cast<T>().ToList();
                // Add other types as needed
                else
                {
                     _logger.LogWarning("GetAllAsync<T>() called with unsupported type: {Type}", typeof(T).Name);
                }

                return Task.FromResult(entities);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all entities of type {Type}", typeof(T).Name);
                throw;
            }
        }

        // AddAsync now focuses on calling specific reducers via dedicated services
        public Task<T?> AddAsync<T>(T entity) where T : class
        {
             _logger.LogWarning("Generic AddAsync<T> is deprecated. Use specific service methods (e.g., IUserService.CreateUserAsync). Entity Type: {Type}", typeof(T).Name);
             // This generic method becomes less useful with specific reducers.
             // It's better to call the specific service methods (e.g., IUserService.CreateUserAsync).
             // Returning null to indicate this method should not be used directly.
             return Task.FromResult<T?>(null);
        }

        // UpdateAsync now focuses on calling specific reducers via dedicated services
        public Task<T?> UpdateAsync<T>(T entity) where T : class
        {
             _logger.LogWarning("Generic UpdateAsync<T> is deprecated. Use specific service methods (e.g., IUserService.UpdateUserAsync). Entity Type: {Type}", typeof(T).Name);
             // Similar to AddAsync, specific service methods are preferred.
             return Task.FromResult<T?>(null);
        }

        // DeleteAsync using uint ID
        public Task<bool> DeleteAsync<T>(uint id) where T : class
        {
             _logger.LogWarning("Generic DeleteAsync<T>(uint id) is deprecated. Use specific service methods (e.g., IRoleService.DeleteRoleAsync). Entity Type: {Type}", typeof(T).Name);
             // Similar to AddAsync, specific service methods are preferred.
             return Task.FromResult(false);
        }

        // DeleteAsync using SpacetimeDB Identity
        public Task<bool> DeleteByIdentityAsync<T>(Identity id) where T : class
        {
             _logger.LogWarning("Generic DeleteByIdentityAsync<T>(Identity id) is deprecated. Use specific service methods (e.g., IUserService.DeleteUserAsync). Entity Type: {Type}", typeof(T).Name);
             // Similar to AddAsync, specific service methods are preferred.
             return Task.FromResult(false);
        }
    }
}
```

```csharp
// --- START OF FILE EmailService.cs ---

using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TicketSalesApp.Services.Implementations
{
    /// <summary>
    /// Implementation of the Email service
    /// </summary>
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;
        private readonly string? _smtpServer;
        private readonly int _smtpPort;
        private readonly string? _smtpUsername;
        private readonly string? _smtpPassword;
        private readonly string? _fromEmail;
        private readonly string? _fromName;
        private readonly bool _enableSsl;
        private readonly bool _isConfigured;

        public EmailService(
            IConfiguration configuration,
            ILogger<EmailService> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _smtpServer = _configuration["Email:SmtpServer"];
            _smtpPort = int.TryParse(_configuration["Email:SmtpPort"], out var port) ? port : 587; // Default port
            _smtpUsername = _configuration["Email:SmtpUsername"];
            _smtpPassword = _configuration["Email:SmtpPassword"];
            _fromEmail = _configuration["Email:FromEmail"];
            _fromName = _configuration["Email:FromName"] ?? "TicketSalesApp"; // Default name
            _enableSsl = bool.TryParse(_configuration["Email:EnableSsl"], out var ssl) ? ssl : true; // Default SSL

            // Check if essential configuration is present
            _isConfigured = !string.IsNullOrEmpty(_smtpServer) &&
                           !string.IsNullOrEmpty(_smtpUsername) &&
                           !string.IsNullOrEmpty(_smtpPassword) &&
                           !string.IsNullOrEmpty(_fromEmail);

            if (!_isConfigured)
            {
                _logger.LogWarning("Email service is not fully configured. Check Email settings in configuration.");
            }
        }

        /// <summary>
        /// Sends an email
        /// </summary>
        public async Task SendEmailAsync(string to, string subject, string body)
        {
            if (!_isConfigured)
            {
                _logger.LogError("Email service not configured. Cannot send email to {To} with subject {Subject}.", to, subject);
                // Optionally throw an exception or just return
                // throw new InvalidOperationException("Email service is not configured.");
                 return;
            }

            // Ensure non-null values for configured properties before use
             if (string.IsNullOrEmpty(_smtpServer) || string.IsNullOrEmpty(_smtpUsername) ||
                string.IsNullOrEmpty(_smtpPassword) || string.IsNullOrEmpty(_fromEmail))
            {
                _logger.LogError("Essential email configuration missing.");
                return;
            }


            try
            {
                _logger.LogInformation("Attempting to send email to: {To}, Subject: {Subject}", to, subject);

                var fromAddress = new MailAddress(_fromEmail, _fromName);
                var toAddress = new MailAddress(to);

                using (var smtpClient = new SmtpClient(_smtpServer, _smtpPort))
                {
                    smtpClient.EnableSsl = _enableSsl;
                    smtpClient.Credentials = new NetworkCredential(_smtpUsername, _smtpPassword);
                    // Consider adding timeout
                    // smtpClient.Timeout = 10000; // 10 seconds

                    using (var message = new MailMessage(fromAddress, toAddress))
                    {
                        message.Subject = subject;
                        message.Body = body;
                        message.IsBodyHtml = true; // Assume HTML body

                        await smtpClient.SendMailAsync(message);
                    }
                }

                _logger.LogInformation("Email sent successfully to: {To}", to);
            }
            catch (ArgumentNullException argEx) // Catch specific exceptions
            {
                 _logger.LogError(argEx, "Argument error sending email to: {To}. Check configuration and input.", to);
                 throw; // Rethrow maybe? Depends on desired behavior
            }
            catch (SmtpException smtpEx)
            {
                _logger.LogError(smtpEx, "SMTP error sending email to: {To}. Status Code: {StatusCode}", to, smtpEx.StatusCode);
                // Handle specific SMTP errors if needed
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "General error sending email to: {To}", to);
                throw;
            }
        }
    }
}
```

```csharp
// --- START OF FILE EmployeeService.cs ---

using Microsoft.Extensions.Logging;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;
using SpacetimeDB; // Added for Identity

namespace TicketSalesApp.Services.Implementations
{
    public class EmployeeService : IEmployeeService
    {
        private readonly ISpacetimeDBService _spacetimeDBService;
        private readonly ILogger<EmployeeService> _logger;

        public EmployeeService(ISpacetimeDBService spacetimeDBService, ILogger<EmployeeService> logger)
        {
            _spacetimeDBService = spacetimeDBService ?? throw new ArgumentNullException(nameof(spacetimeDBService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<List<Employee>> GetAllEmployeesAsync()
        {
            try
            {
                _logger.LogInformation("Retrieving all employees");
                var connection = _spacetimeDBService.GetConnection();
                var employees = connection.Db.Employee.Iter().ToList();
                return Task.FromResult(employees);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all employees");
                throw;
            }
        }

        public Task<Employee?> GetEmployeeByIdAsync(uint employeeId)
        {
            try
            {
                _logger.LogInformation("Retrieving employee by ID: {EmployeeId}", employeeId);
                var connection = _spacetimeDBService.GetConnection();
                var employee = connection.Db.Employee.EmployeeId.Find(employeeId);
                if (employee == null)
                {
                     _logger.LogWarning("Employee {EmployeeId} not found", employeeId);
                }
                return Task.FromResult(employee);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving employee by ID: {EmployeeId}", employeeId);
                throw;
            }
        }

        public Task<List<Employee>> GetEmployeesByJobIdAsync(uint jobId)
        {
            try
            {
                _logger.LogInformation("Retrieving employees by job ID: {JobId}", jobId);
                var connection = _spacetimeDBService.GetConnection();
                var employees = connection.Db.Employee.Iter()
                    .Where(e => e.JobId == jobId)
                    .ToList();
                return Task.FromResult(employees);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving employees by job ID: {JobId}", jobId);
                throw;
            }
        }

        public Task<bool> CreateEmployeeAsync(string employeeName, string employeeSurname, string? employeePatronym, uint jobId)
        {
            try
            {
                _logger.LogInformation("Attempting to create new employee: {Name} {Surname}", employeeName, employeeSurname);
                var connection = _spacetimeDBService.GetConnection();

                // Validate Job exists (optional, reducer should handle it)
                var job = connection.Db.Job.JobId.Find(jobId);
                if (job == null)
                {
                    _logger.LogWarning("Job not found: {JobId} when trying to create employee.", jobId);
                    return Task.FromResult(false); // Indicate failure due to non-existent job
                    // Or let the reducer handle the error: throw new ArgumentException($"Job with ID {jobId} not found.");
                }

                // Call the CreateEmployee reducer
                connection.Reducers.CreateEmployee(employeeName, employeeSurname, employeePatronym ?? string.Empty, jobId); // Pass empty string if null

                _logger.LogInformation("Successfully initiated creation of employee: {Name} {Surname}", employeeName, employeeSurname);
                return Task.FromResult(true); // Assume success if reducer doesn't throw
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating employee: {Name} {Surname}", employeeName, employeeSurname);
                return Task.FromResult(false); // Indicate failure
            }
        }

        public Task<bool> UpdateEmployeeAsync(uint employeeId, string? employeeName = null, string? employeeSurname = null, string? employeePatronym = null, uint? jobId = null)
        {
            try
            {
                _logger.LogInformation("Attempting to update employee: {EmployeeId}", employeeId);
                var connection = _spacetimeDBService.GetConnection();

                // Check if employee exists (optional, reducer handles it)
                var employee = connection.Db.Employee.EmployeeId.Find(employeeId);
                if (employee == null)
                {
                    _logger.LogWarning("Employee not found for update: {EmployeeId}", employeeId);
                    return Task.FromResult(false);
                }

                // Validate Job exists if provided (optional)
                if (jobId.HasValue)
                {
                    var job = connection.Db.Job.JobId.Find(jobId.Value);
                    if (job == null)
                    {
                        _logger.LogWarning("Job not found: {JobId} when trying to update employee {EmployeeId}.", jobId.Value, employeeId);
                         return Task.FromResult(false);
                        // Or let the reducer handle the error: throw new ArgumentException($"Job with ID {jobId.Value} not found.");
                    }
                }

                // Call the UpdateEmployee reducer
                connection.Reducers.UpdateEmployee(employeeId, employeeName, employeeSurname, employeePatronym, jobId);

                _logger.LogInformation("Successfully initiated update for employee {EmployeeId}", employeeId);
                return Task.FromResult(true); // Assume success
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating employee: {EmployeeId}", employeeId);
                return Task.FromResult(false); // Indicate failure
            }
        }

        public Task<bool> DeleteEmployeeAsync(uint employeeId)
        {
            try
            {
                _logger.LogInformation("Attempting to delete employee: {EmployeeId}", employeeId);
                var connection = _spacetimeDBService.GetConnection();

                 // Check if employee exists (optional, reducer handles it)
                var employee = connection.Db.Employee.EmployeeId.Find(employeeId);
                if (employee == null)
                {
                    _logger.LogWarning("Employee not found for deletion: {EmployeeId}", employeeId);
                    return Task.FromResult(false);
                }

                // Check if employee is assigned to routes (optional, reducer handles it)
                 var routes = connection.Db.Route.Iter().Any(r => r.DriverId == employeeId);
                 if (routes)
                 {
                     _logger.LogWarning("Cannot delete employee {EmployeeId} as they are assigned to routes", employeeId);
                     // Let the reducer throw the final error
                     // return Task.FromResult(false);
                 }


                // Call the DeleteEmployee reducer
                connection.Reducers.DeleteEmployee(employeeId);

                _logger.LogInformation("Successfully initiated deletion for employee {EmployeeId}", employeeId);
                return Task.FromResult(true); // Assume success
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting employee: {EmployeeId}: {ErrorMessage}", employeeId, ex.Message);
                return Task.FromResult(false); // Indicate failure
            }
        }

        // --- Job Methods ---

        public Task<List<Job>> GetAllJobsAsync()
        {
            try
            {
                _logger.LogInformation("Retrieving all jobs");
                var connection = _spacetimeDBService.GetConnection();
                var jobs = connection.Db.Job.Iter().ToList();
                return Task.FromResult(jobs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all jobs");
                throw;
            }
        }

        public Task<Job?> GetJobByIdAsync(uint jobId)
        {
            try
            {
                _logger.LogInformation("Retrieving job by ID: {JobId}", jobId);
                var connection = _spacetimeDBService.GetConnection();
                var job = connection.Db.Job.JobId.Find(jobId);
                 if (job == null)
                {
                     _logger.LogWarning("Job {JobId} not found", jobId);
                }
                return Task.FromResult(job);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving job by ID: {JobId}", jobId);
                throw;
            }
        }

        public Task<bool> CreateJobAsync(string jobTitle, string? jobInternship)
        {
            try
            {
                _logger.LogInformation("Attempting to create new job: {Title}", jobTitle);
                var connection = _spacetimeDBService.GetConnection();

                // Check if job already exists (optional, reducer handles it)
                var existingJob = connection.Db.Job.Iter().FirstOrDefault(j => j.JobTitle == jobTitle);
                if (existingJob != null)
                {
                    _logger.LogWarning("Job already exists with title: {Title}", jobTitle);
                    // Let reducer throw the error for consistency
                    // return Task.FromResult(false);
                }

                // Call the CreateJob reducer
                connection.Reducers.CreateJob(jobTitle, jobInternship ?? string.Empty); // Pass empty string if null

                 _logger.LogInformation("Successfully initiated creation of job: {Title}", jobTitle);
                return Task.FromResult(true); // Assume success
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating job: {Title}", jobTitle);
                return Task.FromResult(false); // Indicate failure
            }
        }

        public Task<bool> UpdateJobAsync(uint jobId, string? jobTitle = null, string? jobInternship = null)
        {
            try
            {
                _logger.LogInformation("Attempting to update job: {JobId}", jobId);
                var connection = _spacetimeDBService.GetConnection();

                // Check if job exists (optional)
                var job = connection.Db.Job.JobId.Find(jobId);
                if (job == null)
                {
                    _logger.LogWarning("Job not found for update: {JobId}", jobId);
                    return Task.FromResult(false);
                }

                // Check if new title conflicts (optional)
                if (jobTitle != null)
                {
                    var existingJob = connection.Db.Job.Iter().FirstOrDefault(j => j.JobTitle == jobTitle && j.JobId != jobId);
                    if (existingJob != null)
                    {
                        _logger.LogWarning("Job already exists with title: {Title}", jobTitle);
                        // Let reducer throw error
                        // return Task.FromResult(false);
                    }
                }

                // Call the UpdateJob reducer
                connection.Reducers.UpdateJob(jobId, jobTitle, jobInternship);

                _logger.LogInformation("Successfully initiated update for job {JobId}", jobId);
                return Task.FromResult(true); // Assume success
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating job: {JobId}", jobId);
                return Task.FromResult(false); // Indicate failure
            }
        }

        public Task<bool> DeleteJobAsync(uint jobId)
        {
            try
            {
                _logger.LogInformation("Attempting to delete job: {JobId}", jobId);
                var connection = _spacetimeDBService.GetConnection();

                // Check if job exists (optional)
                var job = connection.Db.Job.JobId.Find(jobId);
                if (job == null)
                {
                    _logger.LogWarning("Job not found for deletion: {JobId}", jobId);
                    return Task.FromResult(false);
                }

                // Check if job has employees (optional)
                var employees = connection.Db.Employee.Iter().Any(e => e.JobId == jobId);
                if (employees)
                {
                    _logger.LogWarning("Cannot delete job {JobId} as it has employees assigned", jobId);
                    // Let reducer throw error
                    // return Task.FromResult(false);
                }


                // Call the DeleteJob reducer
                connection.Reducers.DeleteJob(jobId);

                _logger.LogInformation("Successfully initiated deletion for job {JobId}", jobId);
                return Task.FromResult(true); // Assume success
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting job: {JobId}: {ErrorMessage}", jobId, ex.Message);
                return Task.FromResult(false); // Indicate failure
            }
        }
    }
}
```

```csharp
// --- START OF FILE ExportService.cs ---

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using TicketSalesApp.Services.Interfaces;
using System.Reflection; // Added for GetProperties

namespace TicketSalesApp.Services.Implementations
{
    public class ExportService : IExportService
    {
        private readonly ILogger<ExportService> _logger;

        public ExportService(ILogger<ExportService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // Export to Excel (CSV format)
        public Task<byte[]> ExportToExcelAsync<T>(IEnumerable<T> data, string sheetName)
        {
            _logger.LogInformation("Exporting data to Excel (CSV format) for sheet: {SheetName}", sheetName);
            // For simplicity, Excel export uses CSV format which Excel can open.
            // For true XLSX, a library like ClosedXML would be needed, but that adds dependencies.
            return ExportToCsvAsync(data);
        }

        // Export to PDF (HTML format for simplicity)
        public Task<byte[]> ExportToPdfAsync<T>(IEnumerable<T> data, string title)
        {
            _logger.LogInformation("Exporting data to PDF (HTML format) with title: {Title}", title);
            try
            {
                var sb = new StringBuilder();
                PropertyInfo[]? properties = null; // Get properties once

                // Create HTML document
                sb.AppendLine("<!DOCTYPE html>");
                sb.AppendLine("<html lang=\"en\">"); // Added lang attribute
                sb.AppendLine("<head>");
                sb.AppendLine("<meta charset=\"UTF-8\">");
                sb.AppendLine("<title>" + System.Security.SecurityElement.Escape(title) + "</title>"); // Escape title
                sb.AppendLine("<style>");
                sb.AppendLine("body { font-family: Arial, sans-serif; margin: 20px; font-size: 10pt; }"); // Smaller font
                sb.AppendLine("table { border-collapse: collapse; width: 100%; margin-top: 15px; table-layout: fixed; }"); // Fixed layout
                sb.AppendLine("th, td { border: 1px solid #ccc; padding: 6px; text-align: left; word-wrap: break-word; }"); // Allow word wrap, lighter border
                sb.AppendLine("th { background-color: #f2f2f2; font-weight: bold; }");
                sb.AppendLine("h1 { color: #333; font-size: 16pt; text-align: center; margin-bottom: 20px; }"); // Centered title
                sb.AppendLine("tr:nth-child(even) { background-color: #f9f9f9; }"); // Zebra striping
                sb.AppendLine("</style>");
                sb.AppendLine("</head>");
                sb.AppendLine("<body>");

                // Add title
                sb.AppendLine("<h1>" + System.Security.SecurityElement.Escape(title) + "</h1>");

                // Create table
                sb.AppendLine("<table>");

                 bool headerWritten = false;
                // Add data rows
                foreach (var item in data)
                {
                    if (item == null) continue; // Skip null items

                    if (!headerWritten)
                    {
                         properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
                         // Add headers
                         sb.AppendLine("<thead>"); // Use thead for header row
                         sb.AppendLine("<tr>");
                         foreach (var prop in properties)
                         {
                             sb.AppendLine("<th>" + System.Security.SecurityElement.Escape(prop.Name) + "</th>"); // Escape header names
                         }
                         sb.AppendLine("</tr>");
                         sb.AppendLine("</thead>");
                         sb.AppendLine("<tbody>"); // Start tbody
                         headerWritten = true;
                    }

                     if (properties == null) continue; // Should not happen if data is not empty

                    sb.AppendLine("<tr>");
                    foreach (var prop in properties)
                    {
                        object? propValue = prop.GetValue(item);
                        string valueString = propValue?.ToString() ?? "";
                        sb.AppendLine("<td>" + System.Security.SecurityElement.Escape(valueString) + "</td>"); // Escape cell values
                    }
                    sb.AppendLine("</tr>");
                }

                if (headerWritten)
                {
                     sb.AppendLine("</tbody>"); // End tbody
                }
                sb.AppendLine("</table>");
                sb.AppendLine("</body>");
                sb.AppendLine("</html>");

                // Return bytes using UTF-8 encoding (important for PDF converters)
                return Task.FromResult(Encoding.UTF8.GetBytes(sb.ToString()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting data to PDF format");
                throw; // Rethrow exception
            }
        }

        // Export to CSV
        public Task<byte[]> ExportToCsvAsync<T>(IEnumerable<T> data)
        {
             _logger.LogInformation("Exporting data to CSV format");
            try
            {
                var sb = new StringBuilder();
                PropertyInfo[]? properties = null;
                bool headerWritten = false;

                foreach (var item in data)
                {
                     if (item == null) continue;

                     if (!headerWritten)
                     {
                          properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
                          // Add headers
                          sb.AppendLine(string.Join(",", properties.Select(p => QuoteCsvValue(p.Name))));
                          headerWritten = true;
                     }

                     if (properties == null) continue;

                    var values = new List<string>();
                    foreach (var prop in properties)
                    {
                        object? propValue = prop.GetValue(item);
                        values.Add(QuoteCsvValue(propValue?.ToString() ?? ""));
                    }
                    sb.AppendLine(string.Join(",", values));
                }

                // Return bytes using UTF-8 encoding with BOM for better Excel compatibility
                return Task.FromResult(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray());

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting data to CSV format");
                throw; // Rethrow exception
            }
        }

        // Export to JSON
        public Task<string> ExportToJsonAsync<T>(IEnumerable<T> data)
        {
            _logger.LogInformation("Exporting data to JSON format");
            try
            {
                var jsonString = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    WriteIndented = true, // Make it readable
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping // Prevent over-escaping
                });
                return Task.FromResult(jsonString);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting data to JSON");
                throw; // Rethrow exception
            }
        }

        // Helper to quote CSV values if necessary
        private string QuoteCsvValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            // Fields containing commas, double quotes, or line breaks must be quoted.
            // Double quotes within a quoted field must be escaped by doubling them.
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }
            return value;
        }
    }
}
```

```csharp
// --- START OF FILE MagicLinkService.cs ---

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Implementations
{
    public class MagicLinkService : IMagicLinkService
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MagicLinkService> _logger;
        private const int MAGIC_LINK_EXPIRY_MINUTES = 15;

        public MagicLinkService(
            ISpacetimeDBService spacetimeService,
            IEmailService emailService,
            IConfiguration configuration,
            ILogger<MagicLinkService> logger)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<(bool success, string? errorMessage)> SendMagicLinkAsync(string email, string? userAgent, string? ipAddress)
        {
            try
            {
                _logger.LogInformation("Attempting to send magic link to email: {Email}", email);

                if (string.IsNullOrWhiteSpace(email))
                {
                    _logger.LogWarning("SendMagicLinkAsync called with empty email.");
                    return (false, "Email address is required.");
                }

                var conn = _spacetimeService.GetConnection();

                // Find user by email
                var user = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.Email == email && u.IsActive);

                if (user == null)
                {
                    // Don't reveal that the user doesn't exist, but log it.
                    _logger.LogWarning("Magic link requested for non-existent or inactive user: {Email}", email);
                    // Return success to prevent user enumeration attacks
                    return (true, null);
                }

                 if (user.EmailConfirmed != true) // Explicitly check for true
                 {
                     // Don't reveal that the email is not confirmed, but log it.
                     _logger.LogWarning("Magic link requested for unconfirmed email: {Email}", email);
                     return (true, null);
                 }

                // Generate a unique token
                var token = GenerateRandomToken();

                // Store the token in SpacetimeDB
                var expiryTime = DateTimeOffset.UtcNow.AddMinutes(MAGIC_LINK_EXPIRY_MINUTES);
                ulong expiryTimestamp = (ulong)expiryTime.ToUnixTimeMilliseconds();

                conn.Reducers.CreateMagicLinkToken(
                    user.UserId,
                    token,
                    expiryTimestamp,
                    userAgent ?? "Unknown",
                    ipAddress ?? "Unknown"
                );

                // Generate magic link URL
                var appUrl = _configuration["AppUrl"]; // Ensure this is configured in appsettings.json
                if (string.IsNullOrWhiteSpace(appUrl))
                {
                     _logger.LogError("AppUrl is not configured in appsettings.json. Cannot generate magic link URL.");
                     return(false, "Server configuration error.");
                }
                var magicLinkUrl = $"{appUrl.TrimEnd('/')}/api/auth/validate-magic-link?token={Uri.EscapeDataString(token)}";

                // Send the magic link email
                await _emailService.SendEmailAsync(
                    user.Email,
                    "Your Magic Login Link", // Subject
                    $"Click the link below to log in:<br><a href='{magicLinkUrl}'>{magicLinkUrl}</a><br>This link will expire in {MAGIC_LINK_EXPIRY_MINUTES} minutes." // Body
                );

                _logger.LogInformation("Magic link sent successfully to: {Email}", email);
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending magic link to email: {Email}", email);
                // Return a generic error message to the user
                return (false, "An error occurred while sending the magic link. Please try again later.");
            }
        }

        public Task<(bool success, UserProfile? user, string? errorMessage)> ValidateMagicLinkAsync(string token)
        {
            try
            {
                _logger.LogInformation("Validating magic link token (first 8 chars): {TokenStart}...", token.Length > 8 ? token.Substring(0, 8) : token);

                if (string.IsNullOrEmpty(token))
                {
                    return Task.FromResult<(bool, UserProfile?, string?)>((false, null, "Token is required"));
                }

                var conn = _spacetimeService.GetConnection();

                // Find the token
                var magicLinkToken = conn.Db.MagicLinkToken.Token.Find(token);

                if (magicLinkToken == null)
                {
                    _logger.LogWarning("Magic link token not found: {TokenStart}...", token.Length > 8 ? token.Substring(0, 8) : token);
                    return Task.FromResult<(bool, UserProfile?, string?)>((false, null, "Invalid or expired token"));
                }

                 if (magicLinkToken.IsUsed)
                 {
                     _logger.LogWarning("Magic link token already used: {TokenStart}...", token.Length > 8 ? token.Substring(0, 8) : token);
                     return Task.FromResult<(bool, UserProfile?, string?)>((false, null, "This magic link has already been used."));
                 }

                ulong currentTimestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (magicLinkToken.ExpiresAt < currentTimestamp)
                {
                    _logger.LogWarning("Magic link token expired: {TokenStart}...", token.Length > 8 ? token.Substring(0, 8) : token);
                    // Optionally delete expired token here or via a cleanup job
                    // conn.Reducers.DeleteMagicLinkToken(token); // Assuming a DeleteMagicLinkToken reducer exists
                    return Task.FromResult<(bool, UserProfile?, string?)>((false, null, "This magic link has expired."));
                }

                // Get the user
                var user = conn.Db.UserProfile.UserId.Find(magicLinkToken.UserId);

                if (user == null)
                {
                    _logger.LogWarning("User not found for magic link token: {TokenStart}...", token.Length > 8 ? token.Substring(0, 8) : token);
                    return Task.FromResult<(bool, UserProfile?, string?)>((false, null, "User associated with this link not found."));
                }

                if (!user.IsActive)
                {
                    _logger.LogWarning("Account is disabled for magic link token: {TokenStart}...", token.Length > 8 ? token.Substring(0, 8) : token);
                    return Task.FromResult<(bool, UserProfile?, string?)>((false, null, "Your account is currently disabled."));
                }

                _logger.LogInformation("Magic link token validated successfully for user: {Login}", user.Login);
                return Task.FromResult<(bool, UserProfile?, string?)>((true, user, null));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating magic link token: {TokenStart}...", token.Length > 8 ? token.Substring(0, 8) : token);
                return Task.FromResult<(bool, UserProfile?, string?)>((false, null, "An error occurred while validating the magic link."));
            }
        }

        public Task<bool> MarkMagicLinkAsUsedAsync(string token)
        {
            try
            {
                _logger.LogInformation("Marking magic link token as used: {TokenStart}...", token.Length > 8 ? token.Substring(0, 8) : token);

                var conn = _spacetimeService.GetConnection();

                // Call the reducer to mark the token as used
                conn.Reducers.UseMagicLinkToken(token);

                return Task.FromResult(true); // Assume success if reducer doesn't throw
            }
            catch (Exception ex)
            {
                // Log error, reducer might throw if token not found etc.
                _logger.LogError(ex, "Error marking magic link token as used: {TokenStart}...", token.Length > 8 ? token.Substring(0, 8) : token);
                return Task.FromResult(false);
            }
        }

        private string GenerateRandomToken()
        {
            // Generate a cryptographically secure random token
            // Using Base64 URL encoding to avoid issues with URL characters
            var randomBytes = RandomNumberGenerator.GetBytes(32); // 256 bits
            return Base64UrlEncode(randomBytes);
        }

        private static string Base64UrlEncode(byte[] input)
        {
            return Convert.ToBase64String(input)
                .TrimEnd('=') // Remove padding
                .Replace('+', '-') // Replace '+' with '-'
                .Replace('/', '_'); // Replace '/' with '_'
        }
    }
}
```

```csharp
// --- START OF FILE MaintenanceService.cs ---

using Microsoft.Extensions.Logging;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;
using SpacetimeDB; // Added for Identity

namespace TicketSalesApp.Services.Implementations
{
    public class MaintenanceService : IMaintenanceService
    {
        private readonly ISpacetimeDBService _spacetimeDBService;
        private readonly ILogger<MaintenanceService> _logger;

        public MaintenanceService(ISpacetimeDBService spacetimeDBService, ILogger<MaintenanceService> logger)
        {
            _spacetimeDBService = spacetimeDBService ?? throw new ArgumentNullException(nameof(spacetimeDBService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<List<Maintenance>> GetAllMaintenanceRecordsAsync()
        {
            try
            {
                _logger.LogInformation("Retrieving all maintenance records");
                var connection = _spacetimeDBService.GetConnection();
                var records = connection.Db.Maintenance.Iter().ToList();
                return Task.FromResult(records);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all maintenance records");
                throw;
            }
        }

        public Task<Maintenance?> GetMaintenanceByIdAsync(uint maintenanceId)
        {
            try
            {
                _logger.LogInformation("Retrieving maintenance record by ID: {MaintenanceId}", maintenanceId);
                var connection = _spacetimeDBService.GetConnection();
                var maintenance = connection.Db.Maintenance.MaintenanceId.Find(maintenanceId);
                 if (maintenance == null)
                {
                     _logger.LogWarning("Maintenance record {MaintenanceId} not found", maintenanceId);
                }
                return Task.FromResult(maintenance);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving maintenance record by ID: {MaintenanceId}", maintenanceId);
                throw;
            }
        }

        public Task<List<Maintenance>> GetMaintenanceByBusIdAsync(uint busId)
        {
            try
            {
                _logger.LogInformation("Retrieving maintenance records for bus: {BusId}", busId);
                var connection = _spacetimeDBService.GetConnection();
                var records = connection.Db.Maintenance.Iter()
                    .Where(m => m.BusId == busId)
                    .OrderByDescending(m => m.LastServiceDate)
                    .ToList();
                return Task.FromResult(records);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving maintenance records for bus: {BusId}", busId);
                throw;
            }
        }

        public Task<bool> CreateMaintenanceAsync(uint busId, ulong lastServiceDate, string serviceEngineer, string foundIssues, ulong nextServiceDate, string roadworthiness, string? maintenanceType)
        {
            try
            {
                _logger.LogInformation("Attempting to create maintenance record for bus: {BusId}", busId);
                var connection = _spacetimeDBService.GetConnection();

                 // Validate Bus exists (optional, reducer should handle)
                var bus = connection.Db.Bus.BusId.Find(busId);
                if (bus == null)
                {
                    _logger.LogWarning("Bus not found: {BusId} when creating maintenance record.", busId);
                    return Task.FromResult(false);
                    // throw new ArgumentException($"Bus with ID {busId} not found.");
                }

                // Call the CreateMaintenance reducer
                connection.Reducers.CreateMaintenance(
                    busId,
                    lastServiceDate,
                    serviceEngineer,
                    foundIssues,
                    nextServiceDate,
                    roadworthiness,
                    maintenanceType ?? "Regular" // Default type if null
                );

                _logger.LogInformation("Successfully initiated creation of maintenance record for bus: {BusId}", busId);
                return Task.FromResult(true); // Assume success
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating maintenance record for bus: {BusId}", busId);
                return Task.FromResult(false); // Indicate failure
            }
        }

        public Task<bool> UpdateMaintenanceAsync(uint maintenanceId, uint? busId = null, ulong? lastServiceDate = null, string? serviceEngineer = null, string? foundIssues = null, ulong? nextServiceDate = null, string? roadworthiness = null, string? maintenanceType = null, string? mileage = null)
        {
            try
            {
                _logger.LogInformation("Attempting to update maintenance record: {MaintenanceId}", maintenanceId);
                var connection = _spacetimeDBService.GetConnection();

                // Check if maintenance record exists (optional)
                var maintenance = connection.Db.Maintenance.MaintenanceId.Find(maintenanceId);
                if (maintenance == null)
                {
                    _logger.LogWarning("Maintenance record not found for update: {MaintenanceId}", maintenanceId);
                    return Task.FromResult(false);
                }

                 // Validate Bus exists if provided (optional)
                if (busId.HasValue)
                {
                    var bus = connection.Db.Bus.BusId.Find(busId.Value);
                    if (bus == null)
                    {
                        _logger.LogWarning("Bus not found: {BusId} when updating maintenance record {MaintenanceId}.", busId.Value, maintenanceId);
                        return Task.FromResult(false);
                        // throw new ArgumentException($"Bus with ID {busId.Value} not found.");
                    }
                }

                // Call the UpdateMaintenance reducer
                connection.Reducers.UpdateMaintenance(
                    maintenanceId,
                    busId, // Pass nullables directly, reducer handles defaults
                    lastServiceDate,
                    serviceEngineer,
                    foundIssues,
                    nextServiceDate,
                    roadworthiness,
                    maintenanceType,
                    mileage
                );

                _logger.LogInformation("Successfully initiated update for maintenance record: {MaintenanceId}", maintenanceId);
                return Task.FromResult(true); // Assume success
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating maintenance record: {MaintenanceId}", maintenanceId);
                return Task.FromResult(false); // Indicate failure
            }
        }

        public Task<bool> DeleteMaintenanceAsync(uint maintenanceId)
        {
            try
            {
                _logger.LogInformation("Attempting to delete maintenance record: {MaintenanceId}", maintenanceId);
                var connection = _spacetimeDBService.GetConnection();

                // Check if maintenance record exists (optional)
                var maintenance = connection.Db.Maintenance.MaintenanceId.Find(maintenanceId);
                if (maintenance == null)
                {
                    _logger.LogWarning("Maintenance record not found for deletion: {MaintenanceId}", maintenanceId);
                    return Task.FromResult(false);
                }

                // Call the DeleteMaintenance reducer
                connection.Reducers.DeleteMaintenance(maintenanceId);

                _logger.LogInformation("Successfully initiated deletion for maintenance record: {MaintenanceId}", maintenanceId);
                return Task.FromResult(true); // Assume success
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting maintenance record: {MaintenanceId}: {ErrorMessage}", maintenanceId, ex.Message);
                return Task.FromResult(false); // Indicate failure
            }
        }

        public Task<List<Maintenance>> GetBusMaintenanceHistoryAsync(uint busId)
        {
            // This method is essentially the same as GetMaintenanceByBusIdAsync
            return GetMaintenanceByBusIdAsync(busId);
        }
    }
}
```

```csharp
// --- START OF FILE PermissionService.cs ---

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TicketSalesApp.Services.Interfaces;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace TicketSalesApp.Services.Implementations
{
    public class PermissionService : IPermissionService
    {
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly ILogger<PermissionService> _logger;

        public PermissionService(ISpacetimeDBService spacetimeService, ILogger<PermissionService> logger)
        {
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<IEnumerable<Permission>> GetAllPermissionsAsync()
        {
            try
            {
                _logger.LogInformation("Getting all permissions");
                var conn = _spacetimeService.GetConnection();
                var permissions = conn.Db.Permission.Iter().Where(p => p.IsActive).ToList();
                _logger.LogInformation("Retrieved {Count} active permissions", permissions.Count);
                return Task.FromResult<IEnumerable<Permission>>(permissions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all permissions");
                throw;
            }
        }

        public Task<Permission?> GetPermissionByIdAsync(uint permissionId)
        {
            try
            {
                _logger.LogInformation("Getting permission by ID: {PermissionId}", permissionId);
                var conn = _spacetimeService.GetConnection();
                var permission = conn.Db.Permission.PermissionId.Find(permissionId);
                if (permission == null || !permission.IsActive)
                {
                    _logger.LogWarning("Permission not found or inactive with ID: {PermissionId}", permissionId);
                    return Task.FromResult<Permission?>(null);
                }
                return Task.FromResult<Permission?>(permission);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting permission by ID: {PermissionId}", permissionId);
                throw;
            }
        }

        public Task<IEnumerable<Permission>> GetPermissionsByCategoryAsync(string category)
        {
            try
            {
                 if (string.IsNullOrEmpty(category))
                 {
                      _logger.LogWarning("GetPermissionsByCategoryAsync called with empty category.");
                      return Task.FromResult<IEnumerable<Permission>>(new List<Permission>());
                 }

                _logger.LogInformation("Getting permissions by category: {Category}", category);
                var conn = _spacetimeService.GetConnection();
                var permissions = conn.Db.Permission.Iter()
                    .Where(p => p.Category.Equals(category, StringComparison.OrdinalIgnoreCase) && p.IsActive)
                    .ToList();
                _logger.LogInformation("Retrieved {Count} permissions for category {Category}", permissions.Count, category);
                return Task.FromResult<IEnumerable<Permission>>(permissions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting permissions by category: {Category}", category);
                throw;
            }
        }

        public Task<IEnumerable<string>> GetAllCategoriesAsync()
        {
            try
            {
                _logger.LogInformation("Getting all permission categories");
                var conn = _spacetimeService.GetConnection();
                var categories = conn.Db.Permission.Iter()
                    .Where(p => p.IsActive)
                    .Select(p => p.Category)
                    .Distinct(StringComparer.OrdinalIgnoreCase) // Case-insensitive distinct
                    .ToList();
                _logger.LogInformation("Retrieved {Count} distinct permission categories", categories.Count);
                return Task.FromResult<IEnumerable<string>>(categories);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all permission categories");
                throw;
            }
        }

        public Task<Permission?> CreatePermissionAsync(string name, string description, string category)
        {
            try
            {
                _logger.LogInformation("Attempting to create new permission: {Name}", name);
                 if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(category))
                 {
                     _logger.LogWarning("CreatePermissionAsync called with missing required fields.");
                     throw new ArgumentException("Name, Description, and Category cannot be empty.");
                 }

                var conn = _spacetimeService.GetConnection();

                 // Check if exists (optional, reducer handles)
                 var existingPermission = conn.Db.Permission.Iter().FirstOrDefault(p => p.Name == name);
                 if (existingPermission != null)
                 {
                     _logger.LogWarning("Permission with name {Name} already exists.", name);
                     // Let reducer throw the exception for consistency
                     // return Task.FromResult<Permission?>(null);
                 }

                // Call the AddNewPermission reducer
                conn.Reducers.AddNewPermission(name, description, category);

                // Retrieve the newly created permission (best effort)
                var newPermission = conn.Db.Permission.Iter()
                    .OrderByDescending(p => p.PermissionId) // Assuming higher ID is newer
                    .FirstOrDefault(p => p.Name == name);

                if (newPermission == null)
                {
                    _logger.LogError("Permission {Name} was not retrieved after creation attempt.", name);
                    return Task.FromResult<Permission?>(null); // Indicate potential issue
                }

                _logger.LogInformation("Successfully initiated creation of permission {Name} with ID {PermissionId}", name, newPermission.PermissionId);
                return Task.FromResult<Permission?>(newPermission);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating permission {Name}", name);
                // Don't return null on exception, let it bubble up or return false from controller
                 throw;
                // return Task.FromResult<Permission?>(null);
            }
        }

        public Task<bool> UpdatePermissionAsync(uint permissionId, string? name, string? description, string? category, bool? isActive)
        {
            try
            {
                _logger.LogInformation("Attempting to update permission {PermissionId}", permissionId);
                var conn = _spacetimeService.GetConnection();

                // Check if permission exists (optional)
                var permission = conn.Db.Permission.PermissionId.Find(permissionId);
                if (permission == null)
                {
                    _logger.LogWarning("Permission not found for update: {PermissionId}", permissionId);
                    return Task.FromResult(false);
                }

                 // Check name uniqueness if provided (optional)
                 if (name != null && name != permission.Name)
                 {
                     var existingPermission = conn.Db.Permission.Iter()
                         .FirstOrDefault(p => p.Name == name && p.PermissionId != permissionId);
                     if (existingPermission != null)
                     {
                         _logger.LogWarning("Permission with name {Name} already exists", name);
                         // Let reducer throw the error
                         // return Task.FromResult(false);
                     }
                 }

                // Call the UpdatePermission reducer
                conn.Reducers.UpdatePermission(permissionId, name, description, category, isActive);

                _logger.LogInformation("Successfully initiated update for permission {PermissionId}", permissionId);
                return Task.FromResult(true); // Assume success
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating permission {PermissionId}", permissionId);
                return Task.FromResult(false); // Indicate failure
            }
        }

        public Task<bool> DeletePermissionAsync(uint permissionId)
        {
            try
            {
                _logger.LogInformation("Attempting to delete permission {PermissionId}", permissionId);
                var conn = _spacetimeService.GetConnection();

                // Check if permission exists (optional)
                var permission = conn.Db.Permission.PermissionId.Find(permissionId);
                if (permission == null)
                {
                    _logger.LogWarning("Permission not found for deletion: {PermissionId}", permissionId);
                    return Task.FromResult(false);
                }

                 // Check if the permission is in use (optional)
                 var isInUse = conn.Db.RolePermission.Iter().Any(rp => rp.PermissionId == permissionId);
                 if (isInUse)
                 {
                     _logger.LogWarning("Cannot delete permission {PermissionId} as it is in use by roles.", permissionId);
                     // Let reducer throw the error
                     // return Task.FromResult(false);
                 }

                // Call the DeletePermission reducer
                conn.Reducers.DeletePermission(permissionId);

                _logger.LogInformation("Successfully initiated deletion for permission {PermissionId}", permissionId);
                return Task.FromResult(true); // Assume success
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting permission {PermissionId}: {ErrorMessage}", permissionId, ex.Message);
                return Task.FromResult(false); // Indicate failure
            }
        }

        public Task<bool> IsPermissionInUseAsync(uint permissionId)
        {
            try
            {
                _logger.LogDebug("Checking if permission {PermissionId} is in use", permissionId);
                var conn = _spacetimeService.GetConnection();
                var isInUse = conn.Db.RolePermission.Iter().Any(rp => rp.PermissionId == permissionId);
                _logger.LogDebug("Permission {PermissionId} is {Status}", permissionId, isInUse ? "in use" : "not in use");
                return Task.FromResult(isInUse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if permission {PermissionId} is in use", permissionId);
                throw;
            }
        }
    }
}
```
using System.Collections.Immutable;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;
using System.Security.Claims;
using System.Web;
using Microsoft.AspNetCore.Authentication.Cookies;
using Oidc.OpenIddict.AuthorizationServer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Oidc.OpenIddict.AuthorizationServer.Controllers
{
    [ApiController]
    public class AuthorizationController : Controller
    {
        private readonly IOpenIddictApplicationManager _applicationManager;
        private readonly IOpenIddictScopeManager _scopeManager;
        private readonly AuthorizationService _authService;

        public AuthorizationController(
            IOpenIddictApplicationManager applicationManager,
            IOpenIddictScopeManager scopeManager,
            AuthorizationService authService)
        {
            _applicationManager = applicationManager;
            _scopeManager = scopeManager;
            _authService = authService;
        }

        [HttpGet("~/connect/authorize")]
        [HttpPost("~/connect/authorize")]
        public async Task<IActionResult> Authorize()
        {
            var request = HttpContext.GetOpenIddictServerRequest() ??
                          throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

            var application = await _applicationManager.FindByClientIdAsync(request.ClientId) ??
                              throw new InvalidOperationException("Details concerning the calling client application cannot be found.");

            if (await _applicationManager.GetConsentTypeAsync(application) != ConsentTypes.Explicit)
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidClient,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "Only clients with explicit consent type are allowed."
                    }));
            }

            var parameters = _authService.ParseOAuthParameters(HttpContext, new List<string> { Parameters.Prompt });

            var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            if (!_authService.IsAuthenticated(result, request))
            {
                return Challenge(properties: new AuthenticationProperties
                {
                    RedirectUri = _authService.BuildRedirectUrl(HttpContext.Request, parameters)
                }, new[] { CookieAuthenticationDefaults.AuthenticationScheme });
            }

            if (request.HasPrompt(Prompts.Login))
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                return Challenge(properties: new AuthenticationProperties
                {
                    RedirectUri = _authService.BuildRedirectUrl(HttpContext.Request, parameters)
                }, new[] { CookieAuthenticationDefaults.AuthenticationScheme });
            }

            var consentClaim = result.Principal.GetClaim(Consts.ConsentNaming);

            // it might be extended in a way that consent claim will contain list of allowed client ids.
            if (consentClaim != Consts.GrantAccessValue || request.HasPrompt(Prompts.Consent))
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                var returnUrl = HttpUtility.UrlEncode(_authService.BuildRedirectUrl(HttpContext.Request, parameters));
                var consentRedirectUrl = $"/Consent?returnUrl={returnUrl}";

                return Redirect(consentRedirectUrl);
            }

            var userId = result.Principal.FindFirst(ClaimTypes.Email)!.Value;

            var identity = new ClaimsIdentity(
                authenticationType: TokenValidationParameters.DefaultAuthenticationType,
                nameType: Claims.Name,
                roleType: Claims.Role);

            identity.SetClaim(Claims.Subject, userId)
                .SetClaim(Claims.Email, userId)
                .SetClaim(Claims.Name, userId)
                .SetClaims(Claims.Role, new List<string> { "user", "admin" }.ToImmutableArray());

            identity.SetScopes(request.GetScopes());
            identity.SetResources(await _scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());
            identity.SetDestinations(c => AuthorizationService.GetDestinations(identity, c));

            return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        [HttpPost("~/connect/token")]
        public async Task<IActionResult> Exchange()
        {
            var request = HttpContext.GetOpenIddictServerRequest() ??
                          throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

            if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
                throw new InvalidOperationException("The specified grant type is not supported.");

            var result =
                await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            var userId = result.Principal.GetClaim(Claims.Subject);

            if (string.IsNullOrEmpty(userId))
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "Cannot find user from the token."
                    }));
            }

            var identity = new ClaimsIdentity(result.Principal.Claims,
                authenticationType: TokenValidationParameters.DefaultAuthenticationType,
                nameType: Claims.Name,
                roleType: Claims.Role);

            identity.SetClaim(Claims.Subject, userId)
                .SetClaim(Claims.Email, userId)
                .SetClaim(Claims.Name, userId)
                .SetClaims(Claims.Role, new List<string> { "user", "admin" }.ToImmutableArray());

            identity.SetDestinations(c => AuthorizationService.GetDestinations(identity, c));

            return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        [Authorize(AuthenticationSchemes = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)]
        [HttpGet("~/connect/userinfo"), HttpPost("~/connect/userinfo")]
        public async Task<IActionResult> Userinfo()
        {
            if (User.GetClaim(Claims.Subject) != Consts.Email)
            {
                return Challenge(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidToken,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "The specified access token is bound to an account that no longer exists."
                    }));
            }

            var claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                // Note: the "sub" claim is a mandatory claim and must be included in the JSON response.
                [Claims.Subject] = Consts.Email
            };

            if (User.HasScope(Scopes.Email))
            {
                claims[Claims.Email] = Consts.Email;
            }

            return Ok(claims);
        }

        [HttpGet("~/connect/logout")]
        [HttpPost("~/connect/logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            return SignOut(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties
                {
                    RedirectUri = "/"
                });
        }
    }
}
