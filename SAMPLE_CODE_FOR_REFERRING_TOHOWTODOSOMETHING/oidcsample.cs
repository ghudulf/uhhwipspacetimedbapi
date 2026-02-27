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
using Microsoft.AspNetCore.Mvc;

namespace AliceIdentityService.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }
}
using Microsoft.AspNetCore.Mvc;

namespace AliceIdentityService.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
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

// --- File: AuthController.cs ---

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Extensions; // For GetEncodedPathAndQuery()
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities; // For QueryHelpers
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization; // For JsonExtensionData
using System.Threading.Tasks;
using System.Web; // For HttpUtility
using TicketSalesApp.Services.Interfaces; // Your service interfaces
using Fido2NetLib; // For WebAuthn Types
using Fido2NetLib.Objects; // For WebAuthn Types
using static OpenIddict.Abstractions.OpenIddictConstants; // For Claims, Scopes, Errors etc.
// Using TicketSalesApp namespace for stores assuming they live there
using TicketSalesApp.Services.Implementations; // For Store implementations passed via DI (if needed for context, though unlikely directly)

namespace BRU_AVTOPARK_AspireAPI.ApiService.Controllers // Match your project namespace
{
    [ApiController]
    [Route("api/[controller]")] // Base route for non-OIDC actions
    public class AuthController : ControllerBase
    {
        #region Dependencies

        // Core Services
        private readonly IAuthenticationService _authService;
        private readonly IUserService _userService;
        private readonly IRoleService _roleService;
        private readonly IPermissionService _permissionService;
        private readonly ISpacetimeDBService _spacetimeService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;
        private readonly IMemoryCache _cache; // Used for OIDC state, codes, QR sessions

        // Specific Auth Method Services
        private readonly IQRAuthenticationService _qrAuthService;
        private readonly ITotpService _totpService;
        private readonly IWebAuthnService _webAuthnService;
        private readonly IMagicLinkService _magicLinkService;
        private readonly IEmailService _emailService;

        // OpenIddict Services
        private readonly IOpenIddictApplicationManager _applicationManager;
        private readonly IOpenIddictAuthorizationManager _authorizationManager;
        private readonly IOpenIddictScopeManager _scopeManager;
        private readonly IOpenIddictTokenManager _tokenManager;
        private readonly IAuthenticationSchemeProvider _schemeProvider; // For logout potentially

        #endregion

        #region Constructor

        public AuthController(
            IAuthenticationService authService, IUserService userService, IRoleService roleService,
            IPermissionService permissionService, ISpacetimeDBService spacetimeService, IConfiguration configuration,
            ILogger<AuthController> logger, IMemoryCache cache, IQRAuthenticationService qrAuthService,
            ITotpService totpService, IWebAuthnService webAuthnService, IMagicLinkService magicLinkService,
            IEmailService emailService,
            IOpenIddictApplicationManager applicationManager, IOpenIddictAuthorizationManager authorizationManager,
            IOpenIddictScopeManager scopeManager, IOpenIddictTokenManager tokenManager,
            IAuthenticationSchemeProvider schemeProvider)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _roleService = roleService ?? throw new ArgumentNullException(nameof(roleService));
            _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _qrAuthService = qrAuthService ?? throw new ArgumentNullException(nameof(qrAuthService));
            _totpService = totpService ?? throw new ArgumentNullException(nameof(totpService));
            _webAuthnService = webAuthnService ?? throw new ArgumentNullException(nameof(webAuthnService));
            _magicLinkService = magicLinkService ?? throw new ArgumentNullException(nameof(magicLinkService));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _applicationManager = applicationManager ?? throw new ArgumentNullException(nameof(applicationManager));
            _authorizationManager = authorizationManager ?? throw new ArgumentNullException(nameof(authorizationManager));
            _scopeManager = scopeManager ?? throw new ArgumentNullException(nameof(scopeManager));
            _tokenManager = tokenManager ?? throw new ArgumentNullException(nameof(tokenManager));
            _schemeProvider = schemeProvider ?? throw new ArgumentNullException(nameof(schemeProvider));
        }

        #endregion

       #region Request Models

    public class LoginRequest
    {
        public required string Username { get; set; }
        public required string Password { get; set; }
        public bool SkipTwoFactor { get; set; } = false;
    }

    public class ClaimAccountRequest
    {
        public required string Username { get; set; }
        public required string Password { get; set; }
        public bool GenerateNewIdentity { get; set; } = true;
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

    // Add these form request models at the end of the file
    public class RegisterClientFormRequest
    {
        public required string ClientId { get; set; }
        public required string ClientSecret { get; set; }
        public required string DisplayName { get; set; }
        public required string RedirectUris { get; set; }
        public required string PostLogoutRedirectUris { get; set; }
        public required string AllowedScopes { get; set; }
        public bool RequireConsent { get; set; }
    }

    public class UpdateClientFormRequest
    {
        public required string DisplayName { get; set; }
        public string? ClientSecret { get; set; }
        public required string RedirectUris { get; set; }
        public required string PostLogoutRedirectUris { get; set; }
        public required string AllowedScopes { get; set; }
        public bool RequireConsent { get; set; }
    }

    // Legacy login request model for Avalonia UI
    public class LegacyLoginRequest
    {
        public string Login { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
    
    // Legacy register request model for Avalonia UI
    public class LegacyRegisterRequest
    {
        public string Login { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public int Role { get; set; } = 0;
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
    }
        // --- OIDC Token Endpoint ---
        public class TokenRequest // Matches OAuth2 spec
        {
            [FromForm(Name = GrantTypes.GrantType)] public required string GrantType { get; set; }
            [FromForm(Name = Parameters.Code)] public string? Code { get; set; }
            [FromForm(Name = Parameters.RefreshToken)] public string? RefreshToken { get; set; }
            [FromForm(Name = Parameters.RedirectUri)] public string? RedirectUri { get; set; }
            [FromForm(Name = Parameters.ClientId)] public required string ClientId { get; set; }
            [FromForm(Name = Parameters.ClientSecret)] public string? ClientSecret { get; set; }
            [FromForm(Name = Parameters.Username)] public string? Username { get; set; }
            [FromForm(Name = Parameters.Password)] public string? Password { get; set; }
            [FromForm(Name = Parameters.Scope)] public string? Scope { get; set; }
            [FromForm(Name = Parameters.CodeVerifier)] public string? CodeVerifier { get; set; }
        }
        public class TokenResponse // Matches OAuth2 spec
        {
            [JsonPropertyName(Parameters.AccessToken)] public string AccessToken { get; set; } = string.Empty;
            [JsonPropertyName(Parameters.TokenType)] public string TokenType { get; set; } = TokenTypes.Bearer;
            [JsonPropertyName(Parameters.ExpiresIn)] public int ExpiresIn { get; set; }
            [JsonPropertyName(Parameters.RefreshToken)] public string? RefreshToken { get; set; }
            [JsonPropertyName(Parameters.Scope)] public string Scope { get; set; } = string.Empty;
            [JsonPropertyName(Parameters.IdToken)] public string? IdToken { get; set; }
        }

        // --- OIDC UserInfo ---
        public class UserInfoResponse // Standard OIDC UserInfo claims
        {
            [JsonPropertyName(Claims.Subject)] public string Sub { get; set; } = string.Empty;
            [JsonPropertyName(Claims.Name)] public string? Name { get; set; }
            [JsonPropertyName(Claims.PreferredUsername)] public string? PreferredUsername { get; set; }
            [JsonPropertyName(Claims.Email)] public string? Email { get; set; }
            [JsonPropertyName(Claims.EmailVerified)] public bool? EmailVerified { get; set; }
            [JsonPropertyName(Claims.PhoneNumber)] public string? PhoneNumber { get; set; }
            [JsonPropertyName(Claims.PhoneNumberVerified)] public bool? PhoneNumberVerified { get; set; }
            [JsonPropertyName(Claims.Role)] public List<string>? Roles { get; set; }
            [JsonExtensionData] public Dictionary<string, object> OtherClaims { get; set; } = new Dictionary<string, object>();
        }

        // --- OIDC Client Admin ---
        public class RegisterClientFormRequest { public required string ClientId { get; set; } public required string ClientSecret { get; set; } public required string DisplayName { get; set; } public required string RedirectUris { get; set; } public required string PostLogoutRedirectUris { get; set; } public required string AllowedScopes { get; set; } public bool RequireConsent { get; set; } }
        public class UpdateClientFormRequest { public required string DisplayName { get; set; } public string? ClientSecret { get; set; } public required string RedirectUris { get; set; } public required string PostLogoutRedirectUris { get; set; } public required string AllowedScopes { get; set; } public bool RequireConsent { get; set; } }
        public class RegisterClientResponse { public string ClientId { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty; }
        public class UpdateClientResponse { public string ClientId { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty; }
        public class DeleteClientResponse { public string ClientId { get; set; } = string.Empty; public bool Deleted { get; set; } }
        public class ClientDto { public string? ClientId { get; set; } public string? DisplayName { get; set; } }
        public class GetClientsResponse { public List<ClientDto> Clients { get; set; } = new List<ClientDto>(); }
        public class GetClientResponse { public string ClientId { get; set; } = string.Empty; public string? DisplayName { get; set; } public string[] RedirectUris { get; set; } = Array.Empty<string>(); public string[] PostLogoutRedirectUris { get; set; } = Array.Empty<string>(); public string[] AllowedScopes { get; set; } = Array.Empty<string>(); public bool RequireConsent { get; set; } }

        // --- OIDC Consent Screen ---
        public class ConsentViewModel { public string ClientId { get; set; } = string.Empty; public string ClientName { get; set; } = string.Empty; public Dictionary<string, string> Scopes { get; set; } = new(); public string? Error { get; set; } public string ResponseType { get; set; } = string.Empty; public string RedirectUri { get; set; } = string.Empty; public string State { get; set; } = string.Empty; public string? Nonce { get; set; } }
        public class ConsentRequest { public required string RequestId { get; set; } public bool Consent { get; set; } }
        public class ConsentRequiredResponse { public string RequestId { get; set; } = string.Empty; public string ClientName { get; set; } = string.Empty; public string[] Scopes { get; set; } = Array.Empty<string>(); }

        // --- OIDC Authorize Callback (Internal Processing) ---
        public class AuthorizationCodeData { public uint UserId { get; set; } public string Subject { get; set; } = string.Empty; public string[] Scopes { get; set; } = Array.Empty<string>(); public string RedirectUri { get; set; } = string.Empty; public string ClientId { get; set; } = string.Empty; }

         // --- OIDC Logout ---
        public class LogoutConfirmationRequest { public string? RedirectUri { get; set; } public string? State { get; set; } }


        #endregion

        //===========================
        // HTML Rendering Methods
        //===========================
        #region HTML Templates

        // --- Base Template ---
        private const string BaseHtmlTemplate = @"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{0}</title>
    <style>
        :root {{
            --primary-color: #fc3f1d;
            --primary-dark: #d93412;
            --primary-light: #ff5c3e;
            --background-color: #f6f7f8;
            --card-color: #ffffff;
            --text-color: #21201f;
            --text-muted: #838383;
            --border-color: #e7e8ea;
            --error-color: #ef4444;
            --success-color: #10b981;
            --warning-color: #f59e0b;
            --shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
            
            /* Yandex ID specific variables */
            --id-color-surface-submerged: #f6f7f8;
            --id-color-surface-elevated-0: #ffffff;
            --id-color-line-normal: #e7e8ea;
            --id-color-default-bg-base: #f5f5f5;
            --id-color-status-negative: #ff3333;
            --id-card-border-radius: 12px;
            --id-typography-heading-l: 500 28px/32px 'YS Text', 'Helvetica Neue', Arial, sans-serif;
            --id-typography-heading-m: 500 20px/24px 'YS Text', 'Helvetica Neue', Arial, sans-serif;
            --id-typography-text-m: 400 16px/20px 'YS Text', 'Helvetica Neue', Arial, sans-serif;
            --id-typography-text-s: 400 14px/18px 'YS Text', 'Helvetica Neue', Arial, sans-serif;
            --id-typography-text-xs: 400 13px/16px 'YS Text', 'Helvetica Neue', Arial, sans-serif;
        }}
        
        [data-theme=""dark""] {{
            --primary-color: #fc3f1d;
            --primary-dark: #d93412;
            --primary-light: #ff5c3e;
            --background-color: #21201f;
            --card-color: #312f2f;
            --text-color: #ffffff;
            --text-muted: #b3b3b3;
            --border-color: #3b3a38;
            --shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
            
            /* Yandex ID dark mode colors */
            --id-color-surface-submerged: #21201f;
            --id-color-surface-elevated-0: #312f2f;
            --id-color-line-normal: #3b3a38;
        }}

        * {{
            margin: 0;
            padding: 0;
            box-sizing: border-box;
            font-family: 'YS Text', 'Helvetica Neue', Arial, sans-serif;
        }}

        body {{
            background-color: var(--background-color);
            color: var(--text-color);
            line-height: 1.5;
            min-height: 100vh;
            transition: all 0.3s ease;
            display: flex;
            flex-direction: column;
            height: 100vh;
        }}

        .auth-page-body {{
            background-color: var(--background-color);
            background-image: url('https://yastatic.net/s3/passport-auth/freezer/_/12l0Lb-3jyLI.jpg');
            background-size: cover;
            background-position: center;
            background-repeat: no-repeat;
        }}

        [data-theme=""dark""] .auth-page-body {{
            background-image: url('https://yastatic.net/s3/passport-auth/freezer/_/12l0Lb-3jyLI.jpg');
        }}

        .navbar {{
            display: flex;
            justify-content: space-between;
            align-items: center;
            padding: 1rem 2rem;
            background-color: var(--card-color);
            box-shadow: var(--shadow);
            position: relative;
            z-index: 10;
        }}

        .logo {{
            font-size: 1.5rem;
            font-weight: 500;
            color: var(--text-color);
            text-decoration: none;
            display: flex;
            align-items: center;
        }}

        .logo::before {{
            content: '';
            display: inline-block;
            width: 24px;
            height: 24px;
            background-color: var(--primary-color);
            border-radius: 4px;
            margin-right: 8px;
        }}

        .theme-toggle {{
            background: none;
            border: none;
            color: var(--text-color);
            cursor: pointer;
            font-size: 1.2rem;
            width: 2.5rem;
            height: 2.5rem;
            border-radius: 50%;
            display: flex;
            align-items: center;
            justify-content: center;
            transition: background-color 0.2s;
        }}

        .theme-toggle:hover {{
            background-color: var(--border-color);
        }}

        .container {{
            max-width: 400px;
            margin: 2rem auto;
            padding: 0 1rem;
            width: 100%;
            flex: 1;
            display: flex;
            flex-direction: column;
            justify-content: center;
        }}

        .login-container {{
            display: flex;
            flex-direction: column;
            justify-content: center;
            align-items: center;
            min-height: calc(100vh - 64px);
            padding: 1rem;
        }}

        .card {{
            background-color: var(--card-color);
            border-radius: 0.75rem;
            box-shadow: var(--shadow);
            overflow: hidden;
            transition: all 0.3s ease;
            width: 100%;
            max-width: 400px;
        }}

        .auth-card {{
            background-color: #21201f;
            color: white;
            border-radius: 1rem;
            box-shadow: 0 4px 20px rgba(0, 0, 0, 0.25);
            max-width: 360px;
        }}

        .card-header {{
            padding: 1.5rem;
            border-bottom: 1px solid var(--border-color);
        }}

        .card-body {{
            padding: 1.5rem;
        }}

        .card-footer {{
            padding: 1rem 1.5rem;
            border-top: 1px solid var(--border-color);
            display: flex;
            justify-content: space-between;
            align-items: center;
        }}

        h1, h2, h3, h4, h5, h6 {{
            color: var(--text-color);
            font-weight: 500;
            margin-bottom: 0.5rem;
        }}

        .auth-card h1, .auth-card h2, .auth-card h3, .auth-card label, .auth-card p {{
            color: white;
        }}

        h1 {{
            font-size: 1.75rem;
        }}

        p {{
            margin-bottom: 1rem;
            color: var(--text-muted);
        }}

        .form-group {{
            margin-bottom: 1.25rem;
        }}

        label {{
            display: block; 
            margin-bottom: 0.5rem;
            font-weight: 400;
        }}

        input, select, textarea {{
            width: 100%;
            padding: 0.75rem;
            border: 1px solid var(--border-color);
            border-radius: 0.5rem;
            font-size: 1rem;
            background-color: var(--card-color);
            color: var(--text-color);
            transition: border-color 0.15s ease-in-out, box-shadow 0.15s ease-in-out;
        }}

        .auth-card input {{
            background-color: rgba(255, 255, 255, 0.1);
            color: white;
            border: none;
        }}

        input:focus, select:focus, textarea:focus {{
            outline: none;
            border-color: var(--primary-color);
            box-shadow: 0 0 0 2px rgba(252, 63, 29, 0.1);
        }}

        button, .btn {{
            display: inline-block;
            width: 100%;
            padding: 0.75rem 1.5rem;
            background-color: var(--primary-color);
            color: white;
            border: none;
            border-radius: 0.5rem;
            font-size: 1rem;
            font-weight: 500;
            cursor: pointer;
            transition: background-color 0.15s ease-in-out, transform 0.1s ease;
            text-align: center;
            text-decoration: none;
        }}

        .auth-card button {{
            background-color: white;
            color: black;
        }}

        button:hover, .btn:hover {{
            background-color: var(--primary-dark);
        }}

        .auth-card button:hover {{
            background-color: #f0f0f0;
        }}

        button:active, .btn:active {{
            transform: translateY(1px);
        }}

        .btn-secondary {{
            background-color: transparent;
            color: var(--primary-color);
            border: 1px solid var(--primary-color);
        }}

        .btn-secondary:hover {{
            background-color: rgba(252, 63, 29, 0.1);
        }}

        .btn-block {{
            display: block;
            width: 100%;
        }}

        .error-message {{
            color: var(--error-color);
            background-color: rgba(239, 68, 68, 0.1);
            padding: 0.75rem;
            border-radius: 0.5rem;
            margin-bottom: 1.5rem;
            font-size: 0.875rem;
            display: flex;
            align-items: center;
        }}

        .success-message {{
            color: var(--success-color);
            background-color: rgba(16, 185, 129, 0.1);
            padding: 0.75rem;
            border-radius: 0.5rem;
            margin-bottom: 1.5rem;
            font-size: 0.875rem;
            display: flex;
            align-items: center;
        }}

        .qr-code {{
            display: flex;
            justify-content: center;
            margin: 2rem 0;
        }}

        .qr-code img {{
            max-width: 200px;
            height: auto;
            padding: 0.5rem;
            background-color: white;
            border-radius: 0.5rem;
        }}

        .code-display {{
            font-family: monospace;
            background-color: rgba(0, 0, 0, 0.05);
            padding: 0.5rem;
            border-radius: 0.25rem;
            word-break: break-all;
            margin: 0.5rem 0;
        }}

        [data-theme=""dark""] .code-display {{
            background-color: rgba(255, 255, 255, 0.05);
        }}

        .text-center {{
            text-align: center;
        }}

        .info-box {{
            background-color: rgba(252, 63, 29, 0.07);
            padding: 1rem;
            border-radius: 0.5rem;
            margin-bottom: 1.5rem;
        }}

        .link {{
            color: var(--primary-color);
            text-decoration: none;
            transition: color 0.15s ease;
        }}

        .auth-card .link {{
            color: #76a6f5;
        }}

        .link:hover {{
            color: var(--primary-dark);
            text-decoration: underline;
        }}

        .text-muted {{
            color: var(--text-muted);
        }}

        .flex {{
            display: flex;
        }}

        .flex-col {{
            flex-direction: column;
        }}

        .flex-wrap {{
            flex-wrap: wrap;
        }}

        .items-center {{
            align-items: center;
        }}

        .justify-center {{
            justify-content: center;
        }}

        .justify-between {{
            justify-content: space-between;
        }}

        .gap-2 {{
            gap: 0.5rem;
        }}

        .gap-4 {{
            gap: 1rem;
        }}

        .my-2 {{
            margin-top: 0.5rem;
            margin-bottom: 0.5rem;
        }}

        .my-4 {{
            margin-top: 1rem;
            margin-bottom: 1rem;
        }}

        .mt-4 {{
            margin-top: 1rem;
        }}

        .mt-8 {{
            margin-top: 2rem;
        }}

        /* Layout styles */
        .page-wrapper {{
            display: flex;
            min-height: 100vh;
        }}

        .sidebar {{
            width: 250px;
            background-color: var(--card-color);
            padding: 1.5rem 0;
            border-right: 1px solid var(--border-color);
        }}

        .sidebar-link {{
            display: flex;
            align-items: center;
            padding: 0.75rem 1.5rem;
            color: var(--text-color);
            text-decoration: none;
            transition: background-color 0.15s ease;
        }}

        .sidebar-link:hover {{
            background-color: var(--background-color);
        }}

        .sidebar-link.active {{
            border-left: 3px solid var(--primary-color);
            background-color: var(--background-color);
        }}

        .main-content {{
            flex: 1;
            padding: 1.5rem;
            overflow-y: auto;
        }}

        .profile-page-wrapper {{
            display: flex;
            min-height: 100vh;
            flex-direction: column;
        }}

        .profile-container {{
            max-width: 1200px;
            margin: 0 auto;
            padding: 1.5rem;
            width: 100%;
            display: flex;
            flex-direction: column;
            align-items: center;
        }}

        /* Yandex ID specific styles */
        .profile-content-wrapper {{
            display: flex;
            width: 100%;
            min-height: calc(100vh - 64px);
        }}

        .profile-main-content {{
            flex-grow: 1;
            padding: 24px;
            background-color: var(--id-color-surface-submerged);
        }}

        .Section_root__zl60G {{
            background: var(--id-color-surface-elevated-0);
            padding: 24px;
            margin-bottom: 6px;
            border-radius: var(--id-card-border-radius);
            width: 100%;
        }}

        .Section_inner__N7MeR {{
            max-width: 520px;
            margin: 0 auto;
        }}

        .Heading_root__P0ine {{
            margin-bottom: 16px;
        }}
        .Text_root__J8eOj {{
            display: block;
        }}

        .Text_root__J8eOj[data-variant=""heading-m""] {{
            font: var(--id-typography-heading-m);
        }}

        .Text_root__J8eOj[data-variant=""text-m""] {{
            font: var(--id-typography-text-m);
        }}

        .Text_root__J8eOj[data-variant=""text-s""] {{
            font: var(--id-typography-text-s);
        }}

        .Text_root__J8eOj[data-variant=""text-xs""] {{
            font: var(--id-typography-text-xs);
        }}

        .Text_root__J8eOj[data-color=""secondary""] {{
            color: var(--text-muted);
        }}

        .Text_root__J8eOj[data-color=""tertiary""] {{
            color: var(--text-muted-secondary, rgba(60, 60, 60, 0.7));
        }}

        [data-theme=""dark""] .Text_root__J8eOj[data-color=""tertiary""] {{
            color: var(--text-muted-secondary, rgba(200, 200, 200, 0.7));
        }}

        .UnstyledListItem_root__xsw4w {{
            padding: 12px 0;
        }}

        .UnstyledListItem_inner__Td3gb {{
            display: flex;
            justify-content: space-between;
            align-items: center;
        }}

        .Slot_root__jYlNI {{
            display: flex;
        }}

        .Slot_direction_vertical__I3MEt {{
            flex-direction: column;
        }}

        .Slot_direction_horizontal__aDFeG {{
            flex-direction: row;
        }}

        .Slot_content__XYDYF {{
            flex: 1;
        }}

        .alignment-center_root__ndulA {{
            align-items: center;
        }}

        .alignment-top_root____eiv {{
            align-items: flex-start;
        }}

        .Button_root__rneDS {{
            font-family: 'YS Text', 'Helvetica Neue', Arial, sans-serif;
            position: relative;
            display: inline-flex;
            align-items: center;
            justify-content: center;
            box-sizing: border-box;
            font-weight: 500;
            cursor: pointer;
            transition: 0.1s ease-out;
            text-decoration: none;
            border-radius: 8px;
        }}

        .text-button_root__doKoA {{
            background: transparent;
            color: var(--primary-color);
            border: none;
            padding: 0;
        }}

        .text-button_root__doKoA:hover {{
            color: var(--primary-dark);
            background: transparent;
        }}

        .size-m_root___r3aA {{
            font-size: 16px;
            line-height: 20px;
        }}

        .size-s_root__CoSn6 {{
            font-size: 14px;
            line-height: 18px;
        }}

        .variant-default_root__xWqkR {{
            background-color: var(--primary-color);
            color: white;
            border: none;
            padding: 13px 20px;
        }}

        .variant-default_root__xWqkR:hover {{
            background-color: var(--primary-dark);
        }}

        .size-l_root__PsIsm {{
            font-size: 18px;
            line-height: 22px;
            padding: 16px 24px;
        }}

        .user-avatar_root__CsKdB {{
            position: relative;
            display: inline-block;
            overflow: hidden;
            border-radius: 50%;
        }}

        .user-avatar_root_isBig__RozUb {{
            --id-avatar-size: 96px;
            width: var(--id-avatar-size);
            height: var(--id-avatar-size);
        }}

        .avatar_root__qDicj {{
            width: 100%;
            height: 100%;
            object-fit: cover;
        }}

        .profile-card_root__hJtgV {{
            display: flex;
            flex-direction: column;
            align-items: center;
            text-align: center;
        }}

        .profile-card_avatar__xb4bd {{
            margin-bottom: 8px;
        }}

        .profile-card_title__zZCrX {{
            font: var(--id-typography-heading-l);
            font-weight: 500;
            margin-bottom: 4px;
        }}

        .profile-card_description__nvlpy {{
            font: var(--id-typography-text-m);
        }}

        .bulleted-list_root__k0lgY {{
            padding: 0;
            margin: 0;
            list-style: none;
        }}

        .bulleted-list-item_root__1Y90C {{
            position: relative;
            padding-left: 0;
        }}

        .bulleted-list-item_root__1Y90C:not(:last-child)::after {{
            content: '•';
            margin: 0 6px;
            color: var(--text-muted);
        }}

        .bulleted-list-item_root__1Y90C:first-child {{
            padding-left: 0;
        }}

        .List_root__yESwN {{
            list-style: none;
            padding: 0;
            margin: 0;
        }}

        .unstyled-badge_root__1gOSr {{
            display: inline-flex;
            align-items: center;
            justify-content: center;
            padding: 4px 8px;
            border-radius: 4px;
            background-color: rgba(0, 0, 0, 0.05);
            margin: 2px;
        }}

        .sidebar-navigation_root__2HXQL {{
            padding: 16px 0;
        }}

        .sidebar-navigation_list__R_7Wh {{
            list-style: none;
            padding: 0;
            margin: 0;
        }}

        .sidebar-navigation_item__GvUUF {{
            margin-bottom: 4px;
        }}

        .base-item_root__Z_6ST {{
            display: flex;
            align-items: center;
            gap: 12px;
            padding: 12px 16px;
            color: var(--text-color);
            text-decoration: none;
            border-radius: 8px;
            margin: 0 8px;
        }}

        .base-item_root__Z_6ST:hover {{
            background-color: rgba(0, 0, 0, 0.05);
        }}

        .navigation-item-link_root_isActive__QZ9Ea {{
            background-color: rgba(252, 63, 29, 0.1);
            color: var(--primary-color);
        }}

        .svg-icon {{
            flex-shrink: 0;
        }}

        @media (max-width: 640px) {{
            .container {{
                margin: 1rem auto;
            }}
            
            .card {{
                border-radius: 0.5rem;
            }}
            
            .card-header, .card-body, .card-footer {{
                padding: 1rem;
            }}

            .sidebar {{
                width: 100%;
                border-right: none;
                border-bottom: 1px solid var(--border-color);
                padding: 0.75rem 0;
            }}

            .page-wrapper {{
                flex-direction: column;
            }}
            
            .profile-content-wrapper {{
                flex-direction: column;
            }}
            
            .profile-main-content {{
                padding: 16px;
            }}
            
            .Section_root__zl60G {{
                padding: 16px;
            }}
        }}

        .fade-in {{
            animation: fadeIn 0.3s ease-in-out;
        }}

        @keyframes fadeIn {{
            from {{ opacity: 0; transform: translateY(10px); }}
            to {{ opacity: 1; transform: translateY(0); }}
        }}

        .loader {{
            border: 2px solid rgba(252, 63, 29, 0.1);
            border-radius: 50%;
            border-top: 2px solid var(--primary-color);
            width: 24px;
            height: 24px;
            animation: spin 1s linear infinite;
            margin: 0 auto;
            display: inline-block;
        }}

        @keyframes spin {{
            0% {{ transform: rotate(0deg); }}
            100% {{ transform: rotate(360deg); }}
        }}

        /* Social login buttons */
        .social-buttons {{
            display: flex;
            justify-content: center;
            gap: 1rem;
            margin-top: 1.5rem;
        }}

        .social-button {{
            width: 40px;
            height: 40px;
            border-radius: 50%;
            display: flex;
            align-items: center;
            justify-content: center;
            background-color: rgba(255, 255, 255, 0.1);
            cursor: pointer;
            transition: background-color 0.15s ease;
        }}

        .social-button:hover {{
            background-color: rgba(255, 255, 255, 0.2);
        }}

        .divider {{
            display: flex;
            align-items: center;
            text-align: center;
            margin: 1.5rem 0;
        }}

        .divider::before,
        .divider::after {{
            content: '';
            flex: 1;
            border-bottom: 1px solid var(--border-color);
        }}

        .divider span {{
            padding: 0 0.75rem;
            color: var(--text-muted);
        }}

        /* Yandex specific elements */
        .yandex-id-header {{
            display: flex;
            align-items: center;
            justify-content: center;
            margin-bottom: 1.5rem;
        }}

        .yandex-id-header img {{
            height: 32px;
        }}

        .auth-footer {{
            text-align: center;
            margin-top: 1.5rem;
            font-size: 0.875rem;
            color: var(--text-muted);
        }}

        /* QR code login styling */
        .qr-login-container {{
            text-align: center;
        }}

        .qr-login-container .qr-code {{
            padding: 1rem;
            background-color: white;
            border-radius: 0.75rem;
            display: inline-flex;
        }}

        .secondary-option {{
            background-color: rgba(255, 255, 255, 0.1);
            color: white;
            border: none;
            border-radius: 0.5rem;
            padding: 0.75rem;
            margin-top: 1rem;
            cursor: pointer;
            transition: background-color 0.15s ease;
            width: 100%;
            text-align: center;
            display: flex;
            align-items: center;
            justify-content: center;
            gap: 0.5rem;
        }}

        .secondary-option:hover {{
            background-color: rgba(255, 255, 255, 0.15);
        }}
    </style>
</head>
<body class=""{2}"">
    <div class=""navbar"">
        <a href=""/"" class=""logo"">BRU AVTOPARK</a>
        <button class=""theme-toggle"" id=""themeToggle"" aria-label=""Toggle dark mode"">🌙</button>
    </div>
    {1}
    <script>
        // Theme toggling functionality
        const themeToggleBtn = document.getElementById('themeToggle');
        const prefersDarkScheme = window.matchMedia('(prefers-color-scheme: dark)');
        
        // Check for saved theme preference or use the system preference
        const currentTheme = localStorage.getItem('theme') || (prefersDarkScheme.matches ? 'dark' : 'light');
        
        // Set initial theme
        if (currentTheme === 'dark') {{
            document.body.setAttribute('data-theme', 'dark');
            themeToggleBtn.textContent = '☀️';
        }} else {{
            document.body.removeAttribute('data-theme');
            themeToggleBtn.textContent = '🌙';
        }}
        
        // Toggle theme when the button is clicked
        themeToggleBtn.addEventListener('click', function() {{
            let theme = 'light';
            
            if (!document.body.hasAttribute('data-theme')) {{
                document.body.setAttribute('data-theme', 'dark');
                themeToggleBtn.textContent = '☀️';
                theme = 'dark';
            }} else {{
                document.body.removeAttribute('data-theme');
                themeToggleBtn.textContent = '🌙';
            }}
            
            localStorage.setItem('theme', theme);
        }});
    </script>
</body>
</html>";

     
      

        // --- Login Form ---
        private string RenderLoginForm(string? error = null, string? message = null) => string.Format(BaseHtmlTemplate, "Login - BRU AVTOPARK", $@"
            <div class=""login-container fade-in"">
                <div class=""auth-card"">
                    <div class=""card-body"">
                        <div class=""yandex-id-header"">
                            <div style=""display: flex; align-items: center;"">
                                <div style=""width: 24px; height: 24px; background-color: var(--primary-color); border-radius: 4px; margin-right: 8px;""></div>
                                <span style=""color: white; font-weight: 500; font-size: 1.5rem;"">BRU ID</span>
                            </div>
                        </div>
                        
                        <h2 style=""text-align: center; margin-bottom: 1.5rem; color: white;"">Войдите с BRU ID</h2>
            
            {(error != null ? $@"<div class=""error-message"">{error}</div>" : "")}
            {(message != null ? $@"<div class=""success-message"">{message}</div>" : "")}
                        
            <form method=""POST"" action=""/api/auth/login"" id=""loginForm"">
                <div class=""form-group"">
                    <label for=""username"">Username</label>
                                <input type=""text"" id=""username"" name=""username"" required placeholder=""Enter your username"">
                </div>
                <div class=""form-group"">
                    <label for=""password"">Password</label>
                                <input type=""password"" id=""password"" name=""password"" required placeholder=""Enter your password"">
                </div>
                            <button type=""button"" onclick=""submitLoginForm()"" id=""loginButton"">Log in</button>
                            
                            <div class=""secondary-option"" style=""margin-top: 1rem;"" onclick=""window.location.href='/api/auth/webauthn/login'"">
                                <svg width=""20"" height=""20"" viewBox=""0 0 24 24"" fill=""none"" xmlns=""http://www.w3.org/2000/svg"">
                                    <path d=""M12 12.75C8.83 12.75 6.25 10.17 6.25 7C6.25 3.83 8.83 1.25 12 1.25C15.17 1.25 17.75 3.83 17.75 7C17.75 10.17 15.17 12.75 12 12.75ZM12 2.75C9.66 2.75 7.75 4.66 7.75 7C7.75 9.34 9.66 11.25 12 11.25C14.34 11.25 16.25 9.34 16.25 7C16.25 4.66 14.34 2.75 12 2.75Z"" fill=""white""/>
                                    <path d=""M20.5901 22.75C20.1801 22.75 19.8401 22.41 19.8401 22C19.8401 18.55 16.3601 15.75 12.0001 15.75C7.64008 15.75 4.16008 18.55 4.16008 22C4.16008 22.41 3.82008 22.75 3.41008 22.75C3.00008 22.75 2.66008 22.41 2.66008 22C2.66008 17.73 6.85008 14.25 12.0001 14.25C17.1501 14.25 21.3401 17.73 21.3401 22C21.3401 22.41 21.0001 22.75 20.5901 22.75Z"" fill=""white""/>
                                </svg>
                                <span>Login with Security Key</span>
                            </div>
                            
                            <div class=""secondary-option"" style=""margin-top: 0.5rem;"" onclick=""window.location.href='/api/auth/qr/login'"">
                                <svg width=""20"" height=""20"" viewBox=""0 0 24 24"" fill=""none"" xmlns=""http://www.w3.org/2000/svg"">
                                    <path d=""M3 3H9V9H3V3Z"" stroke=""white"" stroke-width=""2""/>
                                    <path d=""M15 3H21V9H15V3Z"" stroke=""white"" stroke-width=""2""/>
                                    <path d=""M3 15H9V21H3V15Z"" stroke=""white"" stroke-width=""2""/>
                                    <path d=""M15 15H21V21H15V15Z"" stroke=""white"" stroke-width=""2""/>
                                </svg>
                                <span>Login with QR Code</span>
                            </div>
            </form>
                        
            <div id=""statusDiv"" class=""mt-3""></div>
                        
                        <div class=""divider"">
                            <span>or</span>
                        </div>
                        
                        <div class=""social-buttons"">
                            <div class=""social-button"">
                                <svg width=""20"" height=""20"" viewBox=""0 0 20 20"" fill=""none"" xmlns=""http://www.w3.org/2000/svg"">
                                    <path d=""M10.0001 1.66667C5.40008 1.66667 1.66675 5.40001 1.66675 10C1.66675 14.6 5.40008 18.3333 10.0001 18.3333C14.6001 18.3333 18.3334 14.6 18.3334 10C18.3334 5.40001 14.6001 1.66667 10.0001 1.66667ZM13.2334 13.9C13.1334 14.0333 13.0001 14.1333 12.8667 14.1667C12.7334 14.2 12.6001 14.2 12.4667 14.1333C11.3334 13.5333 10.3001 12.7333 9.46675 11.7C8.73341 10.8333 8.13341 9.86667 7.70008 8.8C7.56675 8.46667 7.63341 8.1 7.90008 7.83334C8.00008 7.73334 8.10008 7.63334 8.23341 7.56667C8.53341 7.36667 8.90008 7.4 9.16675 7.66667C9.30008 7.8 9.40008 7.96667 9.50008 8.13334C9.66675 8.43334 9.60008 8.76667 9.36675 9.00001C9.30008 9.06667 9.26675 9.13334 9.20008 9.2C9.13341 9.26667 9.13341 9.36667 9.16675 9.46667C9.50008 10.2 10.0001 10.8 10.7001 11.2333C10.8001 11.3 10.9001 11.3333 11.0334 11.2667C11.2334 11.1667 11.3334 11 11.5001 10.9C11.8001 10.7 12.1334 10.7333 12.4001 10.9667C12.7001 11.2333 13.0001 11.5 13.2667 11.8C13.5001 12.1333 13.4667 12.5333 13.2334 12.8667V13.9Z"" fill=""white""/>
                                </svg>
                            </div>
                            <div class=""social-button"">
                                <svg width=""20"" height=""20"" viewBox=""0 0 20 20"" fill=""none"" xmlns=""http://www.w3.org/2000/svg"">
                                    <path d=""M18.1667 10.1828C18.1667 9.48283 18.1167 8.98283 18.0083 8.46616H10.3333V11.4662H14.8833C14.8 12.2328 14.3 13.3828 13.2417 14.1328L13.2267 14.2328L15.7083 16.1328L15.875 16.1495C17.3583 14.7995 18.1667 12.6828 18.1667 10.1828Z"" fill=""white""/>
                                    <path d=""M10.3332 18.3333C12.425 18.3333 14.1582 17.6833 15.4082 16.1333L12.7415 14.1333C12.0582 14.6333 11.1499 14.9833 10.3332 14.9833C8.19987 14.9833 6.40819 13.5833 5.84987 11.7H5.75404L3.16654 13.6833L3.13321 13.7833C4.37487 16.45 7.13321 18.3333 10.3332 18.3333Z"" fill=""white""/>
                                    <path d=""M5.85002 11.7C5.70002 11.1833 5.61669 10.6333 5.61669 10.0667C5.61669 9.5 5.70002 8.95 5.84169 8.43333L5.83669 8.32667L3.21669 6.31667L3.13335 6.35C2.55835 7.46667 2.23335 8.73333 2.23335 10.0667C2.23335 11.4 2.55835 12.6667 3.13335 13.7833L5.85002 11.7Z"" fill=""white""/>
                                    <path d=""M10.3332 5.15C11.6832 5.15 12.5999 5.75 13.1249 6.25L15.5082 3.95C14.1582 2.71667 12.4249 2 10.3332 2C7.13321 2 4.37487 3.88333 3.13321 6.55L5.84154 8.63333C6.40821 6.75 8.19987 5.15 10.3332 5.15Z"" fill=""white""/>
                                </svg>
                            </div>
                        </div>
                    </div>
                </div>
                
                <div class=""auth-footer"">
                    <div style=""margin-top: 2rem; display: flex; justify-content: center;"">
                        <a href=""/api/auth/register"" class=""link"" style=""color: white; margin: 0 0.5rem;"">Create account</a>
                        <span style=""color: #555;"">|</span>
                        <a href=""/api/auth/magic-link"" class=""link"" style=""color: white; margin: 0 0.5rem;"">Magic Link</a>
                        <span style=""color: #555;"">|</span>
                        <a href=""/api/auth/claim-account"" class=""link"" style=""color: white; margin: 0 0.5rem;"">Claim Account</a>
                    </div>
                    <div style=""margin-top: 1rem; color: #555;"">
                        BRU ID — ключ от всех сервисов
                    </div>
                </div>
            </div>
            <script>
                function submitLoginForm() {{
                    // Disable button to prevent multiple submissions
                    document.getElementById('loginButton').disabled = true;
                    document.getElementById('statusDiv').innerHTML = '<div class=""text-center""><div class=""loader""></div><p>Logging in...</p></div>';
                    
                    // Get form data
                    const username = document.getElementById('username').value;
                    const password = document.getElementById('password').value;
                    
                    // Submit form using fetch instead of form submission to avoid WebSocket issues
                    fetch('/api/auth/login', {{
                        method: 'POST',
                        headers: {{
                            'Content-Type': 'application/x-www-form-urlencoded',
                        }},
                        body: `username=${{encodeURIComponent(username)}}&password=${{encodeURIComponent(password)}}`,
                        credentials: 'same-origin'
                    }})
                    .then(response => {{
                        if (response.redirected) {{
                            // Follow redirect
                            window.location.href = response.url;
                            return;
                        }}
                        return response.json();
                    }})
                    .then(data => {{
                        if (data && data.success) {{
                            // Store token if present
                            if (data.data && data.data.token) {{
                                localStorage.setItem('auth_token', data.data.token);
                            }}
                            
                            // Show success message
                            document.getElementById('statusDiv').innerHTML = '<p class=""success-message"">Login successful! Redirecting...</p>';
                            
                            // Redirect to profile or success page
                            setTimeout(() => {{
                                window.location.href = '/api/auth/profile';
                            }}, 1000);
                        }} else if (data) {{
                            // Show error message
                            document.getElementById('statusDiv').innerHTML = `<p class=""error-message"">${{data.message || 'Login failed'}}</p>`;
                            document.getElementById('loginButton').disabled = false;
                        }}
                    }})
                    .catch(error => {{
                        console.error('Login error:', error);
                        document.getElementById('statusDiv').innerHTML = `<p class=""error-message"">Error: ${{error.message || 'Unknown error'}}</p>`;
                        document.getElementById('loginButton').disabled = false;
                    }});
                    
                    return false;
                }}
                
                // Allow form submission with Enter key
                document.getElementById('loginForm').addEventListener('keypress', function(event) {{
                    if (event.key === 'Enter') {{
                        event.preventDefault();
                        submitLoginForm();
                    }}
                }});
            </script>
        ", "auth-page-body");

        // --- OIDC Consent Form ---
        private string RenderConsentForm(OpenIddictRequest oidcRequest, string clientName, Dictionary<string, string> scopes)
        {
            var scopeListItems = scopes.Select(kvp =>
                 $"<li style='margin-bottom: 8px; display: flex; align-items: start; gap: 8px;'>" +
                 $"<svg width='20' height='20' viewBox='0 0 24 24' fill='none' xmlns='http://www.w3.org/2000/svg' style='margin-top: 2px; flex-shrink: 0; color: var(--success-color);'><path d='M12 22C17.5 22 22 17.5 22 12C22 6.5 17.5 2 12 2C6.5 2 2 6.5 2 12C2 17.5 6.5 22 12 22Z' stroke='currentColor' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'/><path d='M7.75 12L10.58 14.83L16.25 9.17' stroke='currentColor' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'/></svg>" +
                 $"<div><span style='font-weight: 500;'>{HttpUtility.HtmlEncode(FormatScopeName(kvp.Key))}</span><br/><span class='text-muted'>{HttpUtility.HtmlEncode(kvp.Value)}</span></div></li>"
             );

            // IMPORTANT: Include all original OIDC request parameters as hidden fields
            // This ensures the context is maintained when the form is submitted back to /connect/authorize
            var hiddenFields = string.Join("\n", oidcRequest.GetParameters()
                .Select(param => $"<input type=\"hidden\" name=\"{HttpUtility.HtmlAttributeEncode(param.Key)}\" value=\"{HttpUtility.HtmlAttributeEncode(param.Value.ToString())}\">"));

            return string.Format(BaseHtmlTemplate, $"Разрешение для {clientName}", $@"
                <div class=""container fade-in"" style=""max-width: 500px;"">
                    <div class=""card"">
                        <div class=""card-header text-center"">
                            <div class=""yandex-id-header"" style=""margin-bottom: 0.5rem;"">
                                 <div style=""display: flex; align-items: center;"">
                                     <div style=""width: 20px; height: 20px; background-color: var(--primary-color); border-radius: 4px; margin-right: 6px;""></div>
                                     <span style=""font-weight: 500; font-size: 1.25rem;"">BRU ID</span>
                                 </div>
                             </div>
                            <h1>Разрешить доступ?</h1>
                         </div>
                        <div class=""card-body"">
                            <p class=""text-center text-muted"" style=""margin-bottom: 1.5rem;"">Приложение <strong>{HttpUtility.HtmlEncode(clientName)}</strong> (<code class=""code-display"" style=""display: inline-block; padding: 2px 4px; margin: 0;"">{HttpUtility.HtmlEncode(oidcRequest.ClientId)}</code>) запрашивает доступ к вашему BRU ID.</p>

                             <h3 style=""margin-bottom: 0.75rem; font-weight: 500;"">Будет предоставлен доступ к:</h3>
                             <ul style='list-style: none; padding: 0; margin: 0; margin-bottom: 24px;'>
                                {string.Join("", scopeListItems)}
                             </ul>
                             <hr style='border: none; height: 1px; background-color: var(--border-color); margin: 1.5rem 0;'/>

                            <p class=""text-muted"" style=""font-size: 0.875rem;"">Предоставляя доступ, вы разрешаете этому приложению использовать указанную информацию в соответствии с его <a href=""#"" class=""link"" target=""_blank"" rel=""noopener noreferrer"">условиями использования</a> и <a href=""#"" class=""link"" target=""_blank"" rel=""noopener noreferrer"">политикой конфиденциальности</a>. Вы можете отозвать разрешение в любое время в настройках BRU ID.</p>

                            {/* Form POSTs back to the Authorize endpoint */}
                            <form method=""post"" action=""/connect/authorize"">
                                {hiddenFields} {/* Essential to pass back OIDC context */}
                                <div style=""display: flex; justify-content: space-between; gap: 16px; margin-top: 24px;"">
                                    <button type=""submit"" name=""submit.Accept"" value=""Accept"" class=""btn"" style=""flex-grow: 1;"">Разрешить</button>
                                    <button type=""submit"" name=""submit.Deny"" value=""Deny"" class=""btn btn-secondary"" style=""flex-grow: 1;"">Отклонить</button>
                                </div>
                            </form>
                        </div>
                    </div>
                </div>
            ", ""); // No special body class needed
        }

         // --- Logout Confirmation ---
         private string RenderLogoutConfirmation() => string.Format(BaseHtmlTemplate, "Выход выполнен", $@"
             <div class=""container fade-in"">
                 <div class=""card text-center"" style=""padding: 2rem;"">
                     <div class=""card-body"">
                          <svg width=""48"" height=""48"" viewBox=""0 0 24 24"" fill=""none"" xmlns=""http://www.w3.org/2000/svg"" style=""color: var(--success-color); margin-bottom: 1rem;""> <path d=""M12 22C17.5 22 22 17.5 22 12C22 6.5 17.5 2 12 2C6.5 2 2 6.5 2 12C2 17.5 6.5 22 12 22Z"" stroke=""currentColor"" stroke-width=""1.5"" stroke-linecap=""round"" stroke-linejoin=""round""/><path d=""M7.75 12L10.58 14.83L16.25 9.17"" stroke=""currentColor"" stroke-width=""1.5"" stroke-linecap=""round"" stroke-linejoin=""round""/></svg>
                          <h1 style=""font: var(--id-typography-heading-m); margin-bottom: 0.5rem;"">Вы успешно вышли</h1>
                          <p class=""text-muted"">Теперь вы можете безопасно закрыть эту вкладку.</p>
                          <div class=""mt-4"">
                               <a href=""/api/auth/login"" class=""btn"" style=""width: auto;"">Войти снова</a>
                          </div>
                     </div>
                 </div>
             </div>
             <script>localStorage.removeItem('auth_token'); sessionStorage.removeItem('auth_token');</script> {/* Clear legacy token */}
         ", ""); // No special body class

         // --- Error Page ---
         private string RenderErrorPage(string error, string? errorDescription = null) => string.Format(BaseHtmlTemplate, "Ошибка", $@"
             <div class=""container fade-in"">
                 <div class=""card text-center"" style=""border: 1px solid var(--error-color);"">
                     <div class=""card-header"" style=""background-color: rgba(239, 68, 68, 0.05);""><h1 style=""color: var(--error-color);"">Произошла ошибка</h1></div>
                     <div class=""card-body"">
                          <svg width=""40"" height=""40"" viewBox=""0 0 24 24"" fill=""none"" xmlns=""http://www.w3.org/2000/svg"" style=""color: var(--error-color); margin-bottom: 1rem;""><path d=""M12 22C17.5 22 22 17.5 22 12C22 6.5 17.5 2 12 2C6.5 2 2 6.5 2 12C2 17.5 6.5 22 12 22Z"" stroke=""currentColor"" stroke-width=""1.5"" stroke-linecap=""round"" stroke-linejoin=""round""/><path d=""M12 8V13"" stroke=""currentColor"" stroke-width=""1.5"" stroke-linecap=""round"" stroke-linejoin=""round""/><path d=""M11.9945 16H12.0035"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round""/></svg>
                         <p style=""font-weight: 500; color: var(--text-color); margin-bottom: 0.5rem;"">{HttpUtility.HtmlEncode(error)}</p>
                         {(errorDescription != null ? $"<p class='text-muted' style='margin-top: 0;'>{HttpUtility.HtmlEncode(errorDescription)}</p>" : "")}
                         <div class=""mt-4"">
                             <a href=""/api/auth/login"" class=""btn btn-secondary"" style=""width: auto;"">Вернуться на страницу входа</a>
                         </div>
                     </div>
                 </div>
             </div>
         ", "");

        // --- Success Page (Generic Redirector) ---
       
        private string RenderSuccessPage(string token) => string.Format(BaseHtmlTemplate, "Login Successful - BRU AVTOPARK", $@"
            <div class=""success-message"" style=""display: flex; align-items: center; justify-content: center; gap: 10px;"">
                <svg width=""24"" height=""24"" viewBox=""0 0 24 24"" fill=""none"" xmlns=""http://www.w3.org/2000/svg"">
                    <path d=""M12 22C17.5228 22 22 17.5228 22 12C22 6.47715 17.5228 2 12 2C6.47715 2 2 6.47715 2 12C2 17.5228 6.47715 22 12 22Z"" stroke=""var(--success-color)"" stroke-width=""2""/>
                    <path d=""M8 12L11 15L16 9"" stroke=""var(--success-color)"" stroke-width=""2""/>
                </svg>
                <span>You have been successfully logged in!</span>
        </div>
                    <div class=""text-center mt-4"">
                        <a href=""/api/auth/profile"" class=""btn"">Go to Profile</a>
        </div>
    <script>
                // Store the token securely
                const token = '{token.Replace("'", "\\'")}';
                if (token && token !== 'null') {{
                    localStorage.setItem('auth_token', token);
                    
                    // Set up authorization header for future API requests
                    const authHeader = `Bearer ${{token}}`;
                    
                    // Automatically redirect to profile page after a short delay
                    setTimeout(() => {{
                        const profileUrl = '/api/auth/profile';
                        // Use fetch with the auth token to check if we can access the profile
                        fetch(profileUrl, {{
                            headers: {{
                                'Authorization': authHeader
                            }}
                        }})
                        .then(response => {{
                            if (response.ok) {{
                                window.location.href = profileUrl;
                            }} else {{
                                throw new Error('Failed to verify profile access');
                            }}
                        }})
                        .catch(error => {{
                            console.error('Error:', error);
                            // If there's an error, try redirecting with the token as a query parameter
                            window.location.href = `${{profileUrl}}?token=${{encodeURIComponent(token)}}`;
                        }});
                    }}, 1500);
                }}
            </script>
        ", "");

    -
        // --- Profile Page ---
       private string RenderProfilePage(UserProfile user, bool totpEnabled, List<WebAuthnCredentialDto> webAuthnCredentials, List<Role> roles, List<Permission> permissions)
        {
            // Determine if user is admin based on roles
            bool isAdmin = roles.Any(r => r.LegacyRoleId == 1); // Assuming legacy Role ID 1 is Admin

            // Helper function to generate list items for security section
            // Using standard classes like UnstyledListItem_root__xsw4w, Slot_root__jYlNI etc. from Yandex CSS
            string RenderSecurityListItem(string title, string description, string actionHtml) => $@"
                 <div class=""UnstyledListItem_root__xsw4w variant-default_root__vj_1h"" style=""padding: 12px 0;"">
                     <div class=""UnstyledListItem_inner__Td3gb"">
                         <div class=""Slot_root__jYlNI Slot_direction_vertical__I3MEt Slot_content__XYDYF color-primary_root__olFUv alignment-center_root__ndulA"">
                             <span class=""Text_root__J8eOj"" data-variant=""text-m"">{System.Web.HttpUtility.HtmlEncode(title)}</span>
                             <span class=""Text_root__J8eOj"" data-variant=""text-s"" data-color=""secondary"">{System.Web.HttpUtility.HtmlEncode(description)}</span>
                </div>
                         <div class=""Slot_root__jYlNI Slot_direction_horizontal__aDFeG Slot_after____mkr color-primary_root__olFUv alignment-center_root__ndulA"">
                             {actionHtml} 
                    </div>
                    </div>
                    </div>
                 <hr style='border: none; height: 1px; background-color: var(--id-color-line-normal); margin: 0;'/>";
             // Helper function to render WebAuthn Key List Item
            string RenderWebAuthnKeyItem(WebAuthnCredentialDto c) => $@"
                 <div class=""UnstyledListItem_root__xsw4w variant-default_root__vj_1h"" style=""padding: 12px 0;"">
                    <div class=""UnstyledListItem_inner__Td3gb"">
                        <div class=""Slot_root__jYlNI Slot_direction_vertical__I3MEt Slot_content__XYDYF color-primary_root__olFUv alignment-center_root__ndulA"">
                             <span class=""Text_root__J8eOj"" data-variant=""text-m"">Ключ безопасности</span>
                             <span class=""Text_root__J8eOj"" data-variant=""text-s"" data-color=""secondary"">Добавлен: {c.CreatedAt:dd.MM.yyyy}</span>
                             <span class=""Text_root__J8eOj"" data-variant=""text-xs"" data-color=""tertiary"" style=""word-break: break-all;"">ID: {c.Id.Substring(0, Math.Min(12, c.Id.Length))}...</span> 
                </div>
                         <div class=""Slot_root__jYlNI Slot_direction_horizontal__aDFeG Slot_after____mkr color-primary_root__olFUv alignment-center_root__ndulA"">
                             <form method=""POST"" action=""/api/auth/webauthn/credentials/{System.Web.HttpUtility.UrlEncode(c.Id)}"" onsubmit=""return confirm('Удалить этот ключ безопасности?');"">
                                 <input type=""hidden"" name=""_method"" value=""DELETE"">
            
                                 <button type=""submit"" class=""Button_root__rneDS text-button_root__doKoA size-s_root__CoSn6"" style=""color: var(--id-color-status-negative);"">Удалить</button>
                             </form>
                </div>
                                    </div>
                                    </div>
                 <hr style='border: none; height: 1px; background-color: var(--id-color-line-normal); margin: 0;'/>";


            return string.Format(BaseHtmlTemplate, "Ваш профиль - BRU AVTOPARK", $@"
            <div class=""profile-content-wrapper"" style=""display: flex;"">
                 {RenderSidebar()} 
                 <main class=""profile-main-content"" style=""flex-grow: 1; padding: 24px; background-color: var(--id-color-surface-submerged);"">
                     
                     <section class=""Section_root__zl60G"" style=""background: var(--id-color-surface-elevated-0); padding: 24px; margin-bottom: 6px; border-radius: var(--id-card-border-radius);"">
                         <div class=""Section_inner__N7MeR"" style=""max-width: 520px; gap: 8px;"">
                             <div class=""profile-card_root__hJtgV profile-card_root_isResponsive__FJgqS"" data-testid=""profile-card"">
                                 <div class=""profile-card_avatar__xb4bd"" style=""margin-bottom: 8px;"">
                                     <div class=""user-avatar_root__CsKdB user-avatar_root_isBig__RozUb"" style=""--id-avatar-size: 96px;"" data-testid=""user-avatar"">
                                         
                                          <img data-testid=""avatar"" aria-hidden=""true"" class=""avatar_root__qDicj user-avatar_avatar__jSrtG"" src=""https://avatars.mds.yandex.net/get-yapic/0/0-0/islands-200"" alt=""Аватар пользователя"" style=""background-color: var(--id-color-default-bg-base);""/>
                                </div>
                            </div>
                                 
                                 <span class=""Text_root__J8eOj profile-card_title__zZCrX"" style=""font: var(--id-typography-heading-l); font-weight: 500;"" data-testid=""profile-card-menu-trigger"" data-color=""primary"">
                                     {System.Web.HttpUtility.HtmlEncode(user.Login)}
                                   
                                 </span>
                                 
                                 <span class=""Text_root__J8eOj profile-card_description__nvlpy"" data-testid=""profile-card-description"" data-color=""secondary"" style=""font: var(--id-typography-text-m); text-align: center;"">
                                     <ul class=""bulleted-list_root__k0lgY"" style=""padding: 0; margin: 0;"">
                                         {(!string.IsNullOrEmpty(user.PhoneNumber) ? $@"<li class=""Text_root__J8eOj bulleted-list-item_root__1Y90C"" data-color=""secondary""><bdi data-testid=""phone"">{System.Web.HttpUtility.HtmlEncode(user.PhoneNumber)}</bdi></li>" : "")}
                                         {(!string.IsNullOrEmpty(user.Email) ? $@"<li class=""Text_root__J8eOj bulleted-list-item_root__1Y90C"" data-color=""secondary""><bdi data-testid=""email"">{System.Web.HttpUtility.HtmlEncode(user.Email)}</bdi></li>" : "")}
                                         {(string.IsNullOrEmpty(user.PhoneNumber) && string.IsNullOrEmpty(user.Email) ? $@"<li class=""Text_root__J8eOj bulleted-list-item_root__1Y90C"" data-color=""secondary""><bdi>Контактные данные не указаны</bdi></li>" : "")}
                                     </ul>
                                 </span>
                                    </div>
                            </div>
                     </section>

                     
                     <section class=""Section_root__zl60G"" style=""background: var(--id-color-surface-elevated-0); padding: 24px; margin-bottom: 6px; border-radius: var(--id-card-border-radius);"">
                         <div class=""Section_inner__N7MeR"" style=""max-width: 520px;"">
                             <div class=""Heading_root__P0ine Heading_variant_section__p8T1h""><h2 class=""Text_root__J8eOj"" data-variant=""heading-m"">Безопасность</h2></div>
                            
                             <div class=""List_root__yESwN list-style-plain_root__EX_j_"">
                                {RenderSecurityListItem(
                                    "Двухфакторная аутентификация",
                                    totpEnabled ? "Включена • Код из приложения" : "Выключена",
                                    totpEnabled
                                        ? $@"<form method='POST' action='/api/auth/totp/disable' style='display:inline-block;'><button type='submit' class='Button_root__rneDS text-button_root__doKoA size-m_root___r3aA'>Выключить</button></form>"
                                        : $@"<a href='/api/auth/totp/setup' class='Button_root__rneDS text-button_root__doKoA size-m_root___r3aA'>Включить</a>"
                                )}
                                {RenderSecurityListItem(
                                    "Ключи и биометрия",
                                     webAuthnCredentials.Count > 0 ? $"{webAuthnCredentials.Count} {GetNoun(webAuthnCredentials.Count, "ключ", "ключа", "ключей")}" : "Нет ключей",
                                     $@"<a href='/api/auth/webauthn/register/options' class='Button_root__rneDS text-button_root__doKoA size-m_root___r3aA'>{(webAuthnCredentials.Count > 0 ? "Управлять" : "Добавить")}</a>"
                                )}
                                 
                                 {(webAuthnCredentials.Count > 0 ? $@"
                                     <div style='padding: 0; margin-top: -8px;'> 
                                         {string.Join("", webAuthnCredentials.Select(RenderWebAuthnKeyItem))}
                </div>
                                     " : "")}
                        </div>
                        </div>
                     </section>

                   
                     <section class=""Section_root__zl60G"" style=""background: var(--id-color-surface-elevated-0); padding: 24px; margin-bottom: 6px; border-radius: var(--id-card-border-radius);"">
                         <div class=""Section_inner__N7MeR"" style=""max-width: 520px;"">
                             <div class=""Heading_root__P0ine Heading_variant_section__p8T1h""><h2 class=""Text_root__J8eOj"" data-variant=""heading-m"">Роли и права</h2></div>
                              <div class=""List_root__yESwN list-style-plain_root__EX_j_"">
                                  <div class=""UnstyledListItem_root__xsw4w variant-default_root__vj_1h"" style=""padding: 12px 0;"">
                                      <div class=""UnstyledListItem_inner__Td3gb"">
                                          <div class=""Slot_root__jYlNI Slot_direction_vertical__I3MEt Slot_content__XYDYF color-primary_root__olFUv alignment-top_root____eiv"">
                                              <span class=""Text_root__J8eOj"" data-variant=""text-m"" style=""margin-bottom: 4px;"">Роли</span>
                                              {(roles.Any() ?
                                                  $@"<span class=""Text_root__J8eOj"" data-variant=""text-s"" data-color=""secondary"">{string.Join(", ", roles.Select(r => System.Web.HttpUtility.HtmlEncode(r.Name)))}</span>"
                                                  : $@"<span class=""Text_root__J8eOj"" data-variant=""text-s"" data-color=""secondary"">Роли не назначены</span>"
                                              )}
                                        </div>
                                    </div>
                            </div>
                                   <hr style='border: none; height: 1px; background-color: var(--id-color-line-normal); margin: 0;'/>
                                   <div class=""UnstyledListItem_root__xsw4w variant-default_root__vj_1h"" style=""padding: 12px 0;"">
                                      <div class=""UnstyledListItem_inner__Td3gb"">
                                          <div class=""Slot_root__jYlNI Slot_direction_vertical__I3MEt Slot_content__XYDYF color-primary_root__olFUv alignment-top_root____eiv"">
                                              <span class=""Text_root__J8eOj"" data-variant=""text-m"" style=""margin-bottom: 8px;"">Права</span>
                                              {(permissions.Any() ? $@"
                                                  <div class=""flex flex-wrap gap-2"">
                                                      {string.Join("", permissions.Select(permission => $@"
                                                          <span class=""unstyled-badge_root__1gOSr size-m_root__FIFoL variant-default_root__nV3qv"" style='font-weight: 400;'>{System.Web.HttpUtility.HtmlEncode(permission.Name)}</span>
                                                      "))}
                        </div>" :
                                                  @"<span class=""Text_root__J8eOj"" data-variant=""text-s"" data-color=""secondary"">Специальные права отсутствуют</span>"
                    )}
                    </div>
                </div>
            </div>
                </div>
                        </div>
                     </section>

                     
                      {(isAdmin ? $@"
                         <section class=""Section_root__zl60G"" style=""background: var(--id-color-surface-elevated-0); padding: 24px; margin-bottom: 6px; border-radius: var(--id-card-border-radius);"">
                             <div class=""Section_inner__N7MeR"" style=""max-width: 520px;"">
                                 <div class=""Heading_root__P0ine Heading_variant_section__p8T1h""><h2 class=""Text_root__J8eOj"" data-variant=""heading-m"">Администрирование</h2></div>
                                 <div class=""List_root__yESwN list-style-plain_root__EX_j_"" style=""display: flex; gap: 16px; flex-wrap: wrap; margin-top: 16px;"">
                                 
                                      <a href=""/api/auth/register"" class=""UnstyledListItem_root__xsw4w"" style=""padding: 20px; background: var(--id-color-surface-elevated-1); border-radius: 12px; width: calc(50% - 8px); transition: transform 0.2s, box-shadow 0.2s; box-shadow: 0 2px 8px rgba(0,0,0,0.05); display: flex; flex-direction: column; align-items: center; text-align: center; text-decoration: none; color: inherit; border: 1px solid var(--id-color-line-subtle);"" onmouseover=""this.style.transform='translateY(-4px)'; this.style.boxShadow='0 6px 12px rgba(0,0,0,0.1)';"" onmouseout=""this.style.transform=''; this.style.boxShadow='0 2px 8px rgba(0,0,0,0.05)';"">
                                           <div style=""width: 48px; height: 48px; background: var(--id-color-accent-subtle); border-radius: 50%; display: flex; align-items: center; justify-content: center; margin-bottom: 12px;"">
                                               <svg width=""24"" height=""24"" fill=""var(--id-color-accent)"" viewBox=""0 0 24 24"" aria-hidden=""true"" focusable=""false"" role=""img"" class=""svg-icon"">
                                                   <path d=""M12 4a4 4 0 1 0 0 8 4 4 0 0 0 0-8zM6 8a6 6 0 1 1 12 0A6 6 0 0 1 6 8zm2 10a3 3 0 0 0-3 3 1 1 0 1 1-2 0 5 5 0 0 1 5-5h8a5 5 0 0 1 5 5 1 1 0 1 1-2 0 3 3 0 0 0-3-3H8z""></path>
                                                   <path d=""M17 8a1 1 0 0 1 1-1h2a1 1 0 1 1 0 2h-2a1 1 0 0 1-1-1zm1-5a1 1 0 1 0 0 2h2a1 1 0 1 0 0-2h-2z""></path>
                                               </svg>
                        </div>
                                           <span class=""Text_root__J8eOj"" data-variant=""text-m"" style=""font-weight: 500; color: var(--id-color-text-primary);"">Зарегистрировать пользователя</span>
                                           <span class=""Text_root__J8eOj"" data-variant=""text-s"" style=""color: var(--id-color-text-secondary); margin-top: 4px;"">Создание новых учетных записей</span>
                                       </a>
                                     
                                      <a href=""/api/auth/connect/clients"" class=""UnstyledListItem_root__xsw4w"" style=""padding: 20px; background: var(--id-color-surface-elevated-1); border-radius: 12px; width: calc(50% - 8px); transition: transform 0.2s, box-shadow 0.2s; box-shadow: 0 2px 8px rgba(0,0,0,0.05); display: flex; flex-direction: column; align-items: center; text-align: center; text-decoration: none; color: inherit; border: 1px solid var(--id-color-line-subtle);"" onmouseover=""this.style.transform='translateY(-4px)'; this.style.boxShadow='0 6px 12px rgba(0,0,0,0.1)';"" onmouseout=""this.style.transform=''; this.style.boxShadow='0 2px 8px rgba(0,0,0,0.05)';"">
                                           <div style=""width: 48px; height: 48px; background: var(--id-color-accent-subtle); border-radius: 50%; display: flex; align-items: center; justify-content: center; margin-bottom: 12px;"">
                                               <svg width=""24"" height=""24"" fill=""var(--id-color-accent)"" viewBox=""0 0 24 24"" aria-hidden=""true"" focusable=""false"" role=""img"" class=""svg-icon"">
                                                   <path d=""M12.476 1.748c.237.11 1.304.602 2.737 1.183 1.645.668 3.74 1.441 5.614 1.904a1 1 0 0 1 .76.97c0 4.608-.842 8.201-2.45 10.88-1.623 2.704-3.981 4.4-6.846 5.272-.19.057-.392.057-.582 0-2.865-.872-5.224-2.568-6.846-5.271-1.608-2.68-2.45-6.273-2.45-10.88a1 1 0 0 1 .76-.97c1.874-.464 3.969-1.237 5.615-1.905a63 63 0 0 0 2.736-1.183c.312-.146.638-.147.952 0zM12 8a2 2 0 1 0 0 4 2 2 0 0 0 0-4zm-4 2a4 4 0 1 1 8 0 4 4 0 0 1-8 0z""></path>
                                               </svg>
                        </div>
                                           <span class=""Text_root__J8eOj"" data-variant=""text-m"" style=""font-weight: 500; color: var(--id-color-text-primary);"">Управление OIDC клиентами</span>
                                           <span class=""Text_root__J8eOj"" data-variant=""text-s"" style=""color: var(--id-color-text-secondary); margin-top: 4px;"">Настройка OAuth клиентов</span>
                                       </a>
                        </div>
                    </div>
                         </section>
                      " : "")}

                     
                     <div class=""text-center"" style=""margin-top: 32px;"">
                         <a href=""/api/auth/logout"" class=""Button_root__rneDS variant-default_root__xWqkR size-l_root__PsIsm"" style=""min-width: 160px;"">Выйти</a>
            </div>
            
                 </main>
            </div>
         ", "");
        }

        // --- Registration Form ---
        private string RenderRegisterForm(string? error = null, string? message = null, int? adminCheckAttempt = null, bool isAdmin = false) => string.Format(BaseHtmlTemplate,
            "Регистрация - BRU AVTOPARK", // {0} - title
            $@"
            <div class=""login-container fade-in"" style=""max-width: 1200px; margin: 0 auto; padding: 20px;""> 
                <div class=""auth-card"" style=""width: 100%; max-width: none;""> 
                    <div class=""card-body"">
                        <div class=""yandex-id-header"" style=""margin-bottom: 20px;""> 
                            <div style=""display: flex; align-items: center;"">
                                <div style=""width: 24px; height: 24px; background-color: var(--primary-color); border-radius: 4px; margin-right: 8px;""></div>
                                <span style=""color: white; font-weight: 500; font-size: 1.5rem;"">BRU ID</span>
                            </div>
                        </div>

                        <h2 style=""text-align: center; margin-bottom: 1.5rem; color: white;"">Создание аккаунта</h2>

                        {(!string.IsNullOrEmpty(error) ? $@"<div class='error-message' style='background-color: rgba(239, 68, 68, 0.15); color: var(--error-color);'>{System.Web.HttpUtility.HtmlEncode(error)}</div>" : "")}
                        {(!string.IsNullOrEmpty(message) ? $@"<div class='success-message' style='background-color: rgba(16, 185, 129, 0.15); color: var(--success-color);'>{System.Web.HttpUtility.HtmlEncode(message)}</div>" : "")}

                        <div style=""display: flex; flex-direction: row; gap: 20px; flex-wrap: wrap;"">
                            <div style=""flex: 1; min-width: 300px;"">
                                <div class=""info-box"" style=""background-color: rgba(255, 255, 255, 0.05); color: var(--text-muted); border: 1px solid rgba(255,255,255,0.1); margin-bottom: 20px;"">
                                    <strong>Примечание:</strong> Только администраторы могут регистрировать новых пользователей. Требуется вход с правами администратора.
                                </div>

                                <div id=""admin-check-status"" style=""display: {(isAdmin ? "block" : "none")}; background-color: rgba(16, 185, 129, 0.15); color: var(--success-color); padding: 0.75rem; border-radius: 0.5rem; margin-bottom: 1rem; font-size: 0.875rem;"">
                                    <strong>Вы вошли как администратор.</strong>
                                </div>
                                <div id=""admin-check-pending"" style=""display: {(isAdmin ? "none" : (adminCheckAttempt.HasValue ? "block" : "none"))}; background-color: rgba(245, 158, 11, 0.15); color: var(--warning-color); padding: 0.75rem; border-radius: 0.5rem; margin-bottom: 1rem; font-size: 0.875rem;"">
                                     <strong>Проверка прав администратора... (Попытка {adminCheckAttempt ?? 0}/3)</strong>
                                     <p style='margin-top: 8px; color: var(--warning-color);'>Если проверка не удалась, <a href='/api/auth/login?returnUrl=/api/auth/register' class='link' style='color: #76a6f5;'>войдите снова</a>.</p>
                                </div>
                                <div id=""register-status"" class=""my-4""></div> 
                            </div>

                            <div style=""flex: 2; min-width: 300px;"">
                                <form id=""registerForm"" style=""display: {(isAdmin ? "block" : "none")};"">
                                    <div style=""display: flex; flex-wrap: wrap; gap: 15px;"">
                                        <div class=""form-group"" style=""flex: 1; min-width: 250px;"">
                                            <label for=""username"">Придумайте логин</label>
                                            <input type=""text"" id=""username"" name=""username"" required>
                                        </div>

                                        <div class=""form-group"" style=""flex: 1; min-width: 250px;"">
                                            <label for=""password"">Создайте пароль</label>
                                            <input type=""password"" id=""password"" name=""password"" required>
                                        </div>
                                    </div>

                                    <div style=""display: flex; flex-wrap: wrap; gap: 15px;"">
                                        <div class=""form-group"" style=""flex: 1; min-width: 250px;"">
                                            <label for=""email"">Email (необязательно)</label>
                                            <input type=""email"" id=""email"" name=""email"" placeholder=""example@example.com"">
                                        </div>

                                        <div class=""form-group"" style=""flex: 1; min-width: 250px;"">
                                            <label for=""phoneNumber"">Номер телефона (необязательно)</label>
                                            <input type=""tel"" id=""phoneNumber"" name=""phoneNumber"" placeholder=""+375 XX XXX-XX-XX"">
                                        </div>
                                    </div>

                                    <div class=""form-group"">
                                        <label for=""role"">Роль</label>
                                        <select id=""role"" name=""role"" required style=""background-color: rgba(255, 255, 255, 0.1); color: white; border: none; padding: 0.75rem;"">
                                            <option value=""0"" style=""color: black; background-color: white;"">Пользователь</option>
                                            <option value=""1"" style=""color: black; background-color: white;"">Администратор</option>
                                            <option value=""2"" style=""color: black; background-color: white;"">Менеджер</option>
                                            <option value=""3"" style=""color: black; background-color: white;"">Водитель</option>
                                            <option value=""4"" style=""color: black; background-color: white;"">Кондуктор</option>
                                            <option value=""5"" style=""color: black; background-color: white;"">Диспетчер</option>
                                        </select>
                                    </div>
                                    <div class=""text-center"">
                                        <button type=""submit"" class=""btn"" style=""width: auto; min-width: 200px; margin: 0 auto;"">Зарегистрировать</button>
                                    </div>
                                    <div class=""text-center"" style=""margin-top: 1.5rem;"">
                                        <a href=""/api/auth/profile"" class=""link"" style=""color: #76a6f5;"">Назад в профиль</a>
                                    </div>
                                </form>
                            </div>
                        </div>
                    </div>
                </div>
            </div>

           
            <script>
                 // Admin check logic (runs immediately)
                 const isAdmin = {isAdmin.ToString().ToLowerInvariant()};
                 if (isAdmin) {{
                     document.getElementById('registerForm').style.display = 'block';
                 }} else {{
                      const attempt = {adminCheckAttempt ?? 0};
                      if (attempt > 0 && attempt < 3) {{
                         // Keep the pending message visible
                     }} else if (attempt >= 3 && !isAdmin) {{
                          // Optionally redirect or show a final error if max attempts reached
                         document.getElementById('admin-check-pending').innerHTML = `<div class='error-message' style='background-color: rgba(239, 68, 68, 0.15); color: var(--error-color);'>Недостаточно прав для регистрации. Пожалуйста, войдите как администратор. <a href='/api/auth/login?returnUrl=/api/auth/register' class='link' style='color: #76a6f5;'>Войти</a></div>`;
                     }} else if (!isAdmin && !attempt) {{
                         // First load, no admin rights, hide form (already done via style)
                         // Admin check pending message is shown by default if attempts start
                     }}
                 }}

                 // Form submission logic
                 document.getElementById('registerForm')?.addEventListener('submit', async (e) => {{ // Add null check for safety
                     e.preventDefault();
                     const statusElement = document.getElementById('register-status');
                     statusElement.innerHTML = '<div class=""text-center"" style=""color: var(--text-muted);""><div class=""loader""></div><p style=""color: inherit; margin-top: 8px;"">Регистрация...</p></div>'; // Use text-muted for loading text

                     const formData = {{
                         username: document.getElementById('username').value,
                         password: document.getElementById('password').value,
                         email: document.getElementById('email').value || null,
                         phoneNumber: document.getElementById('phoneNumber').value || null,
                         role: parseInt(document.getElementById('role').value)
                     }};

                     try {{
                         const token = localStorage.getItem('auth_token');
                         if (!token) {{
                             statusElement.innerHTML = '<div class=""error-message"" style=""background-color: rgba(239, 68, 68, 0.15); color: var(--error-color);"">Ошибка аутентификации администратора.</div>';
                             setTimeout(() => {{ window.location.href = '/api/auth/login?returnUrl=/api/auth/register'; }}, 2000);
                             return;
                         }}

                         const response = await fetch('/api/auth/register', {{
                             method: 'POST',
                             headers: {{ 'Content-Type': 'application/json', 'Authorization': 'Bearer ' + token }},
                             body: JSON.stringify(formData)
                         }});

                         const result = await response.json(); // Assume response is always JSON

                         if (response.ok && result.success) {{
                             statusElement.innerHTML = '<div class=""success-message"" style=""background-color: rgba(16, 185, 129, 0.15); color: var(--success-color);"">Пользователь ' + formData.username + ' успешно зарегистрирован!</div>';
                             document.getElementById('registerForm').reset(); // Clear form on success
                         }} else {{
                             const errorMsg = result.message || 'Ошибка регистрации';
                             statusElement.innerHTML = '<div class=""error-message"" style=""background-color: rgba(239, 68, 68, 0.15); color: var(--error-color);"">' + errorMsg + '</div>';
                         }}
                     }} catch (error) {{
                         console.error('Registration error:', error);
                         statusElement.innerHTML = '<div class=""error-message"" style=""background-color: rgba(239, 68, 68, 0.15); color: var(--error-color);"">Произошла ошибка при регистрации. Попробуйте снова.</div>';
                     }}
                 }});
            </script>
            ",
             "" // {2} - additional body classes for background image etc.
        );
        }

        // --- Claim Account Form ---
        private string RenderClaimAccountForm(string? error = null, string? message = null)
        {
            string antiForgeryTokenField = ""; // Add if using antiforgery
            return string.Format(BaseHtmlTemplate, "Активация аккаунта - BRU AVTOPARK", $@"
                <div class=""login-container fade-in"">
                    <div class=""auth-card"">
                        <div class=""card-body"">
                            <div class=""yandex-id-header""> {/* Header */} </div>
                            <h2 style=""text-align: center; margin-bottom: 1.5rem; color: white;"">Активация аккаунта</h2>

                            {(!string.IsNullOrEmpty(error) ? $"<div class='error-message'>{HttpUtility.HtmlEncode(error)}</div>" : "")}
                            {(!string.IsNullOrEmpty(message) ? $"<div class='success-message'>{HttpUtility.HtmlEncode(message)}</div>" : "")}
                            <div id=""statusDiv"" class=""my-2""></div>

                            <div class=""info-box"" style=""background-color: rgba(255, 255, 255, 0.1); color: #b3b3b3; margin-bottom: 1.5rem;"">
                                <p style=""color: inherit;"">Если у вас есть неактивный аккаунт (например, созданный администратором), вы можете активировать его здесь, используя логин и пароль.</p>
                            </div>

                            <form method=""POST"" action=""/api/auth/claim-account"" id=""claimForm"">
                                {antiForgeryTokenField}
                                <div class=""form-group"">
                                    <label for=""username"">Логин</label>
                                    <input type=""text"" id=""username"" name=""username"" required placeholder=""Введите ваш логин"">
                                </div>
                                <div class=""form-group"">
                                    <label for=""password"">Пароль</label>
                                    <input type=""password"" id=""password"" name=""password"" required placeholder=""Введите ваш пароль"">
                                </div>
                                <div class=""form-group"" style=""display: none;""> {/* Hidden, assuming default is true */}
                                     <input type=""checkbox"" id=""generateNewIdentity"" name=""generateNewIdentity"" value=""true"" checked>
                                </div>
                                <button type=""submit"" id=""claimButton"">Активировать</button>
                            </form>

                            <div class=""divider""><span>или</span></div>
                            <div style=""text-align: center; margin-top: 1rem;"">
                                <a href=""/api/auth/login"" class=""link"" style=""color: white;"">Вернуться ко входу</a>
                            </div>
                        </div>
                    </div>
                    <div class=""auth-footer"">
                         <div style=""margin-top: 1rem; color: #555;""> BRU ID — ключ от всех сервисов </div>
                    </div>
                </div>
                {/* Optional: Add JS for AJAX submission if preferred over full page reload */}
            ", "auth-page-body");
        }


        // --- TOTP Setup Page ---
        private string RenderTotpSetup(string qrCodeUri, string secretKey)
        {
            string antiForgeryTokenField = ""; // Add if using antiforgery
             return string.Format(BaseHtmlTemplate, "Настройка 2FA", $@"
             <div class=""container fade-in"">
                 <div class=""card"">
                     <div class=""card-header""><h1 style=""font: var(--id-typography-heading-m);"">Настройка двухфакторной аутентификации</h1></div>
                     <div class=""card-body"">
                         <div class=""info-box""> <p>Для повышения безопасности аккаунта настройте двухфакторную аутентификацию. Отсканируйте QR-код с помощью приложения-аутентификатора (например, Google Authenticator, Authy или Microsoft Authenticator).</p> </div>
                         <div class=""qr-code""> <img src=""{qrCodeUri}"" alt=""TOTP QR Code""> </div>
                         <div class=""text-center my-4"">
                              <p class=""text-muted"">Не можете отсканировать? Введите этот код вручную:</p>
                              <div class=""code-display text-center"" style=""font-size: 1.1rem; padding: 0.75rem;"">{HttpUtility.HtmlEncode(secretKey)}</div>
                         </div>
                         <form method=""POST"" action=""/api/auth/totp/verify"">
                              {antiForgeryTokenField}
                              <div class=""form-group"">
                                   <label for=""code"">Введите 6-значный код из приложения</label>
                                   <input type=""text"" id=""code"" name=""code"" required pattern=""[0-9]{{6}}"" maxlength=""6"" inputmode=""numeric"" placeholder=""Введите 6-значный код"" autocomplete=""one-time-code"" style=""font-size: 1.2rem; text-align: center; letter-spacing: 0.5em;"">
                              </div>
                              <input type=""hidden"" name=""secretKey"" value=""{HttpUtility.HtmlAttributeEncode(secretKey)}"">
                              <button type=""submit"" class=""btn btn-block"">Подтвердить и включить</button>
                         </form>
                         <div class=""text-center mt-4""> <a href=""/api/auth/profile"" class=""link"">Вернуться в профиль</a> </div>
                     </div>
                 </div>
             </div>", "");
        }

        // --- WebAuthn Registration Page ---
        private string RenderWebAuthnRegistration(string options) => string.Format(BaseHtmlTemplate, "Register Security Key - BRU AVTOPARK", $@"
            <div class=""container fade-in"">
                <div class=""card"">
                    <div class=""card-header"">
                        <h1>Register Security Key</h1>
                    </div>
                    <div class=""card-body"">
            <div class=""info-box"">
                <p>Register your security key or biometric authentication (fingerprint, face ID) for passwordless login.</p>
            </div>
            <div id=""options"" data-options=""{options}"" class=""hidden""></div>
            <div class=""flex flex-col items-center gap-4 my-4"">
                <div id=""status"" class=""text-center"">
                    <p>Click the button below to register your security key.</p>
                </div>
                <button onclick=""registerWebAuthn()"" id=""registerButton"" class=""btn"">Register Security Key</button>
                <div id=""loader"" class=""loader hidden""></div>
            </div>
            <div class=""text-center mt-4"">
                <a href=""/api/auth/profile"" class=""link"">Back to Profile</a>
            </div>
            <script>
                async function registerWebAuthn() {{
                    try {{
                        document.getElementById('registerButton').disabled = true;
                        document.getElementById('loader').classList.remove('hidden');
                        document.getElementById('status').innerHTML = '<p>Please follow your browser\'s instructions to register your security key...</p>';
                        
                        const options = JSON.parse(document.getElementById('options').dataset.options);
                        const credential = await navigator.credentials.create({{
                            publicKey: options.publicKey
                        }});
                        
                        // Prepare the credential response for the server
                        const credentialResponse = {{
                            id: credential.id,
                            rawId: arrayBufferToBase64(credential.rawId),
                            type: credential.type,
                            response: {{
                                attestationObject: arrayBufferToBase64(credential.response.attestationObject),
                                clientDataJSON: arrayBufferToBase64(credential.response.clientDataJSON)
                            }}
                        }};
                        
                        // Send the credential to the server
                        const response = await fetch('/api/auth/webauthn/register/complete', {{
                            method: 'POST',
                            headers: {{ 'Content-Type': 'application/json' }},
                            body: JSON.stringify({{ attestationResponse: credentialResponse }})
                        }});
                        
                        if (response.ok) {{
                            document.getElementById('status').innerHTML = '<p class=""success-message"">Security key registered successfully!</p>';
                            setTimeout(() => {{
                                window.location.href = '/api/auth/profile';
                            }}, 1500);
                        }} else {{
                            const error = await response.json();
                            throw new Error(error.message || 'Registration failed');
                        }}
                    }} catch (error) {{
                        console.error('WebAuthn registration failed:', error);
                        document.getElementById('status').innerHTML = `<p class=""error-message"">Failed to register security key: ${{error.message || error}}</p>`;
                    }} finally {{
                        document.getElementById('registerButton').disabled = false;
                        document.getElementById('loader').classList.add('hidden');
                    }}
                }}
                
                // Helper function to convert ArrayBuffer to Base64 string
                function arrayBufferToBase64(buffer) {{
                    const bytes = new Uint8Array(buffer);
                    let binary = '';
                    for (let i = 0; i < bytes.byteLength; i++) {{
                        binary += String.fromCharCode(bytes[i]);
                    }}
                    return btoa(binary);
                }}
            </script>
            <style>
                .hidden {{
                    display: none;
                }}
                        </style>
                    </div>
                </div>
            </div>
        ", "");

        // --- Magic Link Request Form ---
        private string RenderMagicLinkForm(string? error = null, string? message = null) => string.Format(BaseHtmlTemplate, "Magic Link - BRU AVTOPARK", $@"
            <div class=""login-container fade-in"">
                <div class=""auth-card"">
                    <div class=""card-body"">
                        <div class=""yandex-id-header"">
                            <div style=""display: flex; align-items: center;"">
                                <div style=""width: 24px; height: 24px; background-color: var(--primary-color); border-radius: 4px; margin-right: 8px;""></div>
                                <span style=""color: white; font-weight: 500; font-size: 1.5rem;"">BRU ID</span>
                            </div>
                        </div>
                        
                        <h2 style=""text-align: center; margin-bottom: 1.5rem; color: white;"">Login with Magic Link</h2>
                        
            {(error != null ? $@"<div class=""error-message"">{error}</div>" : "")}
            {(message != null ? $@"<div class=""success-message"">{message}</div>" : "")}
                        
                        <div class=""info-box"" style=""background-color: rgba(255, 255, 255, 0.1); color: #b3b3b3;"">
                            <p>Enter your email address to receive a secure login link. No password required!</p>
                        </div>
                        
            <form method=""POST"" action=""/api/auth/magic-link/send"">
                <div class=""form-group"">
                    <label for=""email"">Email Address</label>
                                <input type=""email"" id=""email"" name=""email"" required placeholder=""Enter your email address"">
                </div>
                <button type=""submit"">Send Magic Link</button>
            </form>
                        
                        <div class=""divider"">
                            <span>or</span>
                        </div>
                        
                        <div class=""secondary-option"" onclick=""window.location.href='/api/auth/login'"">
                            <svg width=""20"" height=""20"" viewBox=""0 0 20 20"" fill=""none"" xmlns=""http://www.w3.org/2000/svg"">
                                <path d=""M10 9.58301C12.1047 9.58301 13.8095 7.87818 13.8095 5.77348C13.8095 3.66877 12.1047 1.96394 10 1.96394C7.8953 1.96394 6.19047 3.66877 6.19047 5.77348C6.19047 7.87818 7.8953 9.58301 10 9.58301Z"" stroke=""white"" stroke-width=""1.5"" stroke-linecap=""round"" stroke-linejoin=""round""/>
                                <path d=""M17.1432 18.0357C17.1432 14.9167 13.9384 12.3809 10.0003 12.3809C6.06211 12.3809 2.85742 14.9167 2.85742 18.0357"" stroke=""white"" stroke-width=""1.5"" stroke-linecap=""round"" stroke-linejoin=""round""/>
                            </svg>
                            <span>Back to Login</span>
                        </div>
                    </div>
                </div>
            </div>
        ", "auth-page-body");
        }


        // --- QR Login Page (for Desktop) ---
        private string RenderQrLogin(string qrCode) => string.Format(BaseHtmlTemplate, "QR Login - BRU AVTOPARK", $@"
            <div class=""login-container fade-in"">
                <div class=""auth-card"">
                    <div class=""card-body"">
                        <div class=""yandex-id-header"">
                            <div style=""display: flex; align-items: center;"">
                                <div style=""width: 24px; height: 24px; background-color: var(--primary-color); border-radius: 4px; margin-right: 8px;""></div>
                                <span style=""color: white; font-weight: 500; font-size: 1.5rem;"">BRU ID</span>
                            </div>
                        </div>
                        
                        <h2 style=""text-align: center; margin-bottom: 1.5rem; color: white;"">Login with QR Code</h2>
                        
                        <div class=""info-box"" style=""background-color: rgba(255, 255, 255, 0.1); color: #b3b3b3;"">
                <p>Scan this QR code with your mobile device to log in instantly without entering your password.</p>
            </div>
                        
                        <div class=""qr-code qr-login-container"">
                <img src=""data:image/png;base64,{qrCode}"" alt=""Login QR Code"">
            </div>
                        
                        <div id=""status"" class=""text-center my-4"" style=""color: #b3b3b3;"">
                <p>Waiting for you to scan the QR code...</p>
                            <div class=""loader"" style=""margin-top: 1rem;""></div>
            </div>
                        
                        <div class=""secondary-option"" onclick=""window.location.href='/api/auth/login'"">
                            <svg width=""20"" height=""20"" viewBox=""0 0 20 20"" fill=""none"" xmlns=""http://www.w3.org/2000/svg"">
                                <path d=""M10 9.58301C12.1047 9.58301 13.8095 7.87818 13.8095 5.77348C13.8095 3.66877 12.1047 1.96394 10 1.96394C7.8953 1.96394 6.19047 3.66877 6.19047 5.77348C6.19047 7.87818 7.8953 9.58301 10 9.58301Z"" stroke=""white"" stroke-width=""1.5"" stroke-linecap=""round"" stroke-linejoin=""round""/>
                                <path d=""M17.1432 18.0357C17.1432 14.9167 13.9384 12.3809 10.0003 12.3809C6.06211 12.3809 2.85742 14.9167 2.85742 18.0357"" stroke=""white"" stroke-width=""1.5"" stroke-linecap=""round"" stroke-linejoin=""round""/>
                            </svg>
                            <span>Back to Login</span>
            </div>
                    </div>
                </div>

            <script>
                function checkLoginStatus(deviceId) {{
                    fetch(`/api/auth/qr/direct/check?deviceId=${{deviceId}}`)
                        .then(response => response.json())
                        .then(data => {{
                            if (data.success && data.data && data.data.token) {{
                                document.getElementById('status').innerHTML = '<p class=""success-message"">Login successful! Redirecting...</p>';
                                // Store token in localStorage
                                localStorage.setItem('auth_token', data.data.token);
                                setTimeout(() => {{
                                    window.location.href = `/api/auth/success?token=${{data.data.token}}`;
                                }}, 1000);
                            }} else {{
                                setTimeout(() => checkLoginStatus(deviceId), 2000);
                            }}
                        }})
                        .catch(error => {{
                            console.error('Error checking login status:', error);
                            document.getElementById('status').innerHTML = `<p class=""error-message"">Error checking login status: ${{error.message || 'Unknown error'}}</p>`;
                            setTimeout(() => checkLoginStatus(deviceId), 5000);
                        }});
                }}
                
                // Start polling when page loads
                const deviceId = new URLSearchParams(window.location.search).get('deviceId');
                if (deviceId) {{
                    checkLoginStatus(deviceId);
                }}
                </script>
            </div>
        ", "auth-page-body");


        // --- OIDC Client Admin Pages ---
         private string RenderOidcClientsList(List<ClientDto> clients, string? message = null, string? error = null) {
             string antiForgeryTokenField = ""; // Add if needed
             return string.Format(BaseHtmlTemplate, "Управление OIDC Клиентами", $@"
             <div class=""wide-card card fade-in""> {/* Use wide-card */}
                 <div class=""card-header flex justify-between items-center"">
                     <h2 style=""margin-bottom: 0;"">OAuth 2.0 / OpenID Connect Клиенты</h2>
                     <a href=""/api/auth/connect/clients/new"" class=""btn btn-secondary"" style=""width: auto; white-space: nowrap;"">Новый Клиент</a>
                 </div>
                 <div class=""card-body"">
                      {(!string.IsNullOrEmpty(message) ? $"<div class='success-message'>{HttpUtility.HtmlEncode(message)}</div>" : "")}
                      {(!string.IsNullOrEmpty(error) ? $"<div class='error-message'>{HttpUtility.HtmlEncode(error)}</div>" : "")}

                      {(clients.Any() ? $@"
                           <table style=""width: 100%; border-collapse: collapse;"">
                               <thead>
                                   <tr style=""border-bottom: 1px solid var(--border-color);"">
                                   <th style=""text-align: left; padding: 0.75rem 1rem;"">Client ID</th>
                                   <th style=""text-align: left; padding: 0.75rem 1rem;"">Display Name</th>
                                   <th style=""padding: 0.75rem 1rem; text-align: right;"">Действия</th>
                                   </tr>
                               </thead>
                               <tbody>
                                   {string.Join("", clients.Select(client => $@"
                                   <tr style=""border-bottom: 1px solid var(--border-color);"">
                                       <td style=""padding: 0.75rem 1rem;""><code class='code-display' style='margin:0; padding: 2px 4px; display: inline-block;'>{HttpUtility.HtmlEncode(client.ClientId)}</code></td>
                                       <td style=""padding: 0.75rem 1rem;"">{HttpUtility.HtmlEncode(client.DisplayName ?? client.ClientId)}</td>
                                       <td style=""padding: 0.75rem 1rem; text-align: right;"">
                                           <div class=""flex gap-2 justify-end"">
                                               <a href=""/api/auth/connect/clients/{HttpUtility.UrlEncode(client.ClientId)}"" class=""btn btn-secondary"" style=""width: auto; padding: 0.25rem 0.75rem; font-size: 0.875rem;"">Детали</a>
                                               <a href=""/api/auth/connect/clients/{HttpUtility.UrlEncode(client.ClientId)}/edit"" class=""btn btn-secondary"" style=""width: auto; padding: 0.25rem 0.75rem; font-size: 0.875rem;"">Изменить</a>
                                               <form method=""POST"" action=""/api/auth/connect/clients/{HttpUtility.UrlEncode(client.ClientId)}/delete"" onsubmit=""return confirm('Удалить клиента {HttpUtility.JavaScriptStringEncode(client.ClientId)}? Это действие нельзя отменить.');"" style=""display: inline;"">
                                                   {antiForgeryTokenField}
                                                    <button type=""submit"" class=""btn btn-danger"" style=""width: auto; padding: 0.25rem 0.75rem; font-size: 0.875rem;"">Удалить</button>
                                               </form>
                                           </div>
                                       </td>
                                  </tr>"))}
                               </tbody>
                           </table>"
                         : $@"<div class=""info-box""><p>Нет зарегистрированных OAuth клиентов. <a href=""/api/auth/connect/clients/new"" class=""link"">Создать нового клиента</a>.</p></div>")}
                     <div class=""mt-4 text-center""> <a href=""/api/auth/profile"" class=""link"">Вернуться в профиль</a> </div>
                 </div>
             </div>", "");
         }

        private string RenderOidcClientDetails(GetClientResponse client, string? message = null, string? error = null) => string.Format(BaseHtmlTemplate, $"Детали: {client.DisplayName ?? client.ClientId}", $@"
            <div class=""wide-card card fade-in"">
                <div class=""card-header flex justify-between items-center"">
                    <h2 style=""margin-bottom: 0;"">Детали клиента: {HttpUtility.HtmlEncode(client.DisplayName ?? client.ClientId)}</h2>
                     <a href=""/api/auth/connect/clients/{HttpUtility.UrlEncode(client.ClientId)}/edit"" class=""btn btn-secondary"" style=""width: auto;"">Изменить</a>
                 </div>
                 <div class=""card-body"">
                      {(!string.IsNullOrEmpty(message) ? $"<div class='success-message'>{HttpUtility.HtmlEncode(message)}</div>" : "")}
                      {(!string.IsNullOrEmpty(error) ? $"<div class='error-message'>{HttpUtility.HtmlEncode(error)}</div>" : "")}
                     <div class=""form-group""> <label>Client ID</label> <div class=""code-display"">{HttpUtility.HtmlEncode(client.ClientId)}</div> </div>
                     <div class=""form-group""> <label>Display Name</label> <div class=""code-display"">{HttpUtility.HtmlEncode(client.DisplayName)}</div> </div>
                     <div class=""form-group""> <label>Redirect URIs</label> <div class=""code-display"">{(client.RedirectUris.Any() ? string.Join("<br>", client.RedirectUris.Select(HttpUtility.HtmlEncode)) : "<i>Не указаны</i>")}</div> </div>
                     <div class=""form-group""> <label>Post-Logout Redirect URIs</label> <div class=""code-display"">{(client.PostLogoutRedirectUris.Any() ? string.Join("<br>", client.PostLogoutRedirectUris.Select(HttpUtility.HtmlEncode)) : "<i>Не указаны</i>")}</div> </div>
                     <div class=""form-group""> <label>Allowed Scopes</label> <div class=""code-display"">{(client.AllowedScopes.Any() ? string.Join("<br>", client.AllowedScopes.Select(HttpUtility.HtmlEncode)) : "<i>Не указаны</i>")}</div> </div>
                     <div class=""form-group""> <label>Require Consent</label> <div class=""code-display"">{(client.RequireConsent ? "Да" : "Нет")}</div> </div>
                       {/* Add Client Secret display/management if needed securely */}
                      <div class=""mt-4 text-center""> <a href=""/api/auth/connect/clients"" class=""btn btn-secondary"" style=""width: auto;"">Назад к списку клиентов</a> </div>
                 </div>
            </div>", "");

         private string RenderOidcClientForm(string? clientId = null, GetClientResponse? client = null, string? error = null) {
             bool isEdit = clientId != null;
             string antiForgeryTokenField = ""; // Add if needed
              return string.Format(BaseHtmlTemplate, isEdit ? $"Редактировать: {client?.DisplayName ?? clientId}" : "Новый OIDC Клиент", $@"
             <div class=""wide-card card fade-in"">
                 <div class=""card-header""><h2>{ (isEdit ? "Редактировать OIDC Клиент" : "Создать OIDC Клиент")}</h2></div>
                 <div class=""card-body"">
                      {(!string.IsNullOrEmpty(error) ? $"<div class='error-message'>{HttpUtility.HtmlEncode(error)}</div>" : "")}
                      <div class=""info-box""> <p>{(isEdit ? "Измените детали клиента. Будьте осторожны при изменении URI перенаправления и разрешений." : "Зарегистрируйте новое приложение (клиент) для использования с вашим сервером авторизации.")}</p> </div>
                      <form method=""POST"" action=""{(isEdit ? $"/api/auth/connect/update-client/{HttpUtility.UrlEncode(clientId)}" : "/api/auth/connect/register-client")}"">
                           {antiForgeryTokenField}
                           <div class=""form-group"">
                               <label for=""clientIdInput"">Client ID*</label>
                               <input type=""text"" id=""clientIdInput"" name=""ClientId"" value=""{HttpUtility.HtmlAttributeEncode(client?.ClientId ?? "")}"" {(isEdit ? "readonly style='background-color: var(--id-color-default-bg-base);'" : "required")}>
                                <p class=""text-muted"" style=""font-size: 0.875rem;"">Уникальный идентификатор. Не может быть изменен после создания.</p>
                           </div>
                           <div class=""form-group"">
                               <label for=""displayNameInput"">Display Name*</label>
                               <input type=""text"" id=""displayNameInput"" name=""DisplayName"" value=""{HttpUtility.HtmlAttributeEncode(client?.DisplayName ?? "")}"" required>
                           </div>
                           <div class=""form-group"">
                               <label for=""clientSecretInput"">Client Secret {(isEdit ? "(Оставьте пустым для сохранения)" : "*")}</label>
                               <input type=""password"" id=""clientSecretInput"" name=""ClientSecret"" {(isEdit ? "" : "required")} placeholder=""{(isEdit ? "••••••••••" : "")}"">
                                <p class=""text-muted"" style=""font-size: 0.875rem;"">{(isEdit ? "Введите новый секрет только если хотите его изменить." : "Надежный секрет для конфиденциальных клиентов.")}</p>
                           </div>
                            <div class=""form-group"">
                               <label for=""redirectUrisInput"">Redirect URIs* (по одному на строку)</label>
                               <textarea id=""redirectUrisInput"" name=""RedirectUris"" rows=""4"" required placeholder=""https://myapp.com/callback
http://localhost:5000/signin-oidc"">{HttpUtility.HtmlEncode(client?.RedirectUris != null ? string.Join("\n", client.RedirectUris) : "")}</textarea>
                                <p class=""text-muted"" style=""font-size: 0.875rem;"">URL, на которые разрешено перенаправление после авторизации.</p>
                           </div>
                           <div class=""form-group"">
                               <label for=""postLogoutRedirectUrisInput"">Post-Logout Redirect URIs (по одному на строку)</label>
                               <textarea id=""postLogoutRedirectUrisInput"" name=""PostLogoutRedirectUris"" rows=""3"" placeholder=""https://myapp.com/loggedout"">{HttpUtility.HtmlEncode(client?.PostLogoutRedirectUris != null ? string.Join("\n", client.PostLogoutRedirectUris) : "")}</textarea>
                               <p class=""text-muted"" style=""font-size: 0.875rem;"">URL, на которые разрешено перенаправление после выхода.</p>
                           </div>
                            <div class=""form-group"">
                               <label for=""allowedScopesInput"">Allowed Scopes* (по одному на строку)</label>
                               <textarea id=""allowedScopesInput"" name=""AllowedScopes"" rows=""5"" required placeholder=""openid
profile
email
roles
api"">{HttpUtility.HtmlEncode(client?.AllowedScopes != null ? string.Join("\n", client.AllowedScopes) : "openid\nprofile\nemail\nroles")}</textarea>
                               <p class=""text-muted"" style=""font-size: 0.875rem;"">Разрешения, которые может запросить клиент (например, openid, profile, roles, api).</p>
                           </div>
                           <div class=""form-group"">
                                <label style=""display: flex; align-items: center; cursor: pointer;"">
                                   <input type=""checkbox"" id=""requireConsentInput"" name=""RequireConsent"" value=""true"" {(client?.RequireConsent ?? false ? "checked" : "")} style=""width: auto; margin-right: 0.5rem;"">
                                   Require Consent (Запрашивать разрешение пользователя)
                               </label>
                               <p class=""text-muted"" style=""font-size: 0.875rem;"">Если отмечено, пользователь должен будет явно одобрить запрошенные scopes.</p>
                           </div>
                           <div class=""flex gap-4 mt-4"">
                               <button type=""submit"" class=""btn"" style=""width: auto;"">{ (isEdit ? "Сохранить изменения" : "Создать клиента")}</button>
                               <a href=""{(isEdit ? $"/api/auth/connect/clients/{HttpUtility.UrlEncode(clientId)}" : "/api/auth/connect/clients")}"" class=""btn btn-secondary"" style=""width: auto;"">Отмена</a>
                           </div>
                      </form>
                 </div>
            </div>", "");
         }


        #endregion

        // --- File: AuthController.cs (Continued) ---

        //===========================
        // Controller Actions
        //===========================

        #region Combined/Legacy Login & Registration & Profile

        [HttpGet("login")]
        [AllowAnonymous]
        public IActionResult LoginPage([FromQuery] string? returnUrl = null, [FromQuery] string? error = null, [FromQuery] string? message = null)
        {
            if (!IsBrowserRequest()) {
                 _logger.LogDebug("GET /api/auth/login accessed via non-browser, returning message.");
                 return Ok(new ApiResponse<object> { Success = false, Message = "Use POST method to login via API." });
            }
            _logger.LogDebug("Rendering login page for browser request. ReturnUrl: {ReturnUrl}, Error: {Error}", returnUrl, error);
            if (!string.IsNullOrEmpty(returnUrl)) ViewData["ReturnUrl"] = returnUrl;
            return Content(RenderLoginForm(error, message, returnUrl), "text/html");
        }

        [HttpPost("login")]
        [AllowAnonymous]
        [Consumes("application/json", "application/x-www-form-urlencoded")]
        public async Task<IActionResult> Login([FromQuery] string? returnUrl = null)
        {
            _logger.LogInformation("POST /api/auth/login received. ReturnUrl: {ReturnUrl}", returnUrl);
            LoginRequest? finalRequest = null;
            string? loginUsername = null; // For consistent logging

            // --- Try Parsing Request ---
            if (Request.HasFormContentType) {
                if (Request.Form.TryGetValue("username", out var formUsername) &&
                    Request.Form.TryGetValue("password", out var formPassword) &&
                    !string.IsNullOrWhiteSpace(formUsername) &&
                    !string.IsNullOrWhiteSpace(formPassword))
                {
                    _logger.LogInformation("Processing login request from HTML form.");
                    finalRequest = new LoginRequest { Username = formUsername.ToString(), Password = formPassword.ToString(), SkipTwoFactor = false };
                    loginUsername = finalRequest.Username;
                } else { _logger.LogWarning("Login form submitted but missing username or password."); }
            }
            else if (Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ?? false) {
                try {
                    using var reader = new System.IO.StreamReader(Request.Body, Encoding.UTF8);
                    var requestBody = await reader.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(requestBody)) {
                         var bodyRequest = JsonSerializer.Deserialize<CombinedLoginRequest>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                         if (bodyRequest != null && (!string.IsNullOrWhiteSpace(bodyRequest.Username) || !string.IsNullOrWhiteSpace(bodyRequest.Login)) && !string.IsNullOrWhiteSpace(bodyRequest.Password))
                         {
                             finalRequest = new LoginRequest { Username = !string.IsNullOrWhiteSpace(bodyRequest.Username) ? bodyRequest.Username : bodyRequest.Login!, Password = bodyRequest.Password!, SkipTwoFactor = bodyRequest.SkipTwoFactor };
                             loginUsername = finalRequest.Username;
                              _logger.LogInformation("Processing login request from JSON body for user '{LoginUsername}'.", loginUsername);
                         } else { _logger.LogWarning("Login JSON body parsed but missing required fields."); }
                    } else { _logger.LogWarning("Login JSON request body was empty."); }
                } catch (JsonException jsonEx) { _logger.LogWarning(jsonEx, "Failed to parse JSON body for login request."); }
                  catch (Exception ex) { _logger.LogError(ex, "Error reading request body for login."); }
            }
            else {
                _logger.LogWarning("Login request received with unsupported Content-Type: {ContentType}", Request.ContentType);
            }

            // --- Validate Input ---
            if (finalRequest == null || string.IsNullOrWhiteSpace(loginUsername) || string.IsNullOrWhiteSpace(finalRequest.Password))
            {
                 _logger.LogWarning("Login failed: Invalid input data (Username/Password missing).");
                 string errorMsg = "Требуется логин и пароль.";
                 return IsBrowserRequest()
                     ? Redirect($"/api/auth/login?error={Uri.EscapeDataString(errorMsg)}&returnUrl={Uri.EscapeDataString(returnUrl ?? "")}")
                     : BadRequest(new ApiResponse<object> { Success = false, Message = errorMsg });
            }

            // --- Process Login ---
            var (authSuccess, userProfile, twoFactorData, errorMessage) = await ProcessLoginAndGetData(finalRequest);

            if (!authSuccess) {
                _logger.LogWarning("Login failed for user {Username}: {Error}", loginUsername, errorMessage);
                return IsBrowserRequest()
                     ? Redirect($"/api/auth/login?error={Uri.EscapeDataString(errorMessage ?? "Неверный логин или пароль.")}&returnUrl={Uri.EscapeDataString(returnUrl ?? "")}")
                     : Unauthorized(new ApiResponse<object> { Success = false, Message = errorMessage ?? "Authentication failed." });
            }

            // --- Handle 2FA ---
            if (twoFactorData != null) {
                _logger.LogInformation("2FA required for user {Username}, type: {Type}", loginUsername, twoFactorData.TwoFactorType);
                 string twoFaPage = $"/api/auth/2fa?type={twoFactorData.TwoFactorType}&tempToken={Uri.EscapeDataString(twoFactorData.TempToken)}&returnUrl={Uri.EscapeDataString(returnUrl ?? "/api/auth/profile")}";
                 if (IsBrowserRequest()) {
                      _logger.LogDebug("Redirecting browser to 2FA page: {TwoFaPageUrl}", twoFaPage);
                      return Redirect(twoFaPage); // Redirect browser to unified 2FA page
                  } else {
                      // Return specific 2FA response for API clients
                      if (twoFactorData is WebAuthnTwoFactorResponse webAuthnData) {
                           return Ok(new ApiResponse<WebAuthnTwoFactorResponse> { Success = true, Message = "WebAuthn authentication required", Data = webAuthnData });
                      } else {
                           return Ok(new ApiResponse<TwoFactorResponse> { Success = true, Message = "Two-factor authentication required", Data = twoFactorData });
                      }
                  }
            }

            // --- Handle Login Success without 2FA step ---
            if(userProfile == null) {
                 _logger.LogError("Login inconsistency: Authentication succeeded but UserProfile is null for {Username}", loginUsername);
                 string errorMsg = "Внутренняя ошибка сервера при входе.";
                 return IsBrowserRequest() ? Redirect($"/api/auth/login?error={Uri.EscapeDataString(errorMsg)}")
                                           : StatusCode(500, new ApiResponse<object> { Success = false, Message = errorMsg });
            }

            _logger.LogInformation("Login successful for user {Username}. Proceeding with cookie/token.", userProfile.Login);
            var principal = await CreatePrincipalForSessionAndOidcAsync(userProfile);

            if (IsBrowserRequest()) {
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties {
                     IsPersistent = true, // Make session persistent? Configure as needed.
                     IssuedUtc = DateTimeOffset.UtcNow
                     // AllowRefresh = true, // Optionally allow sliding expiration
                 });
                 _logger.LogInformation("Authentication cookie set for browser session for user {Username}.", userProfile.Login);
                 string redirectTarget = GetSafeRedirectUrl(returnUrl); // Use helper to validate/get default
                 _logger.LogDebug("Redirecting browser user to {RedirectTarget}", redirectTarget);
                 return Redirect(redirectTarget);
            } else {
                 _logger.LogDebug("Returning legacy JWT token for API login for user {Username}.", userProfile.Login);
                 var token = GenerateJwtToken(userProfile); // Legacy JWT for Avalonia/API
                 return Ok(new ApiResponse<LoginResponse> {
                     Success = true, Message = "Authentication successful",
                     Data = new LoginResponse { Token = token, User = MapUserToDto(userProfile) }
                 });
            }
        }

        // Refactored internal login processing logic
        private async Task<(bool Success, UserProfile? User, TwoFactorResponse? TwoFactorData, string? ErrorMessage)> ProcessLoginAndGetData(LoginRequest request)
        {
            try
            {
                 _logger.LogDebug("ProcessLoginAndGetData: Authenticating user {Username}", request.Username);
                 var user = await _authService.AuthenticateAsync(request.Username, request.Password);
                 if (user == null) {
                      return (false, null, null, "Неверный логин или пароль");
                 }
                 _logger.LogDebug("ProcessLoginAndGetData: User {Username} authenticated. Fetching settings.", request.Username);

                 var conn = GetConnection();
                 var userSettings = conn.Db.UserSettings.Iter().FirstOrDefault(s => s.UserId.Equals(user.UserId));

                 if (userSettings == null) {
                      _logger.LogWarning("ProcessLoginAndGetData: User settings not found for {Username}, creating defaults.", request.Username);
                      conn.Reducers.CreateUserSettings(user.UserId);
                      await Task.Delay(100); // Allow reducer time
                      userSettings = conn.Db.UserSettings.Iter().FirstOrDefault(s => s.UserId.Equals(user.UserId));
                       if (userSettings == null) {
                            _logger.LogError("ProcessLoginAndGetData: Failed to create/retrieve user settings for {Username}, proceeding without 2FA.", request.Username);
                             return (true, user, null, "Вход выполнен успешно"); // Proceed without 2FA if settings creation failed
                       }
                 }
                 _logger.LogDebug("ProcessLoginAndGetData: User settings loaded for {Username}. TOTP: {Totp}, WebAuthn: {WebAuthn}", request.Username, userSettings.TotpEnabled, userSettings.WebAuthnEnabled);

                 // --- 2FA Check ---
                 if (!request.SkipTwoFactor) // Check if 2FA should be skipped (e.g., during specific flows like ROPC)
                 {
                      if (userSettings.TotpEnabled) {
                           _logger.LogInformation("ProcessLoginAndGetData: TOTP required for {Username}", request.Username);
                           var tempToken = GenerateRandomToken();
                           var expiresAt = (ulong)DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds();
                            // Call SpacetimeDB reducer to store the temporary token
                           conn.Reducers.CreateTwoFactorToken(user.UserId, tempToken, false, expiresAt, Request?.Headers?.UserAgent.ToString(), HttpContext?.Connection?.RemoteIpAddress?.ToString());
                            return (true, user, new TwoFactorResponse { RequiresTwoFactor = true, TwoFactorType = "totp", TempToken = tempToken }, "Требуется двухфакторная аутентификация");
                      }
                      if (userSettings.WebAuthnEnabled) {
                           _logger.LogInformation("ProcessLoginAndGetData: WebAuthn required for {Username}", request.Username);
                           var credentials = conn.Db.WebAuthnCredential.Iter().Where(c => c.UserId.Equals(user.UserId) && c.IsActive).ToList();
                           if (credentials.Any()) {
                               var (optionsSuccess, options, _) = await _webAuthnService.GetAssertionOptionsAsync(user.Login); // Needs username
                               if (optionsSuccess && options != null) {
                                   var tempToken = GenerateRandomToken();
                                   var expiresAt = (ulong)DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds();
                                   conn.Reducers.CreateTwoFactorToken(user.UserId, tempToken, false, expiresAt, Request?.Headers?.UserAgent.ToString(), HttpContext?.Connection?.RemoteIpAddress?.ToString());
                                   return (true, user, new WebAuthnTwoFactorResponse { RequiresTwoFactor = true, TwoFactorType = "webauthn", TempToken = tempToken, Options = options }, "Требуется аутентификация WebAuthn");
                               } else { _logger.LogWarning("ProcessLoginAndGetData: Failed to get WebAuthn assertion options for {Username}", request.Username); }
                           } else { _logger.LogWarning("ProcessLoginAndGetData: WebAuthn enabled for {Username} but no credentials found.", request.Username); }
                      }
                 } else { _logger.LogInformation("ProcessLoginAndGetData: Skipping 2FA check for {Username}.", request.Username); }

                 // Success without 2FA step needed now
                 return (true, user, null, "Вход выполнен успешно");
            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "Error during internal login processing for user: {Username}", request.Username);
                 return (false, null, null, "Произошла внутренняя ошибка сервера.");
            }
        }

         [HttpGet("register")]
        [AllowAnonymous]
        public async Task<IActionResult> RegisterPage([FromQuery] string? error = null, [FromQuery] string? message = null, [FromQuery] int? attempt = null)
        {
            if (!IsBrowserRequest())
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "This endpoint is for browser access only. Use POST /api/auth/register for API calls."
                });
            }

            string? token = null;
            bool isAdmin = false;

            try
            {
            // Check Authorization header first
            if (Request.Headers.Authorization.Count > 0)
            {
                var authHeader = Request.Headers.Authorization.ToString();
                if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    token = authHeader.Substring("Bearer ".Length).Trim();
                }
            }

            // If no auth header, check query string
            if (string.IsNullOrEmpty(token) && Request.Query.ContainsKey("token"))
            {
                token = Request.Query["token"];
            }

                // If still no token, try to get it from localStorage
            if (string.IsNullOrEmpty(token))
            {
                    return Content(@"
                    <script>
                            const storedToken = localStorage.getItem('auth_token');
                            if (storedToken) {
                            window.location.href = '/api/auth/register?token=' + encodeURIComponent(storedToken);
                            } else {
                                window.location.href = '/api/auth/login?error=' + encodeURIComponent('Please log in as administrator') + 
                                                     '&returnUrl=' + encodeURIComponent('/api/auth/register');
                            }
                    </script>
                ", "text/html");
            }

                // Validate token format
                if (!token.Contains('.') || token.Count(c => c == '.') != 2)
                {
                    _logger.LogWarning("Invalid token format detected during admin check");
                    return Redirect("/api/auth/login?error=Invalid token format&returnUrl=/api/auth/register");
                }

                try
                {
                    // Parse token and validate claims
                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(token);

                    // Check for admin role in multiple ways
                var primaryRoleClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "primary_role");
                    var roleClaims = jwtToken.Claims.Where(c => c.Type == ClaimTypes.Role || c.Type == "role").ToList();
                    
                isAdmin = primaryRoleClaim?.Value == "1" || 
                             roleClaims.Any(c => c.Value == "1" || c.Value.Equals("Administrator", StringComparison.OrdinalIgnoreCase));

                    // Log the claims for debugging
                    _logger.LogInformation("Token claims during admin check: Primary Role = {PrimaryRole}, Roles = {Roles}",
                        primaryRoleClaim?.Value,
                        string.Join(", ", roleClaims.Select(c => c.Value)));

                if (!isAdmin)
                {
                        _logger.LogWarning("User attempted to access register page without admin privileges");
                        
                    // If we haven't tried 3 times yet, increment attempt counter
                    if (!attempt.HasValue || attempt.Value < 3)
                    {
                        int nextAttempt = (attempt ?? 0) + 1;
                            return Content(RenderRegisterForm(
                                "Administrator privileges required. Verifying permissions...", 
                                null, 
                                nextAttempt, 
                                false
                            ), "text/html");
                        }
                        
                        return Redirect("/api/auth/login?error=Administrator privileges required&returnUrl=/api/auth/register");
                    }

                    // If we get here, the user is an admin
                    _logger.LogInformation("Admin access verified for registration page");
                    return Content(RenderRegisterForm(error, message, null, true), "text/html");
            }
            catch (Exception ex)
            {
                    _logger.LogError(ex, "Error parsing JWT token during admin check");
                    return Redirect("/api/auth/login?error=Invalid token. Please log in again.&returnUrl=/api/auth/register");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during admin check for registration page");
                return Redirect("/api/auth/login?error=Error validating admin access. Please try again.&returnUrl=/api/auth/register");
            }
        }

        [HttpPost("register")]
        [Consumes("application/json", "application/x-www-form-urlencoded")]
        [AllowAnonymous] // Check permission inside
         public async Task<ActionResult<RegisterResponse>> Register([FromBody] RegisterRequest request)
        {
            _logger.LogInformation("Starting registration process for user: {Username}", request.Username);
            
            try
            {
                _logger.LogInformation("Registration attempt for user: {Username}", request.Username);

                // Check admin status manually using JWT token
                _logger.LogInformation("Checking admin status from JWT token");
                bool isAdmin = false;
                string? token = null;

                // Check Authorization header
                _logger.LogInformation("Checking Authorization header");
                if (Request.Headers.Authorization.Count > 0)
                {
                    var authHeader = Request.Headers.Authorization.ToString();
                    _logger.LogDebug("Authorization header found: {AuthHeader}", authHeader);
                    if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        token = authHeader.Substring("Bearer ".Length).Trim();
                        _logger.LogDebug("Bearer token extracted");
                    }
                }

                if (!string.IsNullOrEmpty(token))
                {
                    _logger.LogInformation("Validating JWT token");
                    try
                    {
                        var handler = new JwtSecurityTokenHandler();
                        var jwtToken = handler.ReadJwtToken(token);
                        _logger.LogDebug("JWT token successfully parsed");

                        // Check for admin role in claims
                        _logger.LogInformation("Checking for admin role in token claims");
                        var primaryRoleClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "primary_role");
                        isAdmin = primaryRoleClaim?.Value == "1" || 
                                jwtToken.Claims.Any(c => c.Type == "role" && c.Value == "1");
                        _logger.LogInformation("Admin status determined: {IsAdmin}", isAdmin);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error validating JWT token for registration");
                        // Continue with isAdmin = false
                    }
                }

                if (!isAdmin)
                {
                    _logger.LogWarning("Unauthorized attempt to register a user without admin privileges");
                    return StatusCode(403, new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Administrator privileges required for user registration"
                    });
                }

                // Check if user already exists
                _logger.LogInformation("Checking if user already exists: {Username}", request.Username);
                var existingUser = await _userService.GetUserByLoginAsync(request.Username);
                if (existingUser != null)
                {
                    _logger.LogWarning("Username already exists: {Username}", request.Username);
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Username already exists"
                    });
                }

                // Check if role is valid - only admins can create admins
                _logger.LogInformation("Validating role assignment: {RequestedRole}", request.Role);
                if (request.Role == 1 && !isAdmin)
                {
                    _logger.LogWarning("Non-admin user attempted to create admin account");
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Only administrators can create administrator accounts"
                    });
                }

                // Extract the identity of the LOGGED IN user (not the one being registered)
                _logger.LogInformation("Extracting logged-in user identity from token");
                Identity? userIdentity = null;
                if (!string.IsNullOrEmpty(token))
                {
                    try
                    {
                        var handler = new JwtSecurityTokenHandler();
                        var jwtToken = handler.ReadJwtToken(token);
                        _logger.LogDebug("JWT token parsed successfully");
                        
                        // Get the currently logged-in user's login from JWT token claims
                        _logger.LogInformation("Extracting logged-in user's login from token claims");
                        var loggedInUserLogin = jwtToken.Claims.FirstOrDefault(c => c.Type == "unique_name")?.Value;
                        _logger.LogDebug("Found unique_name claim with value: {Login}", loggedInUserLogin);
                        
                        if (string.IsNullOrEmpty(loggedInUserLogin))
                        {
                            // Fallback to standard name claim if unique_name not found
                            _logger.LogInformation("unique_name claim not found, trying ClaimTypes.Name");
                            loggedInUserLogin = jwtToken.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
                            _logger.LogDebug("Found Name claim with value: {Login}", loggedInUserLogin);
                        }
                        
                        if (!string.IsNullOrEmpty(loggedInUserLogin))
                        {
                            // Get identity of the logged-in user using their login
                            _logger.LogInformation("Retrieving identity for logged-in user: {Login}", loggedInUserLogin);
                            
                            // Get the connection
                            var conn = _spacetimeService.GetConnection();
                            _logger.LogDebug("Retrieved SpacetimeDB connection");
                            
                            // Find user by login
                            var userProfile = conn.Db.UserProfile.Iter()
                                .FirstOrDefault(u => u.Login == loggedInUserLogin && u.IsActive);
                            
                            if (userProfile != null)
                            {
                                userIdentity = userProfile.UserId;
                                _logger.LogInformation("Successfully retrieved identity for logged-in user: {Login}", loggedInUserLogin);
                            }
                            else
                            {
                                _logger.LogWarning("Could not find user profile for logged-in user: {Login}", loggedInUserLogin);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Could not extract logged-in user's login from any token claims");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error extracting logged-in user identity from JWT token");
                    }
                }
                

                var identity = await GenerateIdentityAsync();
                if (identity == null)
                {
                    _logger.LogWarning("Failed to generate identity");
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Failed to generate user identity"
                    });
                }
                string NewUserIdentity = identity;
                _logger.LogInformation("Successfully generated new user identity: {Identity}", NewUserIdentity);

                _logger.LogInformation("Attempting to register new user: {Username}", request.Username);
                try
                {
                    // Register user
                    _logger.LogInformation("Calling authentication service to register user");
                    var success = await _authService.RegisterAsync(
                        request.Username,
                        request.Password,
                        request.Role,
                        request.Email,
                        request.PhoneNumber,
                        userIdentity,
                        NewUserIdentity
                    );

                    if (!success)
                    {
                        _logger.LogWarning("User registration failed through auth service");
                        return BadRequest(new ApiResponse<object>
                        {
                            Success = false,
                            Message = "Failed to register user"
                        });
                    }
                    _logger.LogInformation("User registration successful through auth service");
                }
                catch (Exception ex)
                {
                    // Check if the error is from SpacetimeDB role assignment
                    _logger.LogError(ex, "Exception during user registration");
                    if (ex.Message?.Contains("Unauthorized") == true || 
                        ex.InnerException?.Message?.Contains("Unauthorized") == true)
                    {
                        _logger.LogWarning("Role assignment failed due to authorization: {Error}", ex.Message);
                        
                        // Check if the user was actually created despite the role error
                        _logger.LogInformation("Checking if user was created despite role error");
                        var newUser = await _userService.GetUserByLoginAsync(request.Username);
                        if (newUser != null)
                        {
                            _logger.LogInformation("User created with default role: {Username}", request.Username);
                            return Ok(new ApiResponse<RegisterResponse>
                            {
                                Success = true,
                                Message = "User created but could not assign requested role. Default user role applied.",
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
                        
                        _logger.LogWarning("User creation failed during role assignment");
                        return BadRequest(new ApiResponse<object>
                        {
                            Success = false,
                            Message = "User creation succeeded but role assignment failed. Try logging in to SpacetimeDB directly to assign a role."
                        });
                    }
                    
                    // For other errors, rethrow to be caught by outer catch
                    _logger.LogError("Rethrowing exception for outer catch handler");
                    throw;
                }

                // Get the newly created user
                _logger.LogInformation("Retrieving newly created user: {Username}", request.Username);
                var newlyCreatedUser = await _userService.GetUserByLoginAsync(request.Username);
                if (newlyCreatedUser == null)
                {
                    _logger.LogError("User was created but could not be retrieved: {Username}", request.Username);
                    return StatusCode(500, new ApiResponse<object>
                    {
                        Success = false,
                        Message = "User was created but could not be retrieved"
                    });
                }

                _logger.LogInformation("User {Username} registered successfully", request.Username);

                return Ok(new ApiResponse<RegisterResponse>
                {
                    Success = true,
                    Message = "User registered successfully",
                    Data = new RegisterResponse
                    {
                        User = new UserDto
                        {
                            Id = newlyCreatedUser.LegacyUserId,
                            Username = newlyCreatedUser.Login,
                            Email = newlyCreatedUser.Email,
                            PhoneNumber = newlyCreatedUser.PhoneNumber,
                            Role = _authService.GetUserRole(newlyCreatedUser.UserId)
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

        // --- Profile Page ---
        [HttpGet("profile")]
        [AllowAnonymous]
        public async Task<IActionResult> ProfilePage()
        {
            if (!IsBrowserRequest())
            {
                return Ok(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Please use a browser to view your profile"
                });
            }

            string? token = null;

            // Check Authorization header first
            if (Request.Headers.Authorization.Count > 0)
            {
                var authHeader = Request.Headers.Authorization.ToString();
                if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    token = authHeader.Substring("Bearer ".Length).Trim();
                }
            }

            // If no auth header, check query string
            if (string.IsNullOrEmpty(token) && Request.Query.ContainsKey("token"))
            {
                token = Request.Query["token"];
            }

            // If still no token, check localStorage via JavaScript
            if (string.IsNullOrEmpty(token))
            {
                return Content($@"
                    <script>
                        const storedToken = localStorage.getItem('auth_token');
                        if (storedToken) {{
                            window.location.href = '/api/auth/profile?token=' + encodeURIComponent(storedToken);
                        }} else {{
                            window.location.href = '/api/auth/login?error=Please log in to view your profile';
                        }}
                    </script>
                ", "text/html");
            }

            try
            {
                // Validate token format
                if (!token.Contains('.') || token.Count(c => c == '.') != 2)
                {
                    return Redirect("/api/auth/login?error=Invalid token format");
                }

                // Parse token without validation to get the user ID
                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(token);
                
                var userIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "identity");
                if (userIdClaim == null)
                {
                    return Redirect("/api/auth/login?error=Invalid token claims");
                }

                // Get user information
                var conn = _spacetimeService.GetConnection();
                var user = conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.UserId.ToString() == userIdClaim.Value);

                if (user == null)
                {
                    return Redirect("/api/auth/login?error=User not found");
                }

                // Get user settings
                var userSettings = conn.Db.UserSettings.Iter()
                    .FirstOrDefault(s => s.UserId.Equals(user.UserId));

                if (userSettings == null)
                {
                    // Create default settings if they don't exist
                    conn.Reducers.CreateUserSettings(user.UserId);
                    await Task.Delay(100); // Wait for reducer
                    userSettings = conn.Db.UserSettings.Iter()
                        .FirstOrDefault(s => s.UserId.Equals(user.UserId));
                }

                // Get WebAuthn credentials
                var webAuthnCredentials = conn.Db.WebAuthnCredential.Iter()
                    .Where(c => c.UserId.Equals(user.UserId) && c.IsActive)
                    .Select(c => new WebAuthnCredentialDto
                    {
                        Id = Convert.ToBase64String(c.CredentialId.ToArray()),
                        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)c.CreatedAt).DateTime
                    })
                    .ToList();

                // Get user roles
                var userRoles = conn.Db.UserRole.Iter()
                    .Where(ur => ur.UserId.Equals(user.UserId))
                    .ToList();

                // Get role details
                var roles = new List<Role>();
                foreach (var ur in userRoles)
                {
                    var role = conn.Db.Role.RoleId.Find(ur.RoleId);
                    if (role != null && role.IsActive)
                    {
                        roles.Add(role);
                    }
                }

                // Get role permissions
                var rolePermissions = conn.Db.RolePermission.Iter()
                    .Where(rp => roles.Select(r => r.RoleId).Contains(rp.RoleId))
                    .ToList();

                // Get permission details
                var permissionIds = rolePermissions.Select(rp => rp.PermissionId).Distinct().ToList();
                var permissions = new List<Permission>();
                foreach (var permissionId in permissionIds)
                {
                    var permission = conn.Db.Permission.PermissionId.Find(permissionId);
                    if (permission != null && permission.IsActive)
                    {
                        permissions.Add(permission);
                    }
                }

                return Content(RenderProfilePage(user, userSettings?.TotpEnabled ?? false, webAuthnCredentials, roles, permissions), "text/html");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading profile page");
                return Redirect($"/api/auth/login?error={Uri.EscapeDataString("Error loading profile: " + ex.Message)}");
            }
        }

        #endregion
// --- File: AuthController.cs (Continued) ---


        #region OIDC Core Endpoints

        //===========================
        // /connect/authorize
        //===========================
        [HttpGet("~/connect/authorize")] // Use route mapping for standard endpoint
        [HttpPost("~/connect/authorize")]
        [AllowAnonymous] // Authentication checked internally via Cookie scheme
        public async Task<IActionResult> Authorize()
        {
            var oidcRequest = HttpContext.GetOpenIddictServerRequest() ??
                              throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

            _logger.LogInformation("OIDC Authorize: Start processing request for ClientId: {ClientId}, ResponseType: {ResponseType}, Scopes: {Scopes}",
                oidcRequest.ClientId, oidcRequest.ResponseType, oidcRequest.Scope);

            // 1. Retrieve the user principal stored in the authentication cookie.
            var authenticateResult = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            // If the user principal cannot be extracted, redirect the user to the login page.
            if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
            {
                _logger.LogInformation("OIDC Authorize: User not authenticated via cookie. Challenging authentication scheme.");
                // Preserve OIDC request parameters in the returnUrl for the login page
                 string returnUrl = Request.Path + Request.QueryString.ToUriComponent(); // Use full path and query
                 var properties = new AuthenticationProperties {
                     RedirectUri = returnUrl // Tell the cookie scheme where to return *after* successful login
                 };
                 // Challenge the cookie scheme, which will redirect to LoginPath (/api/auth/login)
                return Challenge(properties, CookieAuthenticationDefaults.AuthenticationScheme);
            }

            // 3. User IS Authenticated via Cookie
            var userPrincipal = authenticateResult.Principal;
            var userIdString = userPrincipal.FindFirstValue(Claims.Subject); // Expecting Spacetime Identity string
            if (userIdString == null || !Identity.TryParse(userIdString, out var userId)) {
                 _logger.LogError("OIDC Authorize: Cannot extract valid user Spacetime Identity (sub claim) from cookie principal.");
                 await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                 return Redirect($"/api/auth/login?error={Uri.EscapeDataString("Session error. Please log in again.")}");
            }
            _logger.LogInformation("OIDC Authorize: User {UserId} is authenticated via cookie.", userId);

            // Retrieve the application details corresponding to the client_id.
            // Use IOpenIddictApplicationManager for consistency and features.
            var application = await _applicationManager.FindByClientIdAsync(oidcRequest.ClientId!) ??
                              throw new InvalidOperationException($"Client application '{oidcRequest.ClientId}' not registered or inactive.");
            var applicationId = await _applicationManager.GetIdAsync(application) ?? throw new InvalidOperationException("Application OIDC ID missing");

            // Retrieve the permanent authorizations associated with the user and client application.
            // Check Statuses.Valid and Type AuthorizationTypes.Permanent.
            // Crucially, check if the requested scopes are already covered by an existing authorization.
            var authorizations = await _authorizationManager.FindAsync(
                 subject : userIdString,
                 client  : applicationId, // Use the OIDC Application ID
                 status  : Statuses.Valid,
                 type    : AuthorizationTypes.Permanent,
                 scopes  : oidcRequest.GetScopes()
             ).ToListAsync(); // Execute the async enumerable

            // Decide based on consent type and existing authorizations.
            var consentType = await _applicationManager.GetConsentTypeAsync(application);
            bool requiresConsentInteraction = false;

            switch (consentType)
            {
                case ConsentTypes.Explicit:
                     // If no existing authorization *covers all requested scopes*, prompt for consent.
                    if (!authorizations.Any() && oidcRequest.HasScope()) {
                        requiresConsentInteraction = true;
                         _logger.LogInformation("OIDC Authorize: Explicit consent required for user {UserId}, client {ClientId}.", userId, oidcRequest.ClientId);
                     } else {
                         _logger.LogInformation("OIDC Authorize: Existing explicit consent found or no scopes requested for user {UserId}, client {ClientId}.", userId, oidcRequest.ClientId);
                     }
                    break;
                case ConsentTypes.Systematic:
                     requiresConsentInteraction = false; // Consent always granted.
                     _logger.LogInformation("OIDC Authorize: Consent type is Systematic. Auto-approving for user {UserId}, client {ClientId}.", userId, oidcRequest.ClientId);
                    break;
                case ConsentTypes.Implicit:
                    // Grant consent automatically if all scopes are covered by a previous authorization
                    // or if no scopes were requested. This is slightly more nuanced than Systematic.
                     if (!oidcRequest.HasScope() || authorizations.Any()) {
                          requiresConsentInteraction = false;
                          _logger.LogInformation("OIDC Authorize: Consent type is Implicit. Auto-approving (no scopes or existing auth) for user {UserId}, client {ClientId}.", userId, oidcRequest.ClientId);
                     } else {
                          // If implicit, but new scopes requested for which no auth exists, MUST prompt.
                          requiresConsentInteraction = true;
                          _logger.LogInformation("OIDC Authorize: Implicit consent, but new scopes requested requires interaction for user {UserId}, client {ClientId}.", userId, oidcRequest.ClientId);
                     }
                     break;
                case ConsentTypes.External: // Not implemented
                     _logger.LogWarning("OIDC Authorize: External consent type not supported for client {ClientId}.", oidcRequest.ClientId);
                    return Forbid(Errors.ConsentRequired, "External consent required.", oidcRequest);
                default: // Unknown type
                    _logger.LogError("OIDC Authorize: Unsupported consent type '{ConsentType}' for client {ClientId}.", consentType, oidcRequest.ClientId);
                    return Forbid(Errors.ServerError, "Unsupported consent type.", oidcRequest);
            }

             // --- Handle Consent Screen or Proceed ---
             if (requiresConsentInteraction)
             {
                  // Render consent page only for GET requests
                 if (Request.Method == HttpMethods.Get)
                 {
                     _logger.LogInformation("OIDC Authorize: Rendering consent screen for user {UserId}, client {ClientId}.", userId, oidcRequest.ClientId);
                     var clientName = await _applicationManager.GetDisplayNameAsync(application) ?? oidcRequest.ClientId;
                     var scopeDetails = await GetScopeDetailsAsync(oidcRequest.GetScopes());
                      // Pass the OIDC request object itself to the rendering method to extract needed hidden fields
                     return Content(RenderConsentForm(oidcRequest, clientName!, scopeDetails), "text/html");
                 }
                 // Process POST requests from the consent form
                 else if (Request.Method == HttpMethods.Post)
                 {
                     // Check if the "Allow" button (or equivalent form value) was submitted
                     if (!Request.HasFormContentType || !Request.Form.ContainsKey("submit.Accept"))
                     {
                          _logger.LogInformation("OIDC Authorize: Consent denied via POST by user {UserId} for client {ClientId}.", userId, oidcRequest.ClientId);
                          return Forbid(Errors.AccessDenied, "Consent was denied by the resource owner.", oidcRequest);
                     }

                      _logger.LogInformation("OIDC Authorize: Consent granted via POST by user {UserId} for client {ClientId}.", userId, oidcRequest.ClientId);
                     // User clicked "Allow". Proceed to issue tokens/code and save the *permanent* consent.
                     return await IssueTokensAndRedirect(userPrincipal, application, oidcRequest, authorizations, consentGranted: true);
                 }
                 else { return BadRequest("Unsupported HTTP method for consent interaction."); }
             }

             // --- Consent not needed or already exists ---
             _logger.LogInformation("OIDC Authorize: Consent interaction not required. Proceeding for user {UserId}, client {ClientId}.", userId, oidcRequest.ClientId);
            // consentGranted is false because we are using existing consent or it wasn't needed.
            return await IssueTokensAndRedirect(userPrincipal, application, oidcRequest, authorizations, consentGranted: false);
        }

        // Helper to issue tokens/code (Refined)
        private async Task<IActionResult> IssueTokensAndRedirect(ClaimsPrincipal cookiePrincipal, object application, OpenIddictRequest oidcRequest, List<object> existingAuthorizations, bool consentGranted = false)
        {
            var userIdString = cookiePrincipal.FindFirstValue(Claims.Subject); // Spacetime Identity string
            if(userIdString == null) throw new InvalidOperationException("Subject claim missing from cookie principal.");
            var applicationId = await _applicationManager.GetIdAsync(application) ?? throw new InvalidOperationException("Application OIDC ID missing");
            var requestedScopes = oidcRequest.GetScopes();

            // Create the principal for OIDC token generation using data from the authenticated user (cookie)
            var principalForTokens = await CreatePrincipalForOidcTokensAsync(cookiePrincipal, requestedScopes);

            // Set Resources associated with the requested scopes (important for audience validation)
            principalForTokens.SetResources(await GetResourcesAsync(requestedScopes));

            // Set Destinations to control which claims go into which token (ID vs Access)
            principalForTokens.SetDestinations(GetDestinations);

            // --- Handle Persistence of Consent (Authorization) ---
            // Find or create the OpenIddict authorization entry (represents user's grant to client)
            var authorization = existingAuthorizations.LastOrDefault(); // Check if suitable auth already exists

            // If explicit consent was just given OR if systematic/implicit consent applies and no perm auth exists yet
             bool shouldCreateOrUpdatePermanentAuth = (consentGranted && await _applicationManager.GetConsentTypeAsync(application) == ConsentTypes.Explicit) ||
                                                     ((await _applicationManager.GetConsentTypeAsync(application) == ConsentTypes.Systematic ||
                                                       await _applicationManager.GetConsentTypeAsync(application) == ConsentTypes.Implicit) &&
                                                      !authorizations.Any(auth => ((OpenIddictAuthorizationDescriptor)auth).Type == AuthorizationTypes.Permanent) // Check if *any* perm exists
                                                     );


             if (shouldCreateOrUpdatePermanentAuth) {
                 // Use Authorization Manager to handle creation/update idempotently
                 authorization = await _authorizationManager.FindBySubjectAsync(subject: userIdString, client: applicationId).FirstOrDefaultAsync(); // Check again by user/client

                 var descriptor = new OpenIddictAuthorizationDescriptor {
                      Subject = userIdString,
                      ApplicationId = applicationId, // OIDC Application ID
                      Status = Statuses.Valid,
                      Type = AuthorizationTypes.Permanent,
                      Scopes = requestedScopes // Grant the currently requested scopes
                 };

                  if (authorization == null) {
                      authorization = await _authorizationManager.CreateAsync(descriptor, CancellationToken.None);
                       _logger.LogInformation("Created new permanent authorization for user {UserId}, client {ClientId}.", userIdString, oidcRequest.ClientId);
                  } else {
                       // Ensure existing authorization has the requested scopes
                       await _authorizationManager.SetScopesAsync(authorization, requestedScopes, CancellationToken.None);
                       await _authorizationManager.UpdateAsync(authorization, CancellationToken.None);
                       _logger.LogInformation("Updated scopes on existing permanent authorization for user {UserId}, client {ClientId}.", userIdString, oidcRequest.ClientId);
                  }
             }

            // Associate the authorization with the principal if it exists.
            // This allows OpenIddict to link tokens back to the authorization grant.
             if (authorization != null) {
                  var authorizationId = await _authorizationManager.GetIdAsync(authorization);
                  if (authorizationId != null) principalForTokens.SetAuthorizationId(authorizationId);
             }

            // Set requested scopes on the principal.
            principalForTokens.SetScopes(requestedScopes);

            _logger.LogInformation("OIDC Authorize: Signing in user {UserId} via OIDC server scheme to issue tokens/code.", userIdString);
            // Call SignIn with the OIDC scheme. OpenIddict handles generating the
            // authorization code, access token, identity token, and/or refresh token
            // based on the request and configuration, then performs the redirect.
            return SignIn(principalForTokens, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        //===========================
        // /connect/token
        //===========================
        [HttpPost("~/connect/token"), Produces("application/json")] // Standard OIDC route
        [AllowAnonymous]
        [IgnoreAntiforgeryToken] // Standard practice for token endpoints
        public async Task<IActionResult> Exchange()
        {
            var oidcRequest = HttpContext.GetOpenIddictServerRequest() ??
                               throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

            _logger.LogInformation("OIDC Token: Processing request for GrantType: {GrantType}, ClientId: {ClientId}", oidcRequest.GrantType, oidcRequest.ClientId);

            ClaimsPrincipal? principal = null;
            UserProfile? user = null; // Store UserProfile if grant involves a user

            // --- Handle Authorization Code Grant ---
            if (oidcRequest.IsAuthorizationCodeGrantType())
            {
                _logger.LogDebug("OIDC Token: Processing Authorization Code grant.");
                var principalResult = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
                if (!principalResult.Succeeded || principalResult.Principal == null) {
                    return Forbid(Errors.InvalidGrant, "The authorization code grant is invalid or expired.", oidcRequest);
                }
                principal = principalResult.Principal;
                 _logger.LogDebug("OIDC Token: Principal authenticated from Authorization Code.");
            }
            // --- Handle Refresh Token Grant ---
            else if (oidcRequest.IsRefreshTokenGrantType())
            {
                 _logger.LogDebug("OIDC Token: Processing Refresh Token grant.");
                 var principalResult = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
                 if (!principalResult.Succeeded || principalResult.Principal == null) {
                     return Forbid(Errors.InvalidGrant, "The refresh token is invalid or expired.", oidcRequest);
                 }
                 principal = principalResult.Principal;
                 _logger.LogDebug("OIDC Token: Principal authenticated from Refresh Token.");
            }
            // --- Handle Password Grant (ROPC - Legacy) ---
            else if (oidcRequest.IsPasswordGrantType())
            {
                 _logger.LogInformation("OIDC Token: Processing Password grant (ROPC) for user {Username}", oidcRequest.Username);
                 // Validate client allows ROPC
                 var application = await _applicationManager.FindByClientIdAsync(oidcRequest.ClientId!) ?? throw new InvalidOperationException("Client not found.");
                 if (!await _applicationManager.HasGrantTypeAsync(application, GrantTypes.Password)) {
                     return Forbid(Errors.UnsupportedGrantType, "Password grant type not allowed for this client.", oidcRequest);
                 }
                  // Authenticate using SpacetimeDB service (NO 2FA for ROPC usually)
                 var (authSuccess, userProfile, _, errorMessage) = await ProcessLoginAndGetData(new LoginRequest { Username = oidcRequest.Username!, Password = oidcRequest.Password!, SkipTwoFactor = true });
                 if (!authSuccess || userProfile == null) {
                     return Forbid(Errors.InvalidGrant, errorMessage ?? "Invalid username or password.", oidcRequest);
                 }
                 user = userProfile; // Store user profile for later validation
                 principal = await CreatePrincipalForOidcTokensAsync(userProfile, oidcRequest.GetScopes()); // Create principal from user data
                 _logger.LogInformation("OIDC Token: Password grant authentication successful for {Username}.", user.Login);
            }
            // --- Handle Client Credentials Grant ---
             else if (oidcRequest.IsClientCredentialsGrantType())
             {
                 _logger.LogInformation("OIDC Token: Processing Client Credentials grant for client {ClientId}", oidcRequest.ClientId);
                 var application = await _applicationManager.FindByClientIdAsync(oidcRequest.ClientId!) ?? throw new InvalidOperationException("Client not found.");

                 // Client authentication is handled by OpenIddict before this point (via secret or other method)
                 // Create a principal representing the client application.
                 var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
                 // IMPORTANT: Subject for Client Credentials MUST be the ClientId
                 identity.SetClaim(Claims.Subject, oidcRequest.ClientId);
                 identity.SetClaim(Claims.ClientId, oidcRequest.ClientId);
                 identity.SetClaim(Claims.Name, await _applicationManager.GetDisplayNameAsync(application)); // Optional: include display name

                  // Add other claims specific to the client if needed
                 // identity.AddClaim("gty", GrantTypes.ClientCredentials);

                  principal = new ClaimsPrincipal(identity);
                  principal.SetScopes(oidcRequest.GetScopes()); // Set granted scopes
                   _logger.LogInformation("OIDC Token: Client Credentials principal created for {ClientId}.", oidcRequest.ClientId);
             }
            else { return Forbid(Errors.UnsupportedGrantType, "The specified grant type is not supported.", oidcRequest); }

            // --- Post-Authentication/Principal Retrieval Validation ---
            if (principal == null) { return Forbid(Errors.InvalidRequest, "Could not establish principal for token generation.", oidcRequest); }

            var userIdString = principal.FindFirstValue(Claims.Subject);

             // For USER-based grants (Auth Code, Refresh, Password), validate the user still exists and is active.
            if (!oidcRequest.IsClientCredentialsGrantType())
            {
                 if (user == null) { // If user wasn't already fetched (e.g., Auth Code/Refresh Token flow)
                      user = await GetUserByIdentityStringAsync(userIdString);
                 }
                 if (user == null || !user.IsActive) {
                      return Forbid(Errors.InvalidGrant, "The user associated with the grant is no longer valid.", oidcRequest);
                 }
                 // TODO: Optional: Check for changes that should invalidate the grant (e.g., security stamp, password change)
                  // if (principal.GetClaim(Claims.SecurityStamp) != await _userManager.GetSecurityStampAsync(user))
                  //    return Forbid(Errors.InvalidGrant, "User security stamp mismatch.", oidcRequest);
            }

            // --- Issue Tokens ---
            // Re-apply destinations and resources (scopes are already on the principal)
            principal.SetDestinations(GetDestinations);
            principal.SetResources(await GetResourcesAsync(principal.GetScopes()));

            _logger.LogInformation("OIDC Token: Grant type {GrantType} validated. Issuing tokens for Subject: {Subject}", oidcRequest.GrantType, userIdString);
            // Call SignIn with the OIDC scheme. OpenIddict generates the appropriate tokens based on the principal and grant type.
            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }


        //===========================
        // /connect/userinfo
        //===========================
        [Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)] // Secure with OIDC Bearer token validation
        [HttpGet("~/connect/userinfo"), Produces("application/json")]
        public async Task<IActionResult> Userinfo()
        {
            _logger.LogDebug("OIDC UserInfo: Request received.");
            // User principal is populated by the [Authorize] attribute after token validation.
            var userIdString = User.FindFirstValue(Claims.Subject); // Get user ID (Spacetime Identity) from validated token
            if (userIdString == null || !Identity.TryParse(userIdString, out var userId)) {
                 _logger.LogWarning("OIDC UserInfo: Invalid or missing 'sub' claim in access token.");
                 return Challenge(Errors.InvalidToken, "Invalid subject claim in token.");
            }

            var user = await GetUserByIdentityAsync(userId); // Fetch fresh user details
            if (user == null) {
                 _logger.LogWarning("OIDC UserInfo: User {UserId} associated with token not found.", userId);
                 return Challenge(Errors.InvalidToken, "User not found.");
            }

             // Build claims based on scopes present in the *validated access token* (User.HasScope)
            var claims = new Dictionary<string, object>(StringComparer.Ordinal) {
                [Claims.Subject] = user.UserId.ToString() // REQUIRED
            };

            // Standard OIDC Scopes/Claims Mapping
            if (User.HasScope(Scopes.Profile)) {
                claims[Claims.Name] = user.Login;
                claims[Claims.PreferredUsername] = user.Login;
                 // Add other profile claims if available (standard OIDC names):
                 // claims[Claims.FamilyName] = user.Surname;
                 // claims[Claims.GivenName] = user.Name;
                 // claims[Claims.MiddleName] = user.Patronym;
                 // claims[Claims.UpdatedAt] = user.UpdatedAt.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds((long)user.UpdatedAt.Value).ToUnixTimeSeconds() : (long?)null; // Example: Needs Unix seconds
                 // claims[Claims.Picture] = user.PhotoUrl;
                  // claims[Claims.Birthdate] = user.DateOfBirth.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds((long)user.DateOfBirth.Value).ToString("yyyy-MM-dd") : null; // Format birthdate if available
             }
            if (User.HasScope(Scopes.Email)) {
                if (!string.IsNullOrEmpty(user.Email)) claims[Claims.Email] = user.Email;
                claims[Claims.EmailVerified] = user.EmailConfirmed ?? false;
            }
            if (User.HasScope(Scopes.Phone)) {
                 if (!string.IsNullOrEmpty(user.PhoneNumber)) claims[Claims.PhoneNumber] = user.PhoneNumber;
                 // claims[Claims.PhoneNumberVerified] = ...; // Add if available
            }
             if (User.HasScope(Scopes.Roles)) {
                  // Use helper to get role names directly
                  claims[Claims.Role] = GetUserRolesFromDb(user.UserId);
             }

            // Custom Scopes/Claims Mapping
             if (User.HasScope("api") || User.HasScope("permissions")) {
                 claims["permissions"] = GetUserPermissionsFromDb(user.UserId); // Use helper
                 claims["legacy_user_id"] = user.LegacyUserId;
                 if(user.Xuid.HasValue) claims["xuid"] = user.Xuid.Value;
             }
             if (User.HasScope("identity")) { // Custom scope to expose Spacetime Identity
                 claims["identity"] = user.UserId.ToString();
             }

            _logger.LogInformation("OIDC UserInfo: Returning claims for user {UserId}", userId);
            return Ok(claims);
        }

        //===========================
        // /connect/logout (OIDC Logout)
        //===========================
        [HttpGet("~/connect/logout")] // Standard OIDC route
        [HttpPost("~/connect/logout")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken] // Usually safe for logout, but review security needs
        public async Task<IActionResult> OidcLogoutEndpoint()
        {
            _logger.LogInformation("Processing OIDC Logout request.");

            // Ask OpenIddict to perform the logout operation.
            // This might involve validating the id_token_hint and post_logout_redirect_uri.
            // It typically signs out the principal associated with the OIDC scheme if applicable.
            var result = SignOut(
                 authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                 properties: new AuthenticationProperties { RedirectUri = "/" } // Fallback redirect if no post_logout_uri is validated
            );

            // **Sign out from the application's session cookie scheme.**
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            _logger.LogInformation("OIDC Logout: Signed out from Cookie Authentication Scheme.");

            // Retrieve the validated post-logout redirect URI from the SignOutResult properties.
            // OpenIddict populates this *after* validating it against the client's registered URIs.
            string? postLogoutRedirectUri = result.Properties?.RedirectUri;

            // If a validated URI exists, redirect the user agent.
            if (!string.IsNullOrEmpty(postLogoutRedirectUri))
            {
                 _logger.LogInformation("OIDC Logout: Redirecting to validated post-logout URI: {PostLogoutRedirectUri}", postLogoutRedirectUri);
                 // Perform the redirect.
                 return Redirect(postLogoutRedirectUri);
            }

            // If no valid post_logout_redirect_uri or if it's an API request, return confirmation.
            if (IsBrowserRequest())
            {
                 return Content(RenderLogoutConfirmation(), "text/html");
            }

            return Ok(new ApiResponse<object> { Success = true, Message = "Logout successful" });
        }


        #endregion

        // --- File: AuthController.cs (Continued) ---


        #region Existing Auth Methods (TOTP, WebAuthn, MagicLink, QR, Claim)

        //===========================
        // TOTP
        //===========================
        [HttpGet("totp/setup")]
        [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)] // Secure with cookie
        public async Task<IActionResult> TotpSetup()
        {
            var userId = GetUserIdentity();
            if (userId == null) { return Unauthorized(new ApiResponse<object> { Success = false, Message = "User not authenticated." }); }
            var user = await GetUserByIdentityAsync(userId);
            if (user == null) { return NotFound(new ApiResponse<object> { Success = false, Message = "User not found." }); }

            var (success, secretKey, qrCodeUri, errorMessage) = await _totpService.SetupTotpAsync(userId.Value, user.Login);

            if (!success || string.IsNullOrEmpty(secretKey) || string.IsNullOrEmpty(qrCodeUri))
            {
                _logger.LogWarning("Failed to initiate TOTP setup for user {UserId}: {Error}", userId, errorMessage);
                if (IsBrowserRequest()) return Redirect($"/api/auth/profile?error={Uri.EscapeDataString(errorMessage ?? "Failed TOTP setup.")}");
                return BadRequest(new ApiResponse<object>{ Success=false, Message=errorMessage ?? "Failed TOTP setup." });
            }

             _logger.LogInformation("Generated TOTP setup info for user {UserId}", userId);
            if (IsBrowserRequest()) {
                 return Content(RenderTotpSetup(qrCodeUri, secretKey), "text/html");
            }
            return Ok(new ApiResponse<TotpSetupResponse>{ Success=true, Message="TOTP setup initiated.", Data = new TotpSetupResponse { SecretKey=secretKey, QrCodeUri=qrCodeUri } });
        }

        [HttpPost("totp/verify")]
        [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)] // Secure with cookie
        [ValidateAntiForgeryToken] // Protect form posts
        public async Task<IActionResult> VerifyTotp([FromForm] VerifyTotpRequest request) // Expect Form data from HTML page
        {
             var userId = GetUserIdentity();
             if (userId == null) { return Unauthorized("User not authenticated."); }

              // Basic validation
             if (string.IsNullOrWhiteSpace(request?.Code) || string.IsNullOrWhiteSpace(request?.SecretKey)) {
                   string errorMsg = "Verification code and secret key are required.";
                   if(IsBrowserRequest()) return Content(RenderTotpSetup("javascript:void(0);", request?.SecretKey ?? "SECRET_MISSING") /* Need a way to re-render with error */, "text/html"); // Re-rendering is complex here
                   return BadRequest(new ApiResponse<object> { Success = false, Message = errorMsg });
              }


             var (success, errorMessage) = await _totpService.EnableTotpAsync(userId.Value, request.Code, request.SecretKey);

             if (!success) {
                   _logger.LogWarning("TOTP verification failed for user {UserId}: {Error}", userId, errorMessage);
                   if(IsBrowserRequest()) {
                        // Redirect back to profile with error - can't easily show setup page again with same QR/secret
                        return Redirect($"/api/auth/profile?error={Uri.EscapeDataString(errorMessage ?? "Invalid verification code.")}");
                   }
                  return BadRequest(new ApiResponse<object>{ Success=false, Message=errorMessage ?? "Invalid verification code."});
             }

             _logger.LogInformation("TOTP successfully enabled for user {UserId}", userId);
             if(IsBrowserRequest()) {
                  return Redirect("/api/auth/profile?message=TOTP Enabled Successfully");
             }
             return Ok(new ApiResponse<VerifyTotpResponse>{ Success=true, Message="TOTP enabled.", Data=new VerifyTotpResponse { Enabled=true }});
        }

        [HttpPost("totp/disable")]
        [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)] // Secure with cookie
        [ValidateAntiForgeryToken] // Protect form posts if called via form
        public async Task<IActionResult> DisableTotp() // Re-added this endpoint
        {
            var userId = GetUserIdentity();
            if (userId == null) { return Unauthorized(new ApiResponse<object> { Success = false, Message = "User not authenticated." }); }

            var (success, errorMessage) = await _totpService.DisableTotpAsync(userId.Value);

            if (!success) {
                _logger.LogWarning("Failed to disable TOTP for user {UserId}: {Error}", userId, errorMessage);
                 if (IsBrowserRequest()) {
                      return Redirect($"/api/auth/profile?error={Uri.EscapeDataString(errorMessage ?? "Failed to disable TOTP.")}");
                 }
                return BadRequest(new ApiResponse<object>{ Success=false, Message=errorMessage ?? "Failed to disable TOTP."});
            }

            _logger.LogInformation("TOTP disabled successfully for user {UserId}", userId);
            if(IsBrowserRequest()) {
                 return Redirect("/api/auth/profile?message=TOTP Disabled");
            }
            return Ok(new ApiResponse<DisableTotpResponse>{ Success=true, Message="TOTP disabled.", Data=new DisableTotpResponse{ Disabled=true }});
        }

        [HttpPost("totp/validate")] // For 2FA step after initial login (API primarily)
        [AllowAnonymous]
        public async Task<ActionResult<ValidateTotpResponse>> ValidateTotp([FromBody] ValidateTotpRequest request)
        {
             _logger.LogInformation("Attempting TOTP validation using TempToken.");
              if (!ModelState.IsValid) { return BadRequest(new ApiResponse<object> { Success = false, Message = "Invalid request." }); }

             var (success, errorMessage) = await _totpService.ValidateTotpWithTokenAsync(request.TempToken, request.Code);
             if (!success) {
                  _logger.LogWarning("TOTP validation failed for TempToken {TempToken}: {Error}", request.TempToken, errorMessage);
                 return BadRequest(new ApiResponse<object>{Success=false, Message=errorMessage ?? "Invalid code or token."});
             }

             // Retrieve user associated with the validated temp token
             var user = await GetUserFromTempToken(request.TempToken);
             if(user == null) {
                  _logger.LogError("TOTP validation succeeded for TempToken {TempToken} but failed to retrieve associated user.", request.TempToken);
                  return BadRequest(new ApiResponse<object>{Success=false, Message="User session error after validation."});
             }

             // Mark temp token used in SpacetimeDB via reducer
             try {
                 var conn = GetConnection();
                 var tokenRecord = conn.Db.TwoFactorToken.Iter().FirstOrDefault(t => t.Token == request.TempToken);
                  if (tokenRecord != null) {
                      conn.Reducers.UpdateTwoFactorToken(tokenRecord.Id, tokenRecord.UserId, tokenRecord.Token, true, tokenRecord.ExpiresAt);
                  } else { _logger.LogWarning("Could not find temp token {TempToken} in DB to mark as used.", request.TempToken); }
             } catch(Exception ex) { _logger.LogError(ex, "Failed to mark temp token {TempToken} as used.", request.TempToken); }


             // Login Success after 2FA: Return legacy JWT for API clients
             _logger.LogInformation("TOTP validation successful for user {Username}. Issuing legacy token.", user.Login);
             var token = GenerateJwtToken(user);
             return Ok(new ApiResponse<ValidateTotpResponse> {
                 Success = true, Message = "TOTP validation successful",
                 Data = new ValidateTotpResponse { Token = token, User = MapUserToDto(user) }
             });
             // Note: Browser flow after 2FA needs redirect handling based on the stored 'returnUrl' from the temp token or initial login state.
             // This might require storing the returnUrl along with the temp token.
        }

        // --- WebAuthn ---
        [HttpGet("webauthn/register/options_page")] // Endpoint to display the registration page
        [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
        public async Task<IActionResult> WebAuthnRegisterPage() {
            var userId = GetUserIdentity(); if (userId == null) return Unauthorized();
            var user = await GetUserByIdentityAsync(userId); if (user == null) return NotFound();

             var (success, options, errorMessage) = await _webAuthnService.GetCredentialCreateOptionsAsync(userId.Value, user.Login);
             if (!success || options == null) { return Content(RenderErrorPage(errorMessage ?? "Failed to get WebAuthn options"), "text/html"); }

            try {
                 // Serialize options carefully for embedding in HTML
                 var optionsJson = JsonSerializer.Serialize(options, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                 return Content(RenderWebAuthnRegistration(optionsJson), "text/html");
            } catch (JsonException jsonEx) {
                 _logger.LogError(jsonEx, "Failed to serialize WebAuthn options for user {UserId}.", userId);
                 return Content(RenderErrorPage("Internal server error preparing security key registration."), "text/html");
            }
        }

        [HttpPost("webauthn/register/options")] // API endpoint to GET options
        [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)] // Secure with cookie
        public async Task<ActionResult<WebAuthnRegisterOptionsResponse>> GetWebAuthnRegisterOptions() {
            var userId = GetUserIdentity(); if (userId == null) return Unauthorized(/*...*/);
            var user = await GetUserByIdentityAsync(userId); if (user == null) return NotFound(/*...*/);
            var (success, options, errorMessage) = await _webAuthnService.GetCredentialCreateOptionsAsync(userId.Value, user.Login);
            if (!success || options == null) return BadRequest(new ApiResponse<object>{ Success=false, Message=errorMessage});
            return Ok(new ApiResponse<WebAuthnRegisterOptionsResponse>{ Success=true, Data = new WebAuthnRegisterOptionsResponse { Options = options } });
        }

        [HttpPost("webauthn/register/complete")] // Receives data from browser JS
        [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)] // Secure with cookie
        public async Task<ActionResult<WebAuthnRegisterCompleteResponse>> CompleteWebAuthnRegistration([FromBody] WebAuthnRegisterCompleteRequest request) {
            var userId = GetUserIdentity(); if (userId == null) return Unauthorized(/*...*/);
            var user = await GetUserByIdentityAsync(userId); if (user == null) return NotFound(/*...*/);
            // Need to convert Base64Url strings back to byte[] for Fido2NetLib if necessary in CompleteRegistrationAsync
            // Example: request.AttestationResponse.RawId = Base64UrlEncoder.DecodeBytes(request.AttestationResponse.Id);
            // Ensure your service handles the AttestationResponse object correctly.
            var (success, errorMessage) = await _webAuthnService.CompleteRegistrationAsync(userId.Value, user.Login, request.AttestationResponse);
            if (!success) return BadRequest(new ApiResponse<object>{ Success=false, Message=errorMessage });
             // For browser: Indicate success (JS on the page might handle redirect or message)
             // For API: Return success response
             _logger.LogInformation("WebAuthn key registered for user {UserId}", userId);
            return Ok(new ApiResponse<WebAuthnRegisterCompleteResponse>{ Success=true, Message="Security key registered.", Data = new WebAuthnRegisterCompleteResponse { Registered = true } });
        }

        [HttpPost("webauthn/login/options")] // API endpoint for login challenge
        [AllowAnonymous]
        public async Task<ActionResult<WebAuthnLoginOptionsResponse>> GetWebAuthnLoginOptions([FromBody] WebAuthnLoginOptionsRequest request) {
            var (success, options, errorMessage) = await _webAuthnService.GetAssertionOptionsAsync(request.Username);
            if (!success || options == null) return BadRequest(new ApiResponse<object>{ Success=false, Message=errorMessage});
            return Ok(new ApiResponse<WebAuthnLoginOptionsResponse>{ Success=true, Data = new WebAuthnLoginOptionsResponse { Options = options } });
        }

        [HttpPost("webauthn/login/complete")] // API endpoint for standalone WebAuthn Login
        [AllowAnonymous]
        public async Task<ActionResult<WebAuthnLoginCompleteResponse>> CompleteWebAuthnLogin([FromBody] WebAuthnLoginCompleteRequest request) {
            var (success, user, errorMessage) = await _webAuthnService.CompleteAssertionAsync(request.Username, request.AssertionResponse);
            if (!success || user == null) return BadRequest(new ApiResponse<object>{ Success=false, Message=errorMessage});
            var token = GenerateJwtToken(user); // Legacy token
            return Ok(new ApiResponse<WebAuthnLoginCompleteResponse> { Success=true, Message="Login successful.", Data = new WebAuthnLoginCompleteResponse { Token=token, User = MapUserToDto(user) } });
        }

        [HttpPost("webauthn/validate")] // 2FA Step
        [AllowAnonymous]
        public async Task<ActionResult<WebAuthnValidateResponse>> ValidateWebAuthn([FromBody] WebAuthnValidateRequest request) {
             var user = await GetUserFromTempToken(request.TempToken);
             if (user == null) return BadRequest(new ApiResponse<object>{Success=false, Message="Invalid user session"});
             // Need to handle potential byte array conversions for request.AssertionResponse if base64 encoded
             var (success, _, errorMessage) = await _webAuthnService.CompleteAssertionAsync(user.Login, request.AssertionResponse);
             if (!success) return BadRequest(new ApiResponse<object>{ Success=false, Message=errorMessage ?? "WebAuthn validation failed"});
              // Mark temp token used... (via reducer)
             var token = GenerateJwtToken(user); // Final login token
             return Ok(new ApiResponse<WebAuthnValidateResponse>{ Success=true, Message="2FA successful.", Data = new WebAuthnValidateResponse { Token=token, User = MapUserToDto(user) } });
        }

        [HttpGet("webauthn/credentials")]
        [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
        public async Task<ActionResult<WebAuthnCredentialsResponse>> GetWebAuthnCredentials() {
            var userId = GetUserIdentity(); if (userId == null) return Unauthorized(/*...*/);
            var credentials = await _webAuthnService.GetUserCredentialsAsync(userId.Value);
            return Ok(new ApiResponse<WebAuthnCredentialsResponse>{ Success=true, Data = new WebAuthnCredentialsResponse { Credentials = credentials.Select(c => new WebAuthnCredentialDto { Id=Base64UrlEncoder.Encode(c.CredentialId.ToArray()), CreatedAt=DateTimeOffset.FromUnixTimeMilliseconds((long)c.CreatedAt).DateTime }).ToList() } });
        }

        [HttpDelete("webauthn/credentials/{base64UrlId}")] // DELETE API endpoint
        [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
        public async Task<ActionResult<WebAuthnRemoveCredentialResponse>> RemoveWebAuthnCredential(string base64UrlId) {
             var userId = GetUserIdentity(); if (userId == null) return Unauthorized(/*...*/);
             // Note: _webAuthnService.RemoveCredentialAsync should handle decoding the Base64Url ID internally
             var (success, errorMessage) = await _webAuthnService.RemoveCredentialAsync(userId.Value, base64UrlId);
             if (!success) return BadRequest(new ApiResponse<object>{ Success=false, Message=errorMessage });
             return Ok(new ApiResponse<WebAuthnRemoveCredentialResponse>{ Success=true, Data = new WebAuthnRemoveCredentialResponse{ Removed=true } });
        }

        [HttpPost("webauthn/credentials/{base64UrlId}")] // FORM POST from profile page
        [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveWebAuthnCredentialForm(string base64UrlId, [FromForm] string? _method) {
             if (!IsBrowserRequest() || _method != "DELETE") return BadRequest();
             var userId = GetUserIdentity(); if (userId == null) return Unauthorized();

             var (success, errorMessage) = await _webAuthnService.RemoveCredentialAsync(userId.Value, base64UrlId); // Call service

             if (!success) return Redirect($"/api/auth/profile?error={Uri.EscapeDataString(errorMessage ?? "Failed to remove key.")}");
             return Redirect("/api/auth/profile?message=Security Key Removed");
        }

        // --- Magic Link ---
        [HttpGet("magic-link")]
        [AllowAnonymous]
        public IActionResult MagicLinkPage([FromQuery] string? error = null, [FromQuery] string? message = null) {
            if (!IsBrowserRequest()) return BadRequest("Use POST to request magic link.");
            return Content(RenderMagicLinkForm(error, message), "text/html");
         }

        [HttpPost("magic-link/send")]
        [AllowAnonymous]
        [Consumes("application/json", "application/x-www-form-urlencoded")]
        public async Task<IActionResult> SendMagicLink([FromBody] MagicLinkRequest? jsonRequest = null, [FromForm] MagicLinkRequest? formRequest = null) {
            var request = formRequest ?? jsonRequest;
             if (request == null || string.IsNullOrWhiteSpace(request.Email)) return BadRequest(/* ... */);
            var userAgent = Request.Headers.UserAgent.ToString();
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var (success, errorMessage) = await _magicLinkService.SendMagicLinkAsync(request.Email, userAgent, ipAddress);
            if (IsBrowserRequest()) return Content(RenderMagicLinkForm(success ? null : errorMessage, success ? "Magic link sent." : null), "text/html");
            if (!success) return BadRequest(/*...*/);
            return Ok(new ApiResponse<MagicLinkResponse>{ /*...*/});
        }

        [HttpGet("validate-magic-link")] // Browser clicks link
        [AllowAnonymous]
        public async Task<ActionResult> ValidateMagicLink([FromQuery] string token) {
            var (success, user, errorMessage) = await _magicLinkService.ValidateMagicLinkAsync(token);
            if (!success || user == null) return Redirect($"/api/auth/error?message={Uri.EscapeDataString(errorMessage ?? "Invalid link.")}");
            await _magicLinkService.MarkMagicLinkAsUsedAsync(token);
            var principal = await CreatePrincipalForSessionAndOidcAsync(user);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = true });
            // Redirect to profile after cookie sign-in
            return Redirect("/api/auth/success?message=Login successful!"); // Redirect to generic success page
        }

        [HttpPost("validate-magic-link")] // API validates token
        [AllowAnonymous]
        public async Task<ActionResult<ValidateMagicLinkResponse>> ValidateMagicLinkApi([FromBody] ValidateMagicLinkRequest request) {
            var (success, user, errorMessage) = await _magicLinkService.ValidateMagicLinkAsync(request.Token);
            if (!success || user == null) return BadRequest(/*...*/);
            await _magicLinkService.MarkMagicLinkAsUsedAsync(request.Token);
            var legacyToken = GenerateJwtToken(user);
            return Ok(new ApiResponse<ValidateMagicLinkResponse>{ /*...*/ Data = new ValidateMagicLinkResponse { Token=legacyToken, User=MapUserToDto(user) } });
        }

        // --- QR Code ---
        [HttpGet("qr/generate")] // Generate QR for logged-in user (e.g., for mobile app pairing)
        [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
        public async Task<ActionResult<QrCodeResponse>> GenerateQRCode() {
            var userId = GetUserIdentity(); if (userId == null) return Unauthorized();
            var user = await GetUserByIdentityAsync(userId); if (user == null) return NotFound();
             (string qrCodeBase64, string rawData) = await _qrAuthService.GenerateQRCodeWithDataAsync(user); // Generates QR containing user info/token
             return Ok(new ApiResponse<QrCodeResponse>{ /*...*/ Data = new QrCodeResponse{ QrCode = qrCodeBase64, RawData = rawData } });
        }

        [HttpGet("qr/login")] // Renders QR page for *desktop* to be scanned by mobile
        [AllowAnonymous]
        public async Task<IActionResult> QrLoginPage() {
            if (!IsBrowserRequest()) return BadRequest("Use POST for API QR login.");
             var deviceId = Guid.NewGuid().ToString("N");
             // Generate QR code for direct login, containing the deviceId for polling
             var (qrCodeBase64, rawData) = await _qrAuthService.GenerateDirectLoginQRCodeAsync("", "desktop-" + deviceId); // User determined by mobile scan
             _logger.LogDebug("Generated QR code for desktop session {DeviceId}", deviceId);
            return Content(RenderQrLogin(qrCodeBase64, deviceId), "text/html"); // Pass deviceId to polling script
        }

        // POST qr/login might be legacy/unused if direct flow is primary
        [HttpPost("qr/login")]
        [AllowAnonymous]
        public async Task<ActionResult<QrLoginResponse>> QRLogin([FromBody] QrLoginRequest request) {
             _logger.LogWarning("Legacy POST /api/auth/qr/login called."); // This flow seems less likely now
             var user = await _authService.AuthenticateDirectQRAsync(request.Username, request.Token); // Assumes token contains username/password indirectly
             if (user == null) return Unauthorized(/*...*/);
             var token = GenerateJwtToken(user);
             return Ok(new ApiResponse<QrLoginResponse>{ /*...*/ Data = new QrLoginResponse{ Token = token, User = MapUserToDto(user)} });
        }

        [HttpGet("qr/direct/generate")] // Mobile gets QR *to show* to desktop scanner (less common flow)
        [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)] // Secure with cookie
        public async Task<ActionResult<DirectQrCodeResponse>> GenerateDirectLoginQRCode([FromQuery] string deviceType = "mobile") {
             var userId = GetUserIdentity(); if(userId == null) return Unauthorized();
             var user = await GetUserByIdentityAsync(userId); if(user == null) return NotFound();
             var (qrCode, rawData) = await _qrAuthService.GenerateDirectLoginQRCodeAsync(user.Login, deviceType);
             return Ok(new ApiResponse<DirectQrCodeResponse>{Success=true, Data = new DirectQrCodeResponse{QrCode=qrCode, RawData=rawData}});
        }

        [HttpPost("qr/direct/login")] // Mobile *scans* desktop QR and sends the token back
        [AllowAnonymous]
        public async Task<ActionResult<DirectQrLoginResponse>> DirectQRLogin([FromBody] DirectQrLoginRequest request) {
            _logger.LogInformation("Processing Direct QR Login. DeviceType: {DeviceType}, IsDesktopLogin: {IsDesktop}", request.DeviceType, request.IsDesktopLogin);
             var (success, user, deviceId) = await _qrAuthService.ValidateDirectLoginTokenAsync(request.Token, request.DeviceType);
             if (!success || user == null) return Unauthorized(new ApiResponse<object>{ Success=false, Message="Invalid QR token"});
             // No password auth needed here, QR token implies auth for this device/session
             var legacyToken = GenerateJwtToken(user); // Issue token for the *mobile* device

             // If mobile is logging in *for* desktop, notify the desktop via cache/polling
             if (request.DeviceType == "mobile" && !string.IsNullOrEmpty(deviceId) && request.IsDesktopLogin) {
                 _logger.LogDebug("Notifying desktop session {DeviceId} of successful mobile QR scan.", deviceId);
                 // Store the generated legacy token for the desktop to pick up via polling
                  bool notified = await _qrAuthService.NotifyDeviceLoginSuccessAsync(deviceId, legacyToken);
                  if(!notified) _logger.LogWarning("Failed to store notification token for desktop device {DeviceId}", deviceId);
             }

             return Ok(new ApiResponse<DirectQrLoginResponse>{ Success=true, Data = new DirectQrLoginResponse{ Token = legacyToken, DeviceId = deviceId, User = MapUserToDto(user) } });
        }

        [HttpGet("qr/direct/check")] // Desktop *polls* this after showing QR
        [AllowAnonymous]
        public async Task<ActionResult<DirectQrCheckResponse>> CheckDirectLoginStatus([FromQuery] string deviceId) {
             _logger.LogTrace("Checking direct QR login status for device {DeviceId}", deviceId);
             var (authenticated, token, user) = await _qrAuthService.CheckDeviceLoginStatusAsync(deviceId); // Use new method

             if (!authenticated) {
                 return Ok(new ApiResponse<DirectQrCheckResponse> { Success = true, Message = "Login not yet confirmed.", Data = new DirectQrCheckResponse { Authenticated = false }});
             }

              // Login confirmed by mobile scan, return token and user details
             _logger.LogInformation("Direct QR login confirmed for device {DeviceId}. Returning token.", deviceId);
             return Ok(new ApiResponse<DirectQrCheckResponse> {
                 Success = true, Message = "Login successful",
                 Data = new DirectQrCheckResponse { Authenticated = true, Token = token, User = user != null ? MapUserToDto(user) : null }
             });
        }

        // --- Account Claim ---
        [HttpGet("claim-account")]
        [AllowAnonymous]
        public IActionResult ClaimAccountPage([FromQuery] string? error = null, [FromQuery] string? message = null) {
             if (!IsBrowserRequest()) return BadRequest("Use POST for API account claim.");
             return Content(RenderClaimAccountForm(error, message), "text/html");
        }

        [HttpPost("claim-account")]
        [AllowAnonymous]
        [Consumes("application/json", "application/x-www-form-urlencoded")]
        public async Task<IActionResult> ClaimAccount([FromBody] ClaimAccountRequest? jsonRequest = null, [FromForm] ClaimAccountRequest? formRequest = null) {
             var request = formRequest ?? jsonRequest;
             if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password)) return BadRequest(/*...*/);
              _logger.LogInformation("Account claim attempt for login: {Login}", request.Username);

              // --- SpacetimeDB Call ---
             try {
                  var conn = GetConnection();
                   // Check user exists (logic might be inside reducer now)
                   // Generate identity if needed
                   string? newIdentityString = request.GenerateNewIdentity ? await GenerateIdentityAsync() : null;
                  // Call reducer
                   conn.Reducers.ClaimUserAccount(request.Username, request.Password, newIdentityString);
                   _logger.LogInformation("ClaimUserAccount reducer called for {Username}", request.Username);
                   // Give reducer time? May not be necessary if synchronous or next read reflects change.
                   // await Task.Delay(200);

                  // Check if claim was successful indirectly (e.g., user is now active with correct identity - COMPLEX)
                   // Simpler: Assume reducer throws on failure.

                  if (IsBrowserRequest()) return Redirect($"/api/auth/login?message={Uri.EscapeDataString("Account claimed. Please log in.")}");
                  return Ok(new ApiResponse<object> { Success = true, Message = "Account claim initiated. Please log in." });
             } catch (Exception ex) {
                  _logger.LogError(ex, "Error during account claim for user: {Username}", request.Username);
                  if (IsBrowserRequest()) return Redirect($"/api/auth/claim-account?error={Uri.EscapeDataString(ex.Message)}");
                  return StatusCode(500, new ApiResponse<object> { Success = false, Message = ex.Message });
             }
        }

        #endregion

        // --- File: AuthController.cs (Continued) ---


        private bool IsBrowserRequest()
        {
            var acceptHeader = Request.Headers.Accept.ToString().ToLowerInvariant();
            // Browsers typically send text/html, */* or application/xhtml+xml
            // API clients usually send application/json or application/xml
            bool isHtml = acceptHeader.Contains("text/html") || acceptHeader.Contains("application/xhtml+xml");
            bool isJson = acceptHeader.Contains("application/json");
            bool isWildcard = !isJson && acceptHeader.Contains("*/*"); // Treat wildcard as browser if JSON not present

            return isHtml || isWildcard;
        }

       
        private DbConnection GetConnection()
        {
            try
            {
                var conn = _spacetimeService.GetConnection();
                if (conn == null)
                {
                    _logger.LogError("SpacetimeDB connection is null in GetConnection.");
                    throw new InvalidOperationException("Database connection is not available.");
                }
                 if (!_spacetimeService.IsConnected()) {
                     _logger.LogWarning("SpacetimeDB connection is not active in GetConnection.");
                     // Depending on requirements, you might try to reconnect or just throw
                      throw new InvalidOperationException("Database connection is not currently active.");
                 }
                return conn;
            }
            catch (InvalidOperationException) { throw; } // Rethrow specific exceptions
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get SpacetimeDB connection.");
                throw new InvalidOperationException("Database connection failed.", ex);
            }
        }

        // --- Get User Identity (SpacetimeDB Identity) from HTTP Context (Cookie or Bearer) ---
        private Identity? GetUserIdentity()
        {
            ClaimsPrincipal principal = User; // From HttpContext

            // 1. Try getting from the authenticated principal's Subject claim (standard)
            var identityString = principal?.FindFirstValue(Claims.Subject);

            // 2. Fallback: Check custom "identity" claim from legacy JWT if Subject wasn't set correctly there
            if (string.IsNullOrEmpty(identityString)) {
                identityString = principal?.FindFirstValue("identity");
            }

            if (string.IsNullOrWhiteSpace(identityString) || !identityString.StartsWith("0x"))
            {
                // 3. Fallback for legacy tokens where Subject might be LegacyUserId
                 var legacyIdString = principal?.FindFirstValue(Claims.Subject) ?? principal?.FindFirstValue("legacy_user_id");
                  if (!string.IsNullOrWhiteSpace(legacyIdString) && uint.TryParse(legacyIdString, out uint legacyId)) {
                       _logger.LogDebug("User Identity (sub) was legacy ID '{LegacyId}'. Looking up Spacetime Identity.", legacyId);
                        try {
                           var conn = GetConnection();
                           var user = conn.Db.UserProfile.Iter().FirstOrDefault(u => u.LegacyUserId == legacyId);
                           if(user != null) {
                                _logger.LogDebug("Found Spacetime Identity {SpacetimeId} for legacy ID {LegacyId}", user.UserId, legacyId);
                               return user.UserId;
                           } else {
                                _logger.LogWarning("No user found for legacy ID {LegacyId} from token.", legacyId);
                               return null;
                           }
                        } catch (Exception ex) {
                             _logger.LogError(ex, "Error looking up Spacetime Identity by legacy ID {LegacyId}", legacyId);
                             return null;
                        }
                  }

                 _logger.LogWarning("Could not extract valid Spacetime Identity string from User claims (tried 'sub', 'identity'). Principal authenticated: {IsAuth}", principal?.Identity?.IsAuthenticated ?? false);
                 return null;
            }

            if (Identity.TryParse(identityString, out var identity)) {
                 _logger.LogTrace("Successfully parsed Spacetime Identity {Identity} from claims.", identity);
                return identity;
            } else {
                _logger.LogWarning("Failed to parse Spacetime Identity from claim value: {IdentityString}", identityString);
                return null;
            }
        }


        // --- Get UserProfile from Identity ---
        private async Task<UserProfile?> GetUserByIdentityAsync(Identity? userId)
        {
            if (userId == null) {
                _logger.LogDebug("GetUserByIdentityAsync called with null identity.");
                 return null;
            }
            try
            {
                await Task.Yield(); // Simulate async if DB call is sync
                var conn = GetConnection();
                var user = conn.Db.UserProfile.Iter().FirstOrDefault(u => u.UserId.Equals(userId.Value));
                if (user == null) {
                     _logger.LogWarning("User profile not found for Identity: {UserId}", userId.Value);
                } else {
                     _logger.LogTrace("User profile found for Identity: {UserId}", userId.Value);
                }
                return user;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user by Identity {UserId}", userId);
                return null;
            }
        }

        // --- Get UserProfile from Identity String ---
        private async Task<UserProfile?> GetUserByIdentityStringAsync(string? userIdString)
        {
            if (string.IsNullOrEmpty(userIdString) || !Identity.TryParse(userIdString, out var userId))
            {
                 _logger.LogDebug("GetUserByIdentityStringAsync called with invalid or null string: '{UserIdString}'", userIdString);
                 return null;
            }
            return await GetUserByIdentityAsync(userId);
        }


        // --- Map UserProfile to UserDto ---
        private UserDto MapUserToDto(UserProfile user)
        {
             if (user == null) return new UserDto(); // Or throw?
             _logger.LogDebug("Mapping UserProfile to UserDto for Login: {Login}", user.Login);
             var roleNames = GetUserRolesFromDb(user.UserId);
             var permissions = GetUserPermissionsFromDb(user.UserId);
             return new UserDto {
                 Id = user.LegacyUserId,
                 Username = user.Login,
                 Email = user.Email,
                 PhoneNumber = user.PhoneNumber,
                 Role = _authService.GetUserRole(user.UserId), // Keep legacy role ID
                 SpacetimeIdentity = user.UserId.ToString(),
                 Roles = roleNames,
                 Permissions = permissions
             };
        }

         // --- Get Role Names from SpacetimeDB --- (Sync version)
         private List<string> GetUserRolesFromDb(Identity userId)
         {
             if(userId.IsZero) return new List<string>(); // Avoid query for zero identity
              try {
                 var conn = GetConnection();
                 var roleIds = conn.Db.UserRole.Iter().Where(ur => ur.UserId.Equals(userId)).Select(ur => ur.RoleId).ToList();
                  if (!roleIds.Any()) return new List<string>();
                 return conn.Db.Role.Iter().Where(r => roleIds.Contains(r.RoleId) && r.IsActive).Select(r => r.Name).ToList();
              } catch (Exception ex) {
                 _logger.LogError(ex, "Failed to get roles for user {userId}", userId);
                 return new List<string>();
              }
         }
         // --- Get Role Entities (for profile page) ---
         private List<Role> GetUserRolesEntities(Identity userId) {
             if(userId.IsZero) return new List<Role>();
             try {
                  var conn = GetConnection();
                  var roleIds = conn.Db.UserRole.Iter().Where(ur => ur.UserId.Equals(userId)).Select(ur => ur.RoleId).ToList();
                  if (!roleIds.Any()) return new List<Role>();
                  return conn.Db.Role.Iter().Where(r => roleIds.Contains(r.RoleId) && r.IsActive).ToList();
             } catch (Exception ex) { _logger.LogError(ex, "Failed to get role entities for user {userId}", userId); return new List<Role>(); }
         }

         // --- Get Permission Names from SpacetimeDB --- (Sync version)
         private List<string> GetUserPermissionsFromDb(Identity userId)
         {
              if(userId.IsZero) return new List<string>();
              try {
                  var conn = GetConnection();
                  var roleIds = conn.Db.UserRole.Iter().Where(ur => ur.UserId.Equals(userId)).Select(ur => ur.RoleId).ToList();
                  if (!roleIds.Any()) return new List<string>();
                  var permissionIds = conn.Db.RolePermission.Iter().Where(rp => roleIds.Contains(rp.RoleId)).Select(rp => rp.PermissionId).Distinct().ToList();
                  if (!permissionIds.Any()) return new List<string>();
                  return conn.Db.Permission.Iter().Where(p => permissionIds.Contains(p.PermissionId) && p.IsActive).Select(p => p.Name).ToList();
             } catch (Exception ex) {
                  _logger.LogError(ex, "Failed to get permissions for user {userId}", userId);
                  return new List<string>();
             }
         }
          // --- Get Permission Entities (for profile page) ---
          private List<Permission> GetUserPermissionEntities(Identity userId) {
              if(userId.IsZero) return new List<Permission>();
               try {
                    var conn = GetConnection();
                    var roleIds = conn.Db.UserRole.Iter().Where(ur => ur.UserId.Equals(userId)).Select(ur => ur.RoleId).ToList();
                    if (!roleIds.Any()) return new List<Permission>();
                    var permissionIds = conn.Db.RolePermission.Iter().Where(rp => roleIds.Contains(rp.RoleId)).Select(rp => rp.PermissionId).Distinct().ToList();
                    if (!permissionIds.Any()) return new List<Permission>();
                    return conn.Db.Permission.Iter().Where(p => permissionIds.Contains(p.PermissionId) && p.IsActive).ToList();
               } catch (Exception ex) { _logger.LogError(ex, "Failed to get permission entities for user {userId}", userId); return new List<Permission>(); }
          }

         // --- Generate Legacy JWT ---
        private string GenerateJwtToken(UserProfile userProfile)
        {
             _logger.LogDebug("Generating legacy JWT for user {Username}", userProfile.Login);
             var tokenHandler = new JwtSecurityTokenHandler();
             var keyString = _configuration["JwtSettings:Secret"] ?? throw new InvalidOperationException("JWT secret is not configured");
             var keyBytes = Encoding.UTF8.GetBytes(keyString);
             if (keyBytes.Length < 32) { Array.Resize(ref keyBytes, 32); _logger.LogWarning("JWT Secret key was less than 32 bytes, padded."); }
             else if (keyBytes.Length > 64) { Array.Resize(ref keyBytes, 64); _logger.LogWarning("JWT Secret key was greater than 64 bytes, truncated."); }

             var key = new SymmetricSecurityKey(keyBytes);
             var expirationMinutes = double.Parse(_configuration["JwtSettings:ExpirationInMinutes"] ?? "120");

            // --- Claims for LEGACY token ---
             var claims = new List<Claim> {
                 new Claim(JwtRegisteredClaimNames.Sub, userProfile.LegacyUserId.ToString()), // Subject = Legacy ID
                 new Claim(JwtRegisteredClaimNames.UniqueName, userProfile.Login),
                 new Claim(ClaimTypes.Name, userProfile.Login),
                 new Claim("identity", userProfile.UserId.ToString()), // Spacetime Identity
                 new Claim("legacy_user_id", userProfile.LegacyUserId.ToString()) // Explicit Legacy ID
             };
             if (userProfile.Xuid.HasValue) claims.Add(new Claim("xuid", userProfile.Xuid.Value.ToString()));
             if (!string.IsNullOrEmpty(userProfile.Email)) claims.Add(new Claim(JwtRegisteredClaimNames.Email, userProfile.Email));

             // Add Legacy Role ID claim
             claims.Add(new Claim("role", _authService.GetUserRole(userProfile.UserId).ToString()));
             // Add Role Name claims
             var roleNames = GetUserRolesFromDb(userProfile.UserId);
             foreach (var roleName in roleNames) claims.Add(new Claim(ClaimTypes.Role, roleName));
             // Add Permission Name claims
             var permissions = GetUserPermissionsFromDb(userProfile.UserId);
             foreach (var perm in permissions) claims.Add(new Claim("permission", perm));

             var tokenDescriptor = new SecurityTokenDescriptor {
                 Subject = new ClaimsIdentity(claims), // Use the claims list built above
                 Expires = DateTime.UtcNow.AddMinutes(expirationMinutes),
                 SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature),
                  // Optional: Add Issuer/Audience if your legacy consumers validate them
                 // Issuer = _configuration["JwtSettings:Issuer"],
                 // Audience = _configuration["JwtSettings:Audience"]
             };

             var token = tokenHandler.CreateToken(tokenDescriptor);
             return tokenHandler.WriteToken(token);
        }

        // --- Generate Temporary JWT for SpacetimeDB HTTP API Calls (if needed) ---
        private async Task<string> GenerateJwtForSpacetimeHttpApi()
        {
            _logger.LogDebug("Generating temporary JWT for SpacetimeDB HTTP API call.");
            var tokenHandler = new JwtSecurityTokenHandler();
            // Use a SEPARATE, STRONG key specifically for API calls, configure securely.
             var apiKey = _configuration["SpacetimeDB:HttpApiKey"] ?? throw new InvalidOperationException("SpacetimeDB HTTP API key is not configured.");
             var keyBytes = Encoding.UTF8.GetBytes(apiKey);
             if (keyBytes.Length < 32) throw new InvalidOperationException("SpacetimeDB HTTP API key must be at least 32 bytes.");

             var key = new SymmetricSecurityKey(keyBytes);
             var claims = new List<Claim> {
                 new Claim(JwtRegisteredClaimNames.Iss, "BRU_AVTOPARK_ApiService"), // Identify issuer
                 new Claim(JwtRegisteredClaimNames.Aud, "SpacetimeDB_HTTP_API"),   // Identify audience
                 new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),  // Unique token ID
                 new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64) // Issued at
             };
             // Add specific claims required by SpacetimeDB's HTTP API if any (e.g., permissions)

             var tokenDescriptor = new SecurityTokenDescriptor {
                 Subject = new ClaimsIdentity(claims),
                 Expires = DateTime.UtcNow.AddMinutes(1), // VERY short expiry for API calls
                 SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature)
             };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        // --- Generate SpacetimeDB Identity via HTTP API ---
        private async Task<string?> GenerateIdentityAsync()
        {
            _logger.LogInformation("Attempting to generate new SpacetimeDB identity via HTTP API.");
            string identityEndpoint = _configuration["SpacetimeDB:HttpApiIdentityEndpoint"] ?? "http://localhost:3000/v1/identity"; // Get endpoint from config

             try {
                 using var httpClient = new HttpClient(); // Use IHttpClientFactory in real apps
                  var request = new HttpRequestMessage(HttpMethod.Post, identityEndpoint);
                  // If SpacetimeDB requires authentication for the identity endpoint:
                  // string apiToken = await GenerateJwtForSpacetimeHttpApi();
                  // request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiToken);

                  var response = await httpClient.SendAsync(request);

                 if (!response.IsSuccessStatusCode) {
                     var errorContent = await response.Content.ReadAsStringAsync();
                     _logger.LogError("Failed to generate SpacetimeDB identity via API. Status: {StatusCode}, Content: {Content}", response.StatusCode, errorContent);
                     return null; // Return null on failure
                 }

                  var jsonResponse = await response.Content.ReadAsStringAsync();
                 using var jsonDoc = JsonDocument.Parse(jsonResponse);
                  if (jsonDoc.RootElement.TryGetProperty("identity", out var identityElement) && identityElement.ValueKind == JsonValueKind.String) {
                      var identity = identityElement.GetString();
                      if(!string.IsNullOrWhiteSpace(identity) && identity.StartsWith("0x")) {
                           _logger.LogInformation("Successfully generated SpacetimeDB identity: {Identity}", identity);
                           return identity;
                      } else { _logger.LogError("Generated identity is invalid: {Identity}", identity); return null;}
                  } else { _logger.LogError("SpacetimeDB identity endpoint response missing 'identity' string. Resp: {Resp}", jsonResponse); return null; }
            } catch (HttpRequestException httpEx) {
                 _logger.LogError(httpEx, "HTTP error generating SpacetimeDB identity at {Endpoint}.", identityEndpoint);
                 return null;
             } catch (JsonException jsonEx) {
                  _logger.LogError(jsonEx, "JSON error parsing SpacetimeDB identity response from {Endpoint}.", identityEndpoint);
                  return null;
             } catch (Exception ex) {
                  _logger.LogError(ex, "Unexpected error generating SpacetimeDB identity at {Endpoint}.", identityEndpoint);
                  return null;
            }
        }

        // --- Generate Random Token (URL Safe) ---
        private string GenerateRandomToken(int byteLength = 32)
        {
             using var rng = RandomNumberGenerator.Create();
             var randomBytes = new byte[byteLength];
             rng.GetBytes(randomBytes);
             return Base64UrlEncoder.Encode(randomBytes); // Use Microsoft.IdentityModel.Tokens utility
        }

        // --- Create Principal for Session & OIDC ---
        // Creates a principal suitable for establishing a cookie session and
        // as a basis for generating OIDC tokens.
        private async Task<ClaimsPrincipal> CreatePrincipalForSessionAndOidcAsync(UserProfile user)
        {
             _logger.LogDebug("Creating principal for session/OIDC for user {Username}", user.Login);
             var claims = new List<Claim> {
                 new Claim(Claims.Subject, user.UserId.ToString()), // OIDC 'sub' MUST be Spacetime Identity
                 new Claim(Claims.Name, user.Login),                // Standard 'name' claim
                 new Claim(Claims.PreferredUsername, user.Login),   // Standard 'preferred_username'
                 new Claim(JwtRegisteredClaimNames.UniqueName, user.Login), // Often used for name in older systems
                 new Claim("legacy_user_id", user.LegacyUserId.ToString()), // Custom claim for legacy ID
                 new Claim("identity", user.UserId.ToString())         // Custom claim for Spacetime Identity string
             };
            if (user.Xuid.HasValue) claims.Add(new Claim("xuid", user.Xuid.Value.ToString()));
            if (!string.IsNullOrEmpty(user.Email)) {
                 claims.Add(new Claim(Claims.Email, user.Email));
                 claims.Add(new Claim(Claims.EmailVerified, user.EmailConfirmed ?? false, ClaimValueTypes.Boolean));
            }
            if (!string.IsNullOrEmpty(user.PhoneNumber)) {
                 claims.Add(new Claim(Claims.PhoneNumber, user.PhoneNumber));
                 // claims.Add(new Claim(Claims.PhoneNumberVerified, ...)); // Add if available
            }

            // Add Roles (using Role Names)
            var roleNames = GetUserRolesFromDb(user.UserId);
            foreach (var roleName in roleNames) claims.Add(new Claim(Claims.Role, roleName));
            // Add Legacy Role ID (numeric)
            claims.Add(new Claim("legacy_role_id", _authService.GetUserRole(user.UserId).ToString()));

            // Add Permissions (optional for cookie, useful for OIDC access tokens)
             // var permissions = GetUserPermissionsFromDb(user.UserId);
             // foreach (var perm in permissions) claims.Add(new Claim("permission", perm));

            // IMPORTANT: Use the CookieAuthenticationDefaults.AuthenticationScheme for the identity that
            // will be used to sign in the *cookie*. OpenIddict will use this principal later
            // and re-authenticate it against its own scheme for token generation.
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme, Claims.Name, Claims.Role);
            _logger.LogDebug("Created ClaimsIdentity with {ClaimCount} claims for user {Username}.", claims.Count, user.Login);
            return new ClaimsPrincipal(identity);
        }

        // --- Get User from Temp Token ---
        private async Task<UserProfile?> GetUserFromTempToken(string tempToken) {
            if(string.IsNullOrEmpty(tempToken)) return null;
             try {
                  var conn = GetConnection();
                  var tokenRecord = conn.Db.TwoFactorToken.Iter()
                                      .FirstOrDefault(t => t.Token == tempToken && !t.IsUsed && t.ExpiresAt > (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                  if (tokenRecord == null) {
                       _logger.LogWarning("GetUserFromTempToken: Temp token not found, expired, or used: {Token}", tempToken);
                       return null;
                  }
                   _logger.LogDebug("GetUserFromTempToken: Found valid temp token record for User ID: {UserId}", tokenRecord.UserId);
                  return await GetUserByIdentityAsync(tokenRecord.UserId);
             } catch (Exception ex) {
                  _logger.LogError(ex, "Error retrieving user from temp token {TempToken}", tempToken);
                  return null;
             }
        }

        #endregion


        #region Helper Methods (OIDC Related)

        // --- Get Scope Details (Name + Description) for Consent Screen ---
        private async Task<Dictionary<string, string>> GetScopeDetailsAsync(ImmutableArray<string> scopeNames)
        {
            var details = new Dictionary<string, string>();
             if (scopeNames.IsDefaultOrEmpty) return details;

             _logger.LogDebug("Fetching details for scopes: {Scopes}", string.Join(", ", scopeNames));
            foreach (var name in scopeNames) {
                try {
                     // Use Scope Manager to find the scope definition
                     var scopeObject = await _scopeManager.FindByNameAsync(name);
                     if (scopeObject != null) {
                          // Fetch description (or fallback to display name/name)
                           var description = await _scopeManager.GetDescriptionAsync(scopeObject) ??
                                             await _scopeManager.GetDisplayNameAsync(scopeObject) ??
                                             name; // Fallback to scope name itself
                          details.Add(name, description);
                          _logger.LogTrace("Scope detail: {ScopeName} -> {Description}", name, description);
                     } else {
                           _logger.LogWarning("Scope definition not found for '{ScopeName}'. Using name as description.", name);
                          details.Add(name, name); // Add scope name even if definition not found
                     }
                } catch (Exception ex) {
                     _logger.LogError(ex, "Error fetching details for scope '{ScopeName}'.", name);
                     details.Add(name, name); // Add scope name as fallback on error
                }
            }
            return details;
        }

        // --- Get Resources associated with Scopes (for Access Token Audience) ---
        private async Task<IEnumerable<string>> GetResourcesAsync(ImmutableArray<string> scopes)
        {
             var resources = new HashSet<string>(StringComparer.Ordinal);
             if (scopes.IsDefaultOrEmpty) {
                  _logger.LogDebug("GetResourcesAsync: No scopes provided, returning empty resource set.");
                  return resources;
              }

             _logger.LogDebug("Listing resources for scopes: {Scopes}", string.Join(", ", scopes));
             await foreach (var resource in _scopeManager.ListResourcesAsync(scopes, CancellationToken.None))
             {
                  if (!string.IsNullOrEmpty(resource)) {
                      resources.Add(resource);
                      _logger.LogTrace("Associated resource found: {Resource}", resource);
                  }
             }

              // Add default API resource if needed (e.g., your main API identifier)
              // string defaultApiResource = _configuration["OidcSettings:DefaultApiResourceName"] ?? "bru_avtopark_api";
              // if (!string.IsNullOrWhiteSpace(defaultApiResource)) resources.Add(defaultApiResource);

              _logger.LogDebug("Final resource set for scopes {Scopes}: {Resources}", string.Join(", ", scopes), string.Join(", ", resources));
             return resources;
        }

        // --- Define Claim Destinations for OIDC Tokens ---
        public static IEnumerable<string> GetDestinations(Claim claim)
        {
             // Default behavior: claims are not included in any token unless specified.
             switch (claim.Type)
             {
                 // --- Standard OIDC Claims ---
                 case Claims.Name:
                 case Claims.PreferredUsername:
                     yield return Destinations.AccessToken; // Typically needed by Resource APIs
                     if (claim.Subject?.HasScope(Scopes.Profile) == true)
                         yield return Destinations.IdentityToken; // Include in ID token if 'profile' scope granted
                     break;

                 case Claims.Email:
                 case Claims.EmailVerified:
                     yield return Destinations.AccessToken;
                     if (claim.Subject?.HasScope(Scopes.Email) == true)
                         yield return Destinations.IdentityToken;
                     break;

                 case Claims.PhoneNumber:
                 // case Claims.PhoneNumberVerified: // Uncomment if claim exists
                     yield return Destinations.AccessToken;
                     if (claim.Subject?.HasScope(Scopes.Phone) == true)
                         yield return Destinations.IdentityToken;
                     break;

                 case Claims.Role: // Both standard ClaimTypes.Role and custom "role" map here
                     yield return Destinations.AccessToken;
                     if (claim.Subject?.HasScope(Scopes.Roles) == true)
                         yield return Destinations.IdentityToken;
                     break;

                  // --- Custom Application Claims ---
                  // Include these in the Access Token if your resource APIs need them.
                  // Rarely needed in the ID Token unless specifically required by a client OIDC library.
                  case "permission":
                  case "legacy_user_id":
                  case "legacy_role_id":
                  case "identity": // SpacetimeDB Identity string
                  case "xuid":
                       yield return Destinations.AccessToken;
                       // Only add to ID Token if absolutely necessary and client relies on it (generally avoid)
                       // if (claim.Subject?.HasScope("custom_identity_scope") == true) // Example custom scope
                       //     yield return Destinations.IdentityToken;
                       break;

                 // --- Claims to NEVER include ---
                 case "AspNet.Identity.SecurityStamp": // Example: Security stamp should remain server-side
                 // case "PasswordHash": // Never include sensitive data
                      yield break;

                 default:
                     // By default, DO NOT include unknown/unspecified claims in tokens.
                     // If a claim is needed, explicitly define its destination(s) above.
                     yield break;
             }
        }

         // --- Create Principal specifically for OIDC Token Generation ---
         // Takes the *authenticated* cookie principal as input
         private async Task<ClaimsPrincipal> CreatePrincipalForOidcTokensAsync(ClaimsPrincipal cookiePrincipal, ImmutableArray<string> requestedScopes)
         {
             var userIdString = cookiePrincipal.FindFirstValue(Claims.Subject); // Expecting Spacetime Identity
             if (userIdString == null || !Identity.TryParse(userIdString, out var userId)) {
                  throw new InvalidOperationException("Cannot create OIDC principal: Valid 'sub' claim (Spacetime Identity) missing from cookie principal.");
             }
             _logger.LogDebug("CreatePrincipalForOidcTokensAsync: Creating OIDC principal for user {UserId} with scopes: {Scopes}", userId, string.Join(", ", requestedScopes));

              // Fetch fresh user data to ensure claims are up-to-date
             var user = await GetUserByIdentityAsync(userId);
              if (user == null) {
                   throw new InvalidOperationException($"User profile not found for identity {userId} during OIDC principal creation.");
              }

              // --- Build claims for the OIDC tokens ---
              var claims = new List<Claim>();
              // REQUIRED: Subject claim MUST be the Spacetime Identity string
              claims.Add(new Claim(Claims.Subject, user.UserId.ToString()));

              // --- Map standard OIDC claims based on *requested* scopes ---
              if (requestedScopes.Contains(Scopes.Profile)) {
                   claims.Add(new Claim(Claims.Name, user.Login));
                   claims.Add(new Claim(Claims.PreferredUsername, user.Login));
                   // Add other profile claims: family_name, given_name, etc. if available
                   // claims.Add(new Claim(Claims.UpdatedAt, ...)); // Needs Unix Seconds
              }
              if (requestedScopes.Contains(Scopes.Email)) {
                  if (!string.IsNullOrEmpty(user.Email)) claims.Add(new Claim(Claims.Email, user.Email));
                  claims.Add(new Claim(Claims.EmailVerified, user.EmailConfirmed ?? false, ClaimValueTypes.Boolean));
              }
              if (requestedScopes.Contains(Scopes.Phone)) {
                   if (!string.IsNullOrEmpty(user.PhoneNumber)) claims.Add(new Claim(Claims.PhoneNumber, user.PhoneNumber));
                   // claims.Add(new Claim(Claims.PhoneNumberVerified, ...));
              }
              if (requestedScopes.Contains(Scopes.Roles)) {
                   var roleNames = GetUserRolesFromDb(user.UserId);
                   foreach (var roleName in roleNames) claims.Add(new Claim(Claims.Role, roleName));
                   // Also include legacy role ID? Check GetDestinations logic.
                    // claims.Add(new Claim("legacy_role_id", _authService.GetUserRole(user.UserId).ToString()));
              }

               // --- Map custom claims based on *requested* custom scopes ---
              bool includeApiClaims = requestedScopes.Contains("api") || requestedScopes.Contains("permissions"); // Example custom scopes
               if (includeApiClaims) {
                   var permissions = GetUserPermissionsFromDb(user.UserId);
                   foreach (var perm in permissions) claims.Add(new Claim("permission", perm));
                   claims.Add(new Claim("legacy_user_id", user.LegacyUserId.ToString()));
                   if(user.Xuid.HasValue) claims.Add(new Claim("xuid", user.Xuid.Value.ToString()));
               }
               if (requestedScopes.Contains("identity")) { // Example scope for Spacetime ID
                    claims.Add(new Claim("identity", user.UserId.ToString()));
               }


              // Create the identity using the OIDC Server Scheme as the authentication type
               var identity = new ClaimsIdentity(
                   claims,
                   authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                   nameType: Claims.Name, // Specifies which claim holds the display name
                   roleType: Claims.Role   // Specifies which claim holds roles
               );

               // Copy specific claims from the *original cookie principal* if they are needed
               // and should not be refreshed from the database (e.g., authentication method, time).
               // Example: Copy 'amr' (Authentication Methods References) if present
               // var authMethod = cookiePrincipal.FindFirstValue(Claims.AuthenticationMethod);
               // if (!string.IsNullOrEmpty(authMethod)) { identity.AddClaim(new Claim(Claims.AuthenticationMethod, authMethod)); }
                // Example: Copy 'auth_time'
                // var authTime = cookiePrincipal.FindFirstValue(Claims.AuthenticationTime);
                // if (!string.IsNullOrEmpty(authTime)) { identity.AddClaim(new Claim(Claims.AuthenticationTime, authTime, ClaimValueTypes.Integer64)); }


               _logger.LogDebug("Created OIDC ClaimsIdentity with {ClaimCount} claims for user {UserId}.", claims.Count, userId);
              return new ClaimsPrincipal(identity);
         }

        // Overload for Password Grant (ROPC) where no cookie principal exists
        private Task<ClaimsPrincipal> CreatePrincipalForOidcTokensAsync(UserProfile user, ImmutableArray<string> requestedScopes)
        {
             _logger.LogDebug("CreatePrincipalForOidcTokensAsync (from UserProfile): Creating OIDC principal for user {Username}", user.Login);
             // Create a minimal temporary principal containing only the Subject to pass to the main helper
             var tempIdentity = new ClaimsIdentity(new[] { new Claim(Claims.Subject, user.UserId.ToString()) });
             return CreatePrincipalForOidcTokensAsync(new ClaimsPrincipal(tempIdentity), requestedScopes);
        }

        // --- Local Permission Check Helper ---
        private bool HasPermissionLocal(Identity? userId, string permissionName, bool requireAdminRoleFallback = false)
        {
            if (userId == null || userId.Value.IsZero) return false; // Handle null or zero identity
             _logger.LogTrace("Checking permission '{PermissionName}' for user {UserId}. Admin fallback allowed: {AdminFallback}", permissionName, userId, requireAdminRoleFallback);
            try {
                var conn = GetConnection();
                var roleIds = conn.Db.UserRole.Iter().Where(ur => ur.UserId.Equals(userId.Value)).Select(ur => ur.RoleId).ToList();

                 if (!roleIds.Any()) {
                      _logger.LogDebug("Permission check failed for {UserId}: User has no roles assigned.", userId);
                       return false;
                 }
                 _logger.LogTrace("User {UserId} has roles: [{Roles}]", userId, string.Join(", ", roleIds));

                var permissionIds = conn.Db.RolePermission.Iter().Where(rp => roleIds.Contains(rp.RoleId)).Select(rp => rp.PermissionId).Distinct().ToList();
                 if (!permissionIds.Any()) {
                       _logger.LogDebug("Permission check failed for {UserId}: User's roles have no permissions assigned.", userId);
                      // Check admin fallback even if no specific perms assigned to roles
                       if (requireAdminRoleFallback) {
                            var adminRole = conn.Db.Role.Iter().FirstOrDefault(r => r.Name == "Administrator");
                             bool isAdmin = adminRole != null && roleIds.Contains(adminRole.RoleId);
                             _logger.LogDebug("Admin fallback check for {UserId}: IsAdmin={IsAdmin}", userId, isAdmin);
                            return isAdmin;
                       }
                       return false;
                 }
                 _logger.LogTrace("User {UserId} has permissions (via roles): [{Permissions}]", userId, string.Join(", ", permissionIds));


                 bool hasPerm = conn.Db.Permission.Iter().Any(p => permissionIds.Contains(p.PermissionId) && p.Name == permissionName && p.IsActive);
                 _logger.LogDebug("Direct permission check result for {UserId}, Permission '{PermissionName}': {HasPerm}", userId, permissionName, hasPerm);

                // Fallback to checking if user IS an admin if direct permission check fails AND fallback is allowed
                 if (!hasPerm && requireAdminRoleFallback) {
                       var adminRole = conn.Db.Role.Iter().FirstOrDefault(r => r.Name == "Administrator");
                       bool isAdmin = adminRole != null && roleIds.Contains(adminRole.RoleId);
                       _logger.LogDebug("Admin fallback check for {UserId} after direct check failed: IsAdmin={IsAdmin}", userId, isAdmin);
                       return isAdmin;
                 }
                 return hasPerm;
            } catch (Exception ex) {
                 _logger.LogError(ex, "Error checking permission {PermissionName} for user {UserId}", permissionName, userId);
                 return false;
            }
        }

         // --- Simple Forbid Helper for OIDC Errors ---
         private ForbidResult Forbid(string error, string description, OpenIddictRequest request) {
              _logger.LogWarning("Forbidding OIDC request. Error: {Error}, Description: {Description}, Request: {@OidcRequest}", error, description, request);
              var properties = new AuthenticationProperties(new Dictionary<string, string?> {
                   [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                   [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
                   // Optional: Add ErrorUri if you have a page explaining errors
                   // [OpenIddictServerAspNetCoreConstants.Properties.ErrorUri] = "/docs/oidc/errors#" + error
               });
              // Pass the scheme to ensure OpenIddict handles the error response correctly (e.g., redirect with error params)
              return Forbid(properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
         }

         // --- Overload for Challenge/Forbid without explicit request ---
         private ChallengeResult Challenge(string error, string description) {
               return Challenge(new AuthenticationProperties(new Dictionary<string, string?> {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
               }), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
          }
         private ForbidResult Forbid(string error, string description) {
              return Forbid(new AuthenticationProperties(new Dictionary<string, string?> {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
               }), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
          }

        // --- Split Textarea Input ---
        private string[] SplitTextareaInput(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return Array.Empty<string>();
            return input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
        }

         // --- Validate URI Helper ---
         private bool AreUrisValid(IEnumerable<string> uris, bool allowHttpLocalhost = false) {
             foreach(var uriString in uris) {
                  if (!Uri.TryCreate(uriString, UriKind.Absolute, out Uri? uri)) {
                       _logger.LogWarning("Invalid URI format: {UriString}", uriString);
                       return false; // Not a valid URI
                  }
                  // Require HTTPS unless specifically allowed for localhost during development
                  if (uri.Scheme != "https" && !(allowHttpLocalhost && uri.IsLoopback)) {
                        _logger.LogWarning("Invalid URI scheme (must be HTTPS or HTTP for localhost): {UriString}", uriString);
                        return false;
                  }
                  // Add other validation if needed (e.g., no fragments)
                   if (!string.IsNullOrEmpty(uri.Fragment)) {
                        _logger.LogWarning("URI cannot contain fragments: {UriString}", uriString);
                         return false;
                   }
             }
             return true; // All URIs are valid
         }

          // --- Safe Redirect URL Helper ---
          private string GetSafeRedirectUrl(string? returnUrl) {
               if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) {
                   _logger.LogDebug("Using provided local ReturnUrl: {ReturnUrl}", returnUrl);
                    return returnUrl;
               }
                _logger.LogDebug("ReturnUrl '{ReturnUrl}' is invalid or missing, defaulting to profile.", returnUrl);
               return "/api/auth/profile"; // Default safe redirect
          }

        #endregion


        #region Helper Methods (OIDC Client Admin)

         // --- Fetch all clients for the List Page ---
         private async Task<List<ClientDto>> GetAllOidcApplicationsAsync() {
              _logger.LogDebug("Fetching all OIDC applications.");
              var apps = new List<ClientDto>();
               // Use await foreach with the manager's ListAsync method
               await foreach (var appObject in _applicationManager.ListAsync()) {
                    // Important: Handle potential null values from the manager methods
                    string? clientId = await _applicationManager.GetClientIdAsync(appObject);
                    string? displayName = await _applicationManager.GetDisplayNameAsync(appObject);

                    if (clientId != null) { // Only add if we have a ClientId
                        apps.Add(new ClientDto {
                            ClientId = clientId,
                            DisplayName = displayName ?? clientId // Fallback to ClientId if DisplayName is null
                        });
                    } else {
                        _logger.LogWarning("Found an OIDC application object without a ClientId during listing.");
                    }
               }
               _logger.LogDebug("Fetched {Count} OIDC applications.", apps.Count);
               return apps;
         }

         // --- Fetch full details for a single client ---
         private async Task<GetClientResponse?> GetClientDetailsDtoAsync(string clientId) {
              _logger.LogDebug("Fetching details for OIDC client {ClientId}.", clientId);
              var application = await _applicationManager.FindByClientIdAsync(clientId);
              if (application == null) {
                  _logger.LogWarning("Client details not found for ClientId: {ClientId}", clientId);
                   return null;
              }

              var permissions = await _applicationManager.GetPermissionsAsync(application);
              var redirectUris = await _applicationManager.GetRedirectUrisAsync(application);
              var postLogoutUris = await _applicationManager.GetPostLogoutRedirectUrisAsync(application);

              return new GetClientResponse {
                   ClientId = clientId, // Already have this
                   DisplayName = await _applicationManager.GetDisplayNameAsync(application),
                   RedirectUris = redirectUris.ToArray(),
                   PostLogoutRedirectUris = postLogoutUris.ToArray(),
                   // Extract scope names from permissions that start with "scp:"
                   AllowedScopes = permissions.Where(p => p.StartsWith(Permissions.Prefixes.Scope)).Select(p => p.Substring(Permissions.Prefixes.Scope.Length)).ToArray(),
                   RequireConsent = await _applicationManager.GetConsentTypeAsync(application) == ConsentTypes.Explicit
               };
         }

          // --- Map Client Form Data (Create) to OpenIddict Descriptor ---
          private OpenIddictApplicationDescriptor MapFormToDescriptor(RegisterClientFormRequest request) {
               _logger.LogDebug("Mapping RegisterClientFormRequest to OpenIddictApplicationDescriptor for ClientId: {ClientId}", request.ClientId);
               var descriptor = new OpenIddictApplicationDescriptor {
                   ClientId = request.ClientId,
                   ClientSecret = request.ClientSecret, // Secret is handled separately by manager usually
                   DisplayName = request.DisplayName,
                   ConsentType = request.RequireConsent ? ConsentTypes.Explicit : ConsentTypes.Implicit, // Default to Implicit if not Explicit
                   // Determine Client Type based on secret presence? Or add field to form? Defaulting to Public.
                   Type = string.IsNullOrWhiteSpace(request.ClientSecret) ? ClientTypes.Public : ClientTypes.Confidential
               };

               // Add Required Permissions for Common Flows
               descriptor.Permissions.UnionWith(new HashSet<string> {
                   Permissions.Endpoints.Authorization, Permissions.Endpoints.Token,
                   Permissions.Endpoints.Logout,
                   Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken,
                   Permissions.GrantTypes.Password, // Keep ROPC for legacy Avalonia
                   Permissions.ResponseTypes.Code, // For Auth Code flow
                   Permissions.Scopes.OpenId, Permissions.Scopes.Profile, // Common scopes
                   Permissions.Scopes.Email, Permissions.Scopes.Roles, Permissions.Scopes.Phone // Common scopes
                   // Add more endpoints/grants/scopes as needed (e.g., Introspection, Revocation, ClientCredentials)
               });

                // Add Custom Scopes from Form
               foreach (var scope in SplitTextareaInput(request.AllowedScopes)) {
                    if (!string.IsNullOrWhiteSpace(scope) && !scope.StartsWith(Permissions.Prefixes.Scope)) {
                        descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope); // Add "scp:" prefix
                    } else if (!string.IsNullOrWhiteSpace(scope)) {
                         descriptor.Permissions.Add(scope); // Assume already correctly prefixed
                    }
               }

                // Add Redirect URIs
                foreach (var uriString in SplitTextareaInput(request.RedirectUris)) {
                     if (Uri.TryCreate(uriString, UriKind.Absolute, out var uri)) descriptor.RedirectUris.Add(uri);
                     else _logger.LogWarning("Invalid Redirect URI skipped during mapping: {UriString}", uriString);
                }

                 // Add Post-Logout Redirect URIs
                 foreach (var uriString in SplitTextareaInput(request.PostLogoutRedirectUris)) {
                      if (Uri.TryCreate(uriString, UriKind.Absolute, out var uri)) descriptor.PostLogoutRedirectUris.Add(uri);
                      else _logger.LogWarning("Invalid Post-Logout Redirect URI skipped during mapping: {UriString}", uriString);
                 }

                // Add PKCE requirement for Public clients (RECOMMENDED)
                 if (descriptor.Type == ClientTypes.Public) {
                      descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
                 }

                 _logger.LogDebug("Mapped Descriptor: {@Descriptor}", descriptor); // Log the created descriptor
                return descriptor;
          }

          // --- Overload to map Client Form Data (Update) ---
          private OpenIddictApplicationDescriptor MapFormToDescriptor(UpdateClientFormRequest request, string clientId) {
                _logger.LogDebug("Mapping UpdateClientFormRequest to OpenIddictApplicationDescriptor for ClientId: {ClientId}", clientId);
                 // Start with existing descriptor data, then overwrite from form
                 // Note: This requires fetching the existing descriptor first in the calling method,
                 // or constructing a new one and only setting fields present in the Update request.
                 // Creating a new one is simpler here.
                 var descriptor = new OpenIddictApplicationDescriptor {
                     // ClientId is fixed for update
                     DisplayName = request.DisplayName,
                     ConsentType = request.RequireConsent ? ConsentTypes.Explicit : ConsentTypes.Implicit,
                     // Client Type and Secret are updated via _applicationManager.UpdateAsync(app, secret)
                 };

                 // Clear and re-add URIs and Permissions from the form data
                 descriptor.Permissions.Clear();
                 descriptor.RedirectUris.Clear();
                 descriptor.PostLogoutRedirectUris.Clear();

                 // Re-add essential permissions + scopes from form
                  descriptor.Permissions.UnionWith(new HashSet<string> {
                       Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.Endpoints.Logout,
                       Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken, Permissions.GrantTypes.Password,
                       Permissions.ResponseTypes.Code, Permissions.Scopes.OpenId, Permissions.Scopes.Profile,
                       Permissions.Scopes.Email, Permissions.Scopes.Roles, Permissions.Scopes.Phone
                  });
                  foreach (var scope in SplitTextareaInput(request.AllowedScopes)) {
                       if (!string.IsNullOrWhiteSpace(scope) && !scope.StartsWith(Permissions.Prefixes.Scope)) descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
                       else if (!string.IsNullOrWhiteSpace(scope)) descriptor.Permissions.Add(scope);
                  }
                  foreach (var uriString in SplitTextareaInput(request.RedirectUris)) {
                       if (Uri.TryCreate(uriString, UriKind.Absolute, out var uri)) descriptor.RedirectUris.Add(uri);
                  }
                  foreach (var uriString in SplitTextareaInput(request.PostLogoutRedirectUris)) {
                       if (Uri.TryCreate(uriString, UriKind.Absolute, out var uri)) descriptor.PostLogoutRedirectUris.Add(uri);
                  }

                  // Re-apply PKCE if still public (assuming type isn't changed via form)
                  // var existingApp = await _applicationManager.FindByClientIdAsync(clientId); // Need async context or pass existing app type
                  // if (await _applicationManager.GetTypeAsync(existingApp) == ClientTypes.Public) {
                       descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
                  // }

                 _logger.LogDebug("Mapped Update Descriptor: {@Descriptor}", descriptor);
                 return descriptor;
          }

          // --- Helper to map form request to GetClientResponse (for re-rendering form on error) ---
          private GetClientResponse MapFormToClientResponse(RegisterClientFormRequest request) {
               return new GetClientResponse {
                   ClientId = request.ClientId, DisplayName = request.DisplayName,
                   RedirectUris = SplitTextareaInput(request.RedirectUris),
                   PostLogoutRedirectUris = SplitTextareaInput(request.PostLogoutRedirectUris),
                   AllowedScopes = SplitTextareaInput(request.AllowedScopes),
                   RequireConsent = request.RequireConsent
               };
          }
         private GetClientResponse MapFormToClientResponse(UpdateClientFormRequest request, string clientId) {
               return new GetClientResponse {
                   ClientId = clientId, DisplayName = request.DisplayName,
                   RedirectUris = SplitTextareaInput(request.RedirectUris),
                   PostLogoutRedirectUris = SplitTextareaInput(request.PostLogoutRedirectUris),
                   AllowedScopes = SplitTextareaInput(request.AllowedScopes),
                   RequireConsent = request.RequireConsent
               };
          }


        #endregion


        #region Helper Methods (Completion)

        // --- Get OIDC Application Details DTO ---
        private async Task<GetClientResponse?> GetClientDetailsDtoAsync(string clientId) {
            _logger.LogDebug("Fetching details for OIDC client {ClientId}.", clientId);
            var applicationObject = await _applicationManager.FindByClientIdAsync(clientId);
            if (applicationObject == null) {
                _logger.LogWarning("Client details not found for ClientId: {ClientId}", clientId);
                return null;
            }

             // Get all properties using the manager
            var permissions = await _applicationManager.GetPermissionsAsync(applicationObject) ?? ImmutableArray<string>.Empty;
            var redirectUris = await _applicationManager.GetRedirectUrisAsync(applicationObject) ?? ImmutableArray<string>.Empty;
            var postLogoutUris = await _applicationManager.GetPostLogoutRedirectUrisAsync(applicationObject) ?? ImmutableArray<string>.Empty;
            var consentType = await _applicationManager.GetConsentTypeAsync(applicationObject);

             return new GetClientResponse {
                ClientId = clientId, // We already have this
                DisplayName = await _applicationManager.GetDisplayNameAsync(applicationObject),
                RedirectUris = redirectUris.ToArray(),
                PostLogoutRedirectUris = postLogoutUris.ToArray(),
                // Extract just the scope names from the permissions
                AllowedScopes = permissions.Where(p => p.StartsWith(Permissions.Prefixes.Scope)).Select(p => p.Substring(Permissions.Prefixes.Scope.Length)).ToArray(),
                RequireConsent = consentType == ConsentTypes.Explicit // Determine based on consent type string
            };
        }

        // --- Map Client Form Data (Create) to OpenIddict Descriptor ---
        private OpenIddictApplicationDescriptor MapFormToDescriptor(RegisterClientFormRequest request) {
             _logger.LogDebug("Mapping RegisterClientFormRequest to OpenIddictApplicationDescriptor for ClientId: {ClientId}", request.ClientId);
             var descriptor = new OpenIddictApplicationDescriptor {
                 ClientId = request.ClientId,
                 // ClientSecret = request.ClientSecret, // Handled separately by manager
                 DisplayName = request.DisplayName,
                 ConsentType = request.RequireConsent ? ConsentTypes.Explicit : ConsentTypes.Implicit,
                 Type = string.IsNullOrWhiteSpace(request.ClientSecret) ? ClientTypes.Public : ClientTypes.Confidential
             };

              // Add required permissions
             descriptor.Permissions.UnionWith(GetDefaultClientPermissions()); // Use helper for default perms

              // Add scopes from form
              foreach (var scope in SplitTextareaInput(request.AllowedScopes)) {
                   if (!string.IsNullOrWhiteSpace(scope) && !scope.StartsWith(Permissions.Prefixes.Scope)) { descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope); }
                   else if (!string.IsNullOrWhiteSpace(scope)) { descriptor.Permissions.Add(scope); } // Assume prefixed correctly
              }
             // Add OpenId scope if missing
             if (!descriptor.Permissions.Contains(Permissions.Scopes.OpenId) && !descriptor.Permissions.Contains(Permissions.Prefixes.Scope + Scopes.OpenId)) {
                  descriptor.Permissions.Add(Permissions.Scopes.OpenId);
             }

              // Add URIs
              foreach (var uriString in SplitTextareaInput(request.RedirectUris)) { if (Uri.TryCreate(uriString, UriKind.Absolute, out var uri)) descriptor.RedirectUris.Add(uri); }
              foreach (var uriString in SplitTextareaInput(request.PostLogoutRedirectUris)) { if (Uri.TryCreate(uriString, UriKind.Absolute, out var uri)) descriptor.PostLogoutRedirectUris.Add(uri); }

             // Add PKCE for public clients
              if (descriptor.Type == ClientTypes.Public) { descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange); }

             return descriptor;
        }

        // --- Map Client Form Data (Update) to OpenIddict Descriptor ---
        private OpenIddictApplicationDescriptor MapFormToDescriptor(UpdateClientFormRequest request, string clientId) {
             _logger.LogDebug("Mapping UpdateClientFormRequest to OpenIddictApplicationDescriptor for ClientId: {ClientId}", clientId);
             // For update, we typically only update specific fields allowed by the form.
             // ClientId and potentially Type are often immutable or handled separately.
             var descriptor = new OpenIddictApplicationDescriptor {
                  // ClientId is not updated here
                  DisplayName = request.DisplayName,
                  ConsentType = request.RequireConsent ? ConsentTypes.Explicit : ConsentTypes.Implicit
                  // Do NOT map ClientSecret here; manager handles it separately
             };

             // Clear and re-add Permissions and URIs based *only* on form data
             descriptor.Permissions.Clear();
             descriptor.RedirectUris.Clear();
             descriptor.PostLogoutRedirectUris.Clear();
              descriptor.Requirements.Clear(); // Also clear requirements

              // Re-add essential permissions + scopes from form
             descriptor.Permissions.UnionWith(GetDefaultClientPermissions());
             foreach (var scope in SplitTextareaInput(request.AllowedScopes)) {
                   if (!string.IsNullOrWhiteSpace(scope) && !scope.StartsWith(Permissions.Prefixes.Scope)) descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
                   else if (!string.IsNullOrWhiteSpace(scope)) descriptor.Permissions.Add(scope);
             }
              if (!descriptor.Permissions.Contains(Permissions.Scopes.OpenId) && !descriptor.Permissions.Contains(Permissions.Prefixes.Scope + Scopes.OpenId)) {
                   descriptor.Permissions.Add(Permissions.Scopes.OpenId);
              }

              foreach (var uriString in SplitTextareaInput(request.RedirectUris)) { if (Uri.TryCreate(uriString, UriKind.Absolute, out var uri)) descriptor.RedirectUris.Add(uri); }
              foreach (var uriString in SplitTextareaInput(request.PostLogoutRedirectUris)) { if (Uri.TryCreate(uriString, UriKind.Absolute, out var uri)) descriptor.PostLogoutRedirectUris.Add(uri); }

             // Re-apply PKCE if it's a public client (Determine based on existing type or form input if type is editable)
             // var existingApp = await _applicationManager.FindByClientIdAsync(clientId); // Need async context for this
             // if(await _applicationManager.GetTypeAsync(existingApp) == ClientTypes.Public)
                 descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange); // Assume still needed if public


             return descriptor;
        }

        // --- Helper for default permissions when mapping form data ---
        private IEnumerable<string> GetDefaultClientPermissions() {
            return new HashSet<string> {
                 Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.Endpoints.Logout,
                 // Permissions.Endpoints.Introspection, Permissions.Endpoints.Revocation, // Add if needed
                 Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken,
                 Permissions.GrantTypes.Password, // Keep for legacy
                 // Permissions.GrantTypes.ClientCredentials, // Add if needed
                 Permissions.ResponseTypes.Code,
                 Permissions.Scopes.OpenId // Always add standard scopes implicitly? Or rely on form? Add here for safety.
                 // Permissions.Scopes.Profile, Permissions.Scopes.Email, Permissions.Scopes.Roles, Permissions.Scopes.Phone // Add standard scopes?
             };
        }

         // --- Helper to map form request to GetClientResponse (for re-rendering form on error) ---
         private GetClientResponse MapFormToClientResponse(RegisterClientFormRequest request) {
              return new GetClientResponse {
                  ClientId = request.ClientId, DisplayName = request.DisplayName,
                  RedirectUris = SplitTextareaInput(request.RedirectUris),
                  PostLogoutRedirectUris = SplitTextareaInput(request.PostLogoutRedirectUris),
                  AllowedScopes = SplitTextareaInput(request.AllowedScopes),
                  RequireConsent = request.RequireConsent
              };
         }
        private GetClientResponse MapFormToClientResponse(UpdateClientFormRequest request, string clientId) {
              return new GetClientResponse {
                  ClientId = clientId, DisplayName = request.DisplayName,
                  RedirectUris = SplitTextareaInput(request.RedirectUris),
                  PostLogoutRedirectUris = SplitTextareaInput(request.PostLogoutRedirectUris),
                  AllowedScopes = SplitTextareaInput(request.AllowedScopes),
                  RequireConsent = request.RequireConsent
              };
         }

         // --- Validate Post-Logout Redirect URI ---
         // Should validate against the URIs registered for the specific client involved in the logout.
         // Requires id_token_hint validation or other mechanism to know the client_id during logout.
         private async Task<bool> ValidatePostLogoutRedirectUriAsync(string? uri, string? clientId = null) {
              if (string.IsNullOrEmpty(uri) || !Uri.TryCreate(uri, UriKind.Absolute, out _)) return false; // Must be absolute

              // If clientId is known, validate against registered URIs for that client
              if (!string.IsNullOrEmpty(clientId)) {
                   var application = await _applicationManager.FindByClientIdAsync(clientId);
                   if (application != null) {
                        var allowedUris = await _applicationManager.GetPostLogoutRedirectUrisAsync(application);
                        if (allowedUris.Contains(uri, StringComparer.OrdinalIgnoreCase)) {
                            _logger.LogDebug("Post-logout URI '{Uri}' is valid for client {ClientId}.", uri, clientId);
                             return true;
                        } else {
                            _logger.LogWarning("Post-logout URI '{Uri}' is NOT registered for client {ClientId}.", uri, clientId);
                             return false; // URI not registered for this client
                        }
                   } else {
                        _logger.LogWarning("Client {ClientId} not found during post-logout URI validation.", clientId);
                        // Fallback to general validation if client lookup fails? Or disallow? Disallowing is safer.
                        return false;
                   }
              }

              // Fallback: Basic validation if clientId is unknown (less secure)
              // Allow only local URLs during development for safety if client is unknown
              if (Url.IsLocalUrl(uri)) {
                  _logger.LogDebug("Allowing local post-logout URI '{Uri}' as fallback.", uri);
                   return true;
              }

               _logger.LogWarning("Disallowing post-logout URI '{Uri}' due to unknown client or non-local URL.", uri);
              return false; // Disallow external URIs if client isn't known/verified
         }


        // --- Get User From Temp Token ---
        // Fetches UserProfile associated with a temporary 2FA token
        private async Task<UserProfile?> GetUserFromTempToken(string tempToken) {
             if(string.IsNullOrEmpty(tempToken)) return null;
              try {
                   var conn = GetConnection();
                   var nowMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                   // Find non-expired, non-used token
                   var tokenRecord = conn.Db.TwoFactorToken.Iter()
                                       .FirstOrDefault(t => t.Token == tempToken && !t.IsUsed && t.ExpiresAt > nowMs);
                   if (tokenRecord == null) {
                        _logger.LogWarning("GetUserFromTempToken: Temp token not found, expired, or used: {Token}", tempToken);
                        return null;
                   }
                    _logger.LogDebug("GetUserFromTempToken: Found valid temp token record for User ID: {UserId}", tokenRecord.UserId);
                    // Mark token as used IMMEDIATELY after finding it to prevent reuse (race condition mitigation)
                     try {
                         conn.Reducers.UpdateTwoFactorToken(tokenRecord.Id, tokenRecord.UserId, tokenRecord.Token, true, tokenRecord.ExpiresAt);
                         _logger.LogInformation("Marked temp token {TempToken} as used.", tempToken);
                     } catch (Exception reducerEx) {
                          _logger.LogError(reducerEx, "Failed to mark temp token {TempToken} as used via reducer.", tempToken);
                           // Decide how to handle this - maybe proceed but log heavily, or fail the request? Failing is safer.
                           return null;
                     }

                   return await GetUserByIdentityAsync(tokenRecord.UserId);
              } catch (Exception ex) {
                   _logger.LogError(ex, "Error retrieving user from temp token {TempToken}", tempToken);
                   return null;
              }
        }


        #endregion
    }
}
