using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using SpacetimeDB;
using SpacetimeDB.Types;
using TicketSalesApp.Services.Interfaces;
using BRU_AVTOPARK_AspireAPI.ApiService.Models.Auth;
using BRU_AVTOPARK_AspireAPI.ApiService.Services.Auth;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Controllers
{
    /// <summary>
    /// Thin, partial AuthController that wires together the injected services.
    /// Domain-specific endpoints live in separate partial-class files:
    ///   - AuthController.Login.cs      (login / register / claim-account)
    ///   - AuthController.Totp.cs       (TOTP setup, verify, disable, validate)
    ///   - AuthController.WebAuthn.cs   (WebAuthn register / login / validate / credentials)
    ///   - AuthController.MagicLink.cs  (magic-link send & validate)
    ///   - AuthController.QrAuth.cs     (QR generate / login / check)
    ///   - AuthController.OpenId.cs     (authorize, token, userinfo, client management)
    ///   - AuthController.Profile.cs    (profile, logout)
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public partial class AuthController : ControllerBase
    {
        // ── Injected services ───────────────────────────────────────────

        private readonly IAuthenticationService _authService;
        private readonly IQRAuthenticationService _qrAuthService;
        private readonly IUserService _userService;
        private readonly ITotpService _totpService;
        private readonly IWebAuthnService _webAuthnService;
        private readonly IMagicLinkService _magicLinkService;
        private readonly IOpenIdConnectService _openIdConnectService;
        private readonly ITokenService _tokenService;
        private readonly IAuthHtmlRenderer _htmlRenderer;
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
            ITokenService tokenService,
            IAuthHtmlRenderer htmlRenderer,
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
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _htmlRenderer = htmlRenderer ?? throw new ArgumentNullException(nameof(htmlRenderer));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _spacetimeService = spacetimeService ?? throw new ArgumentNullException(nameof(spacetimeService));
        }

        // ── Shared helpers available to all partial files ────────────────

        /// <summary>Checks the Accept header to decide between HTML and JSON responses.</summary>
        private bool IsBrowserRequest()
        {
            var accept = Request.Headers.Accept.ToString().ToLower();
            return accept.Contains("text/html") || accept.Contains("*/*");
        }

        /// <summary>Returns an HTML ContentResult (text/html).</summary>
        private ContentResult HtmlContent(string html)
            => Content(html, "text/html");

        /// <summary>Resolves the SpacetimeDB Identity from the current principal.</summary>
        private Identity? GetUserIdentity()
        {
            var identityString = User.FindFirst("identity")?.Value;
            if (string.IsNullOrEmpty(identityString)) return null;

            try
            {
                var conn = _spacetimeService.GetConnection();
                return conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.LegacyUserId.ToString() == identityString)?.UserId;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Looks up a UserProfile by SpacetimeDB Identity.</summary>
        private UserProfile? GetUserByIdentity(Identity? userId)
        {
            if (userId == null) return null;
            try
            {
                var conn = _spacetimeService.GetConnection();
                return conn.Db.UserProfile.Iter()
                    .FirstOrDefault(u => u.UserId.Equals(userId));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Determines whether the current request carries an admin-level token
        /// (via cookie auth, ASP.NET Identity, or a custom JWT Bearer header).
        /// </summary>
        protected bool IsAdmin()
        {
            try
            {
                if (User?.Identity?.IsAuthenticated == true)
                {
                    if (User.IsInRole("Administrator")) return true;
                    var pr = User.FindFirst("primary_role");
                    if (pr?.Value == "1") return true;
                    var roles = User.Claims.Where(c => c.Type == ClaimTypes.Role || c.Type == "role");
                    if (roles.Any(c => c.Value == "Administrator" || c.Value == "1")) return true;
                }

                var authHeader = Request.Headers["Authorization"].ToString();
                if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ")) return false;

                var token = authHeader["Bearer ".Length..];
                var handler = new JwtSecurityTokenHandler();
                if (!handler.CanReadToken(token)) return false;

                var jwt = handler.ReadJwtToken(token);
                var jwtPr = jwt.Claims.FirstOrDefault(c => c.Type == "primary_role");
                if (jwtPr?.Value == "1") return true;

                return jwt.Claims.Where(c => c.Type == "role").Any(c => c.Value == "1");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking admin status");
                return false;
            }
        }

        /// <summary>Checks the current token for a specific permission claim.</summary>
        protected bool HasPermission(string permissionName)
        {
            try
            {
                if (User?.Identity?.IsAuthenticated == true)
                {
                    if (User.Claims.Where(c => c.Type == "permission").Any(c => c.Value == permissionName))
                        return true;
                }

                var authHeader = Request.Headers["Authorization"].ToString();
                if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ")) return false;

                var token = authHeader["Bearer ".Length..];
                var handler = new JwtSecurityTokenHandler();
                if (!handler.CanReadToken(token)) return false;

                var jwt = handler.ReadJwtToken(token);
                return jwt.Claims.Where(c => c.Type == "permission").Any(c => c.Value == permissionName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking permission: {Permission}", permissionName);
                return false;
            }
        }

        /// <summary>
        /// Creates a UserDto from a UserProfile, calling the auth service
        /// to resolve the primary role.
        /// </summary>
        private UserDto ToUserDto(UserProfile user) => new()
        {
            Id = user.LegacyUserId,
            Username = user.Login,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            Role = _authService.GetUserRole(user.UserId)
        };

        /// <summary>Sets a persistent cookie for the given JWT.</summary>
        private async Task SetAuthCookieAsync(string jwtTokenString)
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(jwtTokenString);
            var claims = jwtToken.Claims.ToList();
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24)
                });
        }
    }
}
