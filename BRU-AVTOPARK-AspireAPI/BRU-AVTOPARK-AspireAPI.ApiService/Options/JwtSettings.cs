using System.ComponentModel.DataAnnotations;

namespace TicketSalesApp.AdminServer.Configuration
{
    /// <summary>
    /// Strongly-typed options for JWT token generation and validation.
    /// Bound from the "JwtSettings" section of appsettings.json.
    ///
    /// Generation settings control what claims and lifetimes are embedded in
    /// tokens issued by <see cref="BRU_AVTOPARK.Services.Implementations.TokenService"/>.
    ///
    /// Validation toggles (RequireExpiration, ValidateNbf, ValidateIssuer, ValidateAudience)
    /// apply to all local JWT validators: <see cref="TicketSalesApp.AdminServer.Controllers.BaseController"/>
    /// (<c>ValidateJwtLocalAsync</c>) and <see cref="BRU_AVTOPARK.Services.Implementations.TokenService"/>
    /// (<c>ValidateToken</c>). They do NOT affect the JWT bearer middleware unless explicitly wired in Program.cs.
    /// </summary>
    public class JwtSettings
    {
        public const string SectionName = "JwtSettings";

        // ── Signing ──────────────────────────────────────────────────────────

        /// <summary>
        /// HMAC-SHA256 signing secret. Must be at least 16 characters.
        /// </summary>
        [Required]
        [MinLength(16)]
        public string Secret { get; set; } = string.Empty;

        // ── Issuer / Audience ────────────────────────────────────────────────

        /// <summary>
        /// Value written into the "iss" claim of every generated token.
        /// Also used as ValidIssuer during validation when ValidateIssuer is true.
        /// Defaults to "https://localhost:5001".
        /// </summary>
        public string Issuer { get; set; } = "https://localhost:5001";

        /// <summary>
        /// Value written into the "aud" claim of every generated token.
        /// Also used as ValidAudience during validation when ValidateAudience is true.
        /// Defaults to "https://localhost:5001".
        /// </summary>
        public string Audience { get; set; } = "https://localhost:5001";

        // ── Lifetime ─────────────────────────────────────────────────────────

        /// <summary>
        /// Access-token lifetime in minutes. Written as the "exp" claim.
        /// Defaults to 120 minutes (2 hours).
        /// </summary>
        [Range(1, 10080)]
        public int ExpirationInMinutes { get; set; } = 120;

        /// <summary>
        /// Seconds before the issue time at which the token becomes valid ("nbf" claim).
        /// A value of 0 means the token is valid immediately (nbf == iat).
        /// A small positive value (e.g. 5) provides a grace window for clock-skew
        /// between the issuer and consumers that check nbf strictly.
        /// Defaults to 0.
        /// </summary>
        [Range(0, 300)]
        public int NotBeforeOffsetSeconds { get; set; } = 0;

        // ── Validation toggles ───────────────────────────────────────────────

        /// <summary>
        /// When true, the "exp" claim is required and validated during local JWT
        /// validation. Disabling this is strongly discouraged in production.
        /// Defaults to true.
        /// </summary>
        public bool RequireExpiration { get; set; } = true;

        /// <summary>
        /// When true, the "nbf" (not-before) claim is enforced during local JWT
        /// validation. Tokens presented before their nbf time are rejected.
        /// Defaults to true.
        /// </summary>
        public bool ValidateNbf { get; set; } = true;

        /// <summary>
        /// When true, the "iss" claim is validated against <see cref="Issuer"/>
        /// during local JWT validation.
        /// Defaults to true.
        /// </summary>
        public bool ValidateIssuer { get; set; } = true;

        /// <summary>
        /// When true, the "aud" claim is validated against <see cref="Audience"/>
        /// during local JWT validation.
        /// Defaults to true.
        /// </summary>
        public bool ValidateAudience { get; set; } = true;

        /// <summary>
        /// Clock-skew tolerance applied to exp/nbf checks, in minutes.
        /// Defaults to 5 minutes.
        /// </summary>
        [Range(0, 60)]
        public int ClockSkewMinutes { get; set; } = 5;
    }
}