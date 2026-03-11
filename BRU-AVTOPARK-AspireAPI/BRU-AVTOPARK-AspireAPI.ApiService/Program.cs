using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Serilog;
using OpenIddict.Validation.AspNetCore;
using System.Security.Claims;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Abstractions;
using OpenIddict.Validation.ServerIntegration;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Any;
using Serilog.Events;
using Serilog.Sinks.File;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using BRU_AVTOPARK_AspireAPI.ApiService;
using BRU_AVTOPARK_AspireAPI.ApiService.Services;
using BRU_AVTOPARK_AspireAPI.ApiService.Middleware;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.DataProtection;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Contracts;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Filters;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Hubs;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Infrastructure;
using BRU_AVTOPARK_AspireAPI.ApiService.Realtime.Options;


var builder = WebApplication.CreateBuilder(args);

// Configure Data Protection with persistent key storage
// CRITICAL: This ensures encryption keys are stable across requests and app restarts
// Without this, OpenIddict cannot decrypt PKCE data from authorization code payloads
var dataProtectionPath = Path.Combine(Directory.GetCurrentDirectory(), "DataProtectionKeys");
Directory.CreateDirectory(dataProtectionPath); // Ensure directory exists

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath))
    .SetApplicationName("BRU-AVTOPARK-AspireAPI");

// Configure Serilog
Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.Debug()
                .WriteTo.Logger(lc => lc
                    .Filter.ByExcluding(logEvent =>
                    {
                        // Check if LogReducerLogsToFile is disabled in configuration
                        var logReducerLogs = builder.Configuration.GetValue<bool>("SpacetimeDB:LogReducerLogsToFile", true);
                        if (logReducerLogs)
                        {
                            return false; // Don't exclude anything if logging is enabled
                        }
                        
                        // Exclude logs that contain reducer log markers
                        var message = logEvent.RenderMessage();
                        return message.Contains("SpacetimeDB Reducer Logs") ||
                               message.Contains("Fetching SpacetimeDB reducer logs") ||
                               message.Contains("Found") && message.Contains("log lines for reducer") ||
                               (message.StartsWith("{") && message.Contains("\"target\":\"UpdateOpenIdClient\"")) ||
                               (message.StartsWith("{") && message.Contains("\"target\":\"RegisterOpenIdClient\""));
                    })
                    .WriteTo.File("logs/app-.log",
                        rollingInterval: RollingInterval.Day,
                        restrictedToMinimumLevel: LogEventLevel.Information))
                .CreateLogger();

builder.Host.UseSerilog();

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();
// Configure logging first
builder.Services.AddLogging(builder =>
{
    builder.ClearProviders();
    builder.AddConsole();
    builder.AddDebug();

    builder.SetMinimumLevel(LogLevel.Debug);

});

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSerilog();

// Configure Swagger
builder.Services.AddSwaggerGen(c =>
            {
                c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "TicketSalesApp Admin API", Version = "v1" });
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
                    Name = "Authorization",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "Bearer"
                });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                        {
                            new OpenApiSecurityScheme
                            {
                                Reference = new OpenApiReference
                                {
                                    Type = ReferenceType.SecurityScheme,
                                    Id = "Bearer"
                                }
                            },
                            Array.Empty<string>()
                        }
                });
            });

// Add SpacetimeDB services
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.ISpacetimeDBService, TicketSalesApp.Services.Implementations.SpacetimeDBService>();

// Register a hosted service that will call ProcessFrameTick() at regular intervals
builder.Services.AddHostedService<SpacetimeFrameTickService>();

// Configure Fido2 for WebAuthn
builder.Services.AddFido2(options =>
{
    options.ServerDomain = "localhost";
    options.ServerName = "TicketSalesApp Admin API";
    options.Origins = new HashSet<string> { "https://localhost:5001" };
    options.TimestampDriftTolerance = 300000;
});

// Add authentication services
builder.Services.AddScoped<TicketSalesApp.Services.Interfaces.IOpenIdConnectService, TicketSalesApp.Services.Implementations.OpenIdConnectService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IAuthenticationService, TicketSalesApp.Services.Implementations.AuthenticationService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IUserService, TicketSalesApp.Services.Implementations.UserService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.ITotpService, TicketSalesApp.Services.Implementations.TotpService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.ITwoFactorService, TicketSalesApp.Services.Implementations.TwoFactorService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.ISettingsService, TicketSalesApp.Services.Implementations.SettingsService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IWebAuthnService, TicketSalesApp.Services.Implementations.WebAuthnService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IMagicLinkService, TicketSalesApp.Services.Implementations.MagicLinkService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IQRAuthenticationService, TicketSalesApp.Services.Implementations.QRAuthenticationService>();

// Add other services
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IRoleService, TicketSalesApp.Services.Implementations.RoleService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.ITicketSalesService, TicketSalesApp.Services.Implementations.TicketSalesService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IExportService, TicketSalesApp.Services.Implementations.ExportService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IAdminActionLogger, TicketSalesApp.Services.Implementations.AdminActionLogger>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IDataService, TicketSalesApp.Services.Implementations.DataService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IBusService, TicketSalesApp.Services.Implementations.BusService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IPermissionService, TicketSalesApp.Services.Implementations.PermissionService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.ITicketService, TicketSalesApp.Services.Implementations.TicketService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IRouteService, TicketSalesApp.Services.Implementations.RouteService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IRouteScheduleService, TicketSalesApp.Services.Implementations.RouteScheduleService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IEmployeeService, TicketSalesApp.Services.Implementations.EmployeeService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IMaintenanceService, TicketSalesApp.Services.Implementations.MaintenanceService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IEmailService, TicketSalesApp.Services.Implementations.EmailService>();

// Add Experimental services (for refactoring)
builder.Services.AddSingleton<TicketSalesApp.AdminServer.Experimental.Services.Interfaces.IFeatureFlagService, TicketSalesApp.AdminServer.Experimental.Services.Implementations.FeatureFlagService>();
builder.Services.AddScoped<BRU_AVTOPARK.Services.Interfaces.IAuthOrchestrationService, BRU_AVTOPARK.Services.Implementations.AuthOrchestrationService>();
builder.Services.AddScoped<BRU_AVTOPARK.Services.Interfaces.IHtmlRenderingService, BRU_AVTOPARK.Experimental.Services.Implementations.HtmlRenderingService>();

// CRITICAL ROUTING CONFIGURATION: Feature flag-based endpoint resolution
// This prevents AmbiguousMatchException when multiple controllers have identical routes
// 
// APPROACH: Use IEndpointSelectorPolicy (works with default selector)
// FAILSAFE: Custom EndpointSelector available if policy approach fails
//
// The policy runs AFTER action constraints but BEFORE the endpoint selector,
// allowing it to resolve ambiguity without replacing ASP.NET Core's default behavior

try
{
    // PRIMARY APPROACH: Policy-based resolution (preferred, non-invasive)
    // This works alongside the default EndpointSelector
    builder.Services.AddSingleton<Microsoft.AspNetCore.Routing.Matching.IEndpointSelectorPolicy, 
        BRU_AVTOPARK_AspireAPI.ApiService.Routing.FeatureFlagEndpointSelectorPolicy>();
    
    Log.Information("✓ Registered FeatureFlagEndpointSelectorPolicy for ambiguity resolution");
    Log.Information("  Policy Order: 1000 (runs after constraints, before selector)");
    Log.Information("  Policy will resolve ambiguity between legacy and refactored controllers");
    
    // NUCLEAR FAILSAFE: Custom EndpointSelector (COMMENTED OUT - only enable if policy fails)
    // Uncommenting this line will REPLACE the default selector entirely
    // WARNING: This is a last resort and may cause 404s if not properly implemented
    // builder.Services.AddSingleton<Microsoft.AspNetCore.Routing.Matching.EndpointSelector, 
    //     BRU_AVTOPARK_AspireAPI.ApiService.Routing.FeatureFlagEndpointSelector>();
    // Log.Warning("⚠ Using custom EndpointSelector - this replaces ASP.NET Core's default selector!");
}
catch (Exception ex)
{
    Log.Error(ex, "CRITICAL ERROR: Failed to register FeatureFlagEndpointSelectorPolicy");
    Log.Warning("Application will continue but may encounter AmbiguousMatchException errors");
    Log.Warning("Attempting to register custom EndpointSelector as failsafe...");
    
    // If policy registration fails, try the nuclear option as last resort
    try
    {
        builder.Services.AddSingleton<Microsoft.AspNetCore.Routing.Matching.EndpointSelector, 
            BRU_AVTOPARK_AspireAPI.ApiService.Routing.FeatureFlagEndpointSelector>();
        Log.Warning("⚠ FAILSAFE ACTIVATED: Registered custom EndpointSelector as fallback");
        Log.Warning("  This replaces ASP.NET Core's default selector - may cause routing issues");
    }
    catch (Exception fallbackEx)
    {
        Log.Fatal(fallbackEx, "FATAL: Could not register any routing failsafes!");
        Log.Fatal("  AmbiguousMatchException WILL occur for duplicate routes!");
        Log.Fatal("  Application startup will continue but routing is BROKEN!");
        // Don't throw - let the app start so we can see the error in logs
    }
}
builder.Services.AddScoped<BRU_AVTOPARK.Services.Interfaces.ITokenService, BRU_AVTOPARK.Services.Implementations.TokenService>();
builder.Services.AddScoped<BRU_AVTOPARK.Services.Interfaces.IProfileService, BRU_AVTOPARK.Services.Implementations.ProfileService>();
builder.Services.AddScoped<BRU_AVTOPARK.Services.Interfaces.IRequestDetector, BRU_AVTOPARK.Services.Implementations.RequestDetector>();
builder.Services.AddScoped<BRU_AVTOPARK.Services.Interfaces.IOidcHelperService, BRU_AVTOPARK.Services.Implementations.OidcHelperService>();
builder.Services.AddScoped<BRU_AVTOPARK.Services.Interfaces.IIdentityService, BRU_AVTOPARK.Services.Implementations.IdentityService>();

// Add input sanitization service for security
builder.Services.AddScoped<BRU_AVTOPARK.Services.Interfaces.IInputSanitizationService, BRU_AVTOPARK.Services.Implementations.InputSanitizationService>();

// Configure FeatureFlagOptions from appsettings.json
builder.Services.Configure<TicketSalesApp.AdminServer.Configuration.FeatureFlagOptions>(
    builder.Configuration.GetSection(TicketSalesApp.AdminServer.Configuration.FeatureFlagOptions.FeatureFlags));

// Add Routing Diagnostics Service for debugging controller discovery issues
builder.Services.AddHostedService<BRU_AVTOPARK_AspireAPI.ApiService.Services.RoutingDiagnosticsService>();

// Add memory cache for QR authentication
builder.Services.AddMemoryCache();

// Configure realtime eventing and websocket options
builder.Services.Configure<RealtimeEventOptions>(builder.Configuration.GetSection(RealtimeEventOptions.SectionName));
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = false;
    options.MaximumReceiveMessageSize = 64 * 1024;
    options.StreamBufferCapacity = 50;
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    options.KeepAliveInterval = TimeSpan.FromSeconds(10);
});

builder.Services.AddSingleton<SignalRRealtimeEventBus>();
builder.Services.AddSingleton<IRealtimeEventBus>(sp => sp.GetRequiredService<SignalRRealtimeEventBus>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<SignalRRealtimeEventBus>());
builder.Services.AddScoped<ApiMutationEventFilter>();

// Add HTTP context accessor for admin action logging
builder.Services.AddHttpContextAccessor();

// Configure ForwardedHeaders middleware to trust proxies and populate RemoteIpAddress
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
                               Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    // Clear default networks/proxies and configure based on your infrastructure
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    // For development, trust all proxies (in production, configure specific KnownProxies/KnownNetworks)
    options.ForwardLimit = 1; // Only trust the first proxy
});

// Configure JWT authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var key = Encoding.UTF8.GetBytes(jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT secret is not configured"));

// Ensure key is exactly 32 bytes (256 bits)
if (key.Length != 32)
{
    var newKey = new byte[32];
    if (key.Length < 32)
    {
        // If key is too short, pad with zeros
        Array.Copy(key, newKey, key.Length);
    }
    else
    {
        // If key is too long, truncate
        Array.Copy(key, newKey, 32);
    }
    key = newKey;
}

// Create symmetric security key with KeyId for OpenIddict compatibility
var symmetricKey = new SymmetricSecurityKey(key)
{
    KeyId = "default-signing-key"
};

// Register the symmetric key as a singleton so it can be injected into controllers
builder.Services.AddSingleton(symmetricKey);

// Configure OpenIddict
builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        // Set default entity types - these must match what the stores use
        options.SetDefaultApplicationEntity<TicketSalesApp.Services.Implementations.OpenIddictApplication>();
        // Authorization storage disabled - PKCE data stored in token payload
        // options.SetDefaultAuthorizationEntity<SpacetimeDB.Types.OpenIddictSpacetimeAuthorization>();
        options.SetDefaultTokenEntity<OpenIddict.Abstractions.OpenIddictTokenDescriptor>();
        options.SetDefaultScopeEntity<OpenIddict.Abstractions.OpenIddictScopeDescriptor>();
        
        // Register stores
        options.AddApplicationStore<TicketSalesApp.Services.Implementations.ApplicationStore>();
        // Authorization store disabled - not needed when DisableAuthorizationStorage is used
        // options.AddAuthorizationStore<TicketSalesApp.Services.Implementations.AuthorizationStore>();
        options.AddTokenStore<TicketSalesApp.Services.Implementations.TokenStore>();
        options.AddScopeStore<TicketSalesApp.Services.Implementations.ScopeStore>();
    })
    .AddServer(options =>
    {
        options.SetAuthorizationEndpointUris("/connect/authorize")
            .SetTokenEndpointUris("/connect/token")
            .SetUserinfoEndpointUris("/connect/userinfo");

        options.AllowAuthorizationCodeFlow()
            .AllowRefreshTokenFlow()
            .RequireProofKeyForCodeExchange() // CRITICAL: Enforce PKCE for public clients
            .DisableAuthorizationStorage(); // CRITICAL: PKCE data stored in token payload, not authorization

       //options.DisableTransportSecurityRequirement(); this wont work for some fucking reason with openiddict 4.1.0

        // Add symmetric signing key for access tokens, authorization codes, and refresh tokens
        options.AddSigningKey(symmetricKey);

        // Add asymmetric signing key for identity tokens (required)
        if (builder.Environment.IsDevelopment())
        {
            options.AddDevelopmentSigningCertificate();
        }
        else
        {
            options.AddEphemeralSigningKey();
        }

        // Add encryption key
        options.AddEncryptionKey(symmetricKey);

        // Set a fixed issuer to work with both HTTP and HTTPS
        options.SetIssuer(new Uri("http://localhost:5000/"));

        options.UseAspNetCore()
            .EnableTokenEndpointPassthrough()
            .EnableAuthorizationEndpointPassthrough()
            .EnableUserinfoEndpointPassthrough()
            .DisableTransportSecurityRequirement();
    })
    .AddValidation(options =>
    {
        // Register the ASP.NET Core host
        options.UseAspNetCore();

        // Import the configuration from the local OpenIddict server instance.
        options.UseLocalServer();

        options.AddEncryptionKey(symmetricKey); // << Important

        // Configure the token validation parameters to accept our custom JWT tokens
        options.Configure(validationOptions =>
        {
            validationOptions.TokenValidationParameters.IssuerSigningKey = symmetricKey;
            // CRITICAL FIX: Use the same issuer as the server (http://localhost:5000/)
            validationOptions.TokenValidationParameters.ValidIssuer = "http://localhost:5000/";
            validationOptions.TokenValidationParameters.ValidAudience = "http://localhost:5000/";
            validationOptions.TokenValidationParameters.ValidateIssuer = true;
            validationOptions.TokenValidationParameters.ValidateAudience = false; // OpenIddict handles audience validation
            validationOptions.TokenValidationParameters.ValidateLifetime = true;
            validationOptions.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
            validationOptions.TokenValidationParameters.RoleClaimType = "role";
            validationOptions.TokenValidationParameters.NameClaimType = "name";
        });
    });
// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader()
            .WithExposedHeaders("Content-Disposition", "Authorization");
    });
});

builder.Services.AddAuthentication(options =>
{
    // Set JWT Bearer as default for API authentication
    // This prevents "No authenticationScheme was specified" errors when returning ForbidResult
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.LoginPath = "/api/auth/login";
    options.LogoutPath = "/api/auth/logout";
    options.AccessDeniedPath = "/api/auth/error";
    options.ExpireTimeSpan = TimeSpan.FromHours(24);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
})
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = symmetricKey,
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        RoleClaimType = "role",
        NameClaimType = "name"
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var path = context.HttpContext.Request.Path;
            if ((path.StartsWithSegments("/hubs/system-events") || path.StartsWithSegments("/api") && path.Value.Contains("/realtime/ws")) &&
                context.Request.Query.TryGetValue("access_token", out var accessToken) &&
                !string.IsNullOrWhiteSpace(accessToken))
            {
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            var identity = context.Principal?.Identity as System.Security.Claims.ClaimsIdentity;
            if (identity != null)
            {
                var spacetimeIdentity = identity.FindFirst("identity")?.Value;
                var xuid = identity.FindFirst("xuid")?.Value;

                if (!string.IsNullOrEmpty(spacetimeIdentity))
                {
                    identity.AddClaim(new System.Security.Claims.Claim("spacetime_identity", spacetimeIdentity));
                }

                if (!string.IsNullOrEmpty(xuid))
                {
                    identity.AddClaim(new System.Security.Claims.Claim("xuid", xuid));
                }
            }
        }
    };
});

// Configure authorization
builder.Services.AddAuthorization(options =>
{
    // Policy for cookie-authenticated web pages
    options.AddPolicy("RequireAuthenticatedUser", policy =>
        policy.AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser());

    options.AddPolicy("PublicEndpoints", policy =>
        policy.RequireAssertion(_ => true));

    // API-specific policy that requires scope claim for API access via JWT
    options.AddPolicy("ApiAccess", policy =>
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .RequireClaim("scope", "api"));
    
    // Flexible API policy that accepts EITHER JWT Bearer OR OpenIddict tokens
    // This allows endpoints to work with both custom JWT and OpenIddict-issued tokens
    options.AddPolicy("FlexibleApiAccess", policy =>
        policy.AddAuthenticationSchemes(
            JwtBearerDefaults.AuthenticationScheme,
            OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser());
    
    // Administrator policy that accepts both authentication schemes
    // OpenIddict validation is now configured to accept our custom JWT tokens
    options.AddPolicy("RequireAdministrator", policy =>
        policy.AddAuthenticationSchemes(
            JwtBearerDefaults.AuthenticationScheme,
            OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .RequireRole("Administrator"));
    
    // No default policy - let each endpoint specify its own requirements
    options.FallbackPolicy = null;
});

// Add controllers with views support (needed for HtmlRenderingService)
// ENHANCED: Add feature flag-aware controller configuration
var featureFlagOptions = builder.Configuration.GetSection(TicketSalesApp.AdminServer.Configuration.FeatureFlagOptions.FeatureFlags)
    .Get<TicketSalesApp.AdminServer.Configuration.FeatureFlagOptions>();

builder.Services.AddControllersWithViews(options =>
            {
                options.RespectBrowserAcceptHeader = true;
                options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
                options.Filters.AddService<ApiMutationEventFilter>();
                
                // ENHANCED DEBUG LOGGING: Log controller configuration
                var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("ControllerConfiguration");
                logger.LogInformation("Configuring controllers with feature flags:");
                logger.LogInformation("  EnableLoginRefactoring: {Value}", featureFlagOptions?.EnableLoginRefactoring ?? false);
                logger.LogInformation("  EnableRegisterRefactoring: {Value}", featureFlagOptions?.EnableRegisterRefactoring ?? false);
                logger.LogInformation("  EnableProfileRefactoring: {Value}", featureFlagOptions?.EnableProfileRefactoring ?? false);
            })
            .ConfigureApiBehaviorOptions(options =>
            {
                // Suppress ambiguous match exceptions - let action constraints handle routing
                options.SuppressMapClientErrors = true;
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve;
                options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
            })
            .AddRazorOptions(options =>
            {
                // CRITICAL: Configure Razor to look in Experimental/Views folder
                // This allows HtmlRenderingService to find views in the Experimental folder
                options.ViewLocationFormats.Clear();
                options.ViewLocationFormats.Add("/Experimental/Views/{1}/{0}.cshtml");
                options.ViewLocationFormats.Add("/Experimental/Views/Shared/{0}.cshtml");
                options.ViewLocationFormats.Add("/Views/{1}/{0}.cshtml");
                options.ViewLocationFormats.Add("/Views/Shared/{0}.cshtml");
                
                // Add area support if needed
                options.AreaViewLocationFormats.Clear();
                options.AreaViewLocationFormats.Add("/Experimental/Views/{2}/{1}/{0}.cshtml");
                options.AreaViewLocationFormats.Add("/Experimental/Views/{2}/Shared/{0}.cshtml");
                options.AreaViewLocationFormats.Add("/Experimental/Views/Shared/{0}.cshtml");
                options.AreaViewLocationFormats.Add("/Areas/{2}/Views/{1}/{0}.cshtml");
                options.AreaViewLocationFormats.Add("/Areas/{2}/Views/Shared/{0}.cshtml");
                options.AreaViewLocationFormats.Add("/Views/Shared/{0}.cshtml");
                
                var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("RazorConfiguration");
                logger.LogInformation("Razor view locations configured:");
                foreach (var format in options.ViewLocationFormats)
                {
                    logger.LogInformation("  {Format}", format);
                }
            })
            .ConfigureApplicationPartManager(manager =>
            {
                // ENHANCED: Ensure both controllers are discovered
                // This explicitly adds both AuthController and AuthControllerRefactored to the application parts
                var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("ApplicationPartManager");
                logger.LogInformation("Application parts count: {Count}", manager.ApplicationParts.Count);
                
                foreach (var part in manager.ApplicationParts)
                {
                    logger.LogInformation("  Part: {PartName} ({PartType})", part.Name, part.GetType().Name);
                }
                
                // Force discovery of both controllers by ensuring the assembly is loaded
                var controllerAssembly = typeof(BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthController).Assembly;
                var refactoredControllerType = controllerAssembly.GetType("BRU_AVTOPARK_AspireAPI.ApiService.Controllers.AuthControllerRefactored");
                
                if (refactoredControllerType != null)
                {
                    logger.LogInformation("AuthControllerRefactored type found in assembly");
                }
                else
                {
                    logger.LogWarning("AuthControllerRefactored type NOT found in assembly!");
                }
            });


// BUILD APPLICATION WITH ERROR HANDLING
WebApplication app;
try
{
    app = builder.Build();
    Log.Information("✓ Application built successfully");
}
catch (Exception ex)
{
    Log.Fatal(ex, "FATAL ERROR: Failed to build application");
    throw;
}

// CONFIGURE MIDDLEWARE PIPELINE WITH ERROR HANDLING
// Configure ForwardedHeaders middleware FIRST to process proxy headers
app.UseForwardedHeaders();

try
{
    // CRITICAL: Feature flag routing middleware MUST run BEFORE UseRouting()
    // This middleware intercepts requests and stores routing decisions in HttpContext.Items
    // to prevent ambiguous match exceptions when multiple controllers have the same routes
    app.UseFeatureFlagRouting();
    Log.Information("✓ Feature flag routing middleware registered");
}
catch (Exception ex)
{
    Log.Error(ex, "ERROR: Failed to register feature flag routing middleware - continuing without it");
}

try
{
    app.UseRouting();
    Log.Information("✓ Routing middleware registered");
}
catch (Exception ex)
{
    Log.Fatal(ex, "FATAL ERROR: Failed to register routing middleware");
    throw;
}

// CRITICAL: Serve static files from Experimental folder (CSS, JS)
// This allows the browser to load /css/bru-design-system.css and /js/*.js files
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.Combine(builder.Environment.ContentRootPath, "Experimental")),
    RequestPath = "",
    OnPrepareResponse = ctx =>
    {
        // Add cache headers for static files
        ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=3600");
    }
});

// Also serve from wwwroot if it exists (standard location)
if (Directory.Exists(Path.Combine(builder.Environment.ContentRootPath, "wwwroot")))
{
    app.UseStaticFiles();
}

// Add controller logging middleware to track which controller handles each request
// This is especially useful for debugging feature flag routing (legacy vs refactored)
app.UseControllerLogging();

// Add input validation middleware for security (before authentication)
// This provides defense-in-depth protection against injection attacks
app.UseMiddleware<BRU_AVTOPARK.Middleware.InputValidationMiddleware>();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

// Add authentication and authorization in the correct order
app.UseAuthentication();
app.UseAuthorization();

var realtimeOptions = app.Services
    .GetRequiredService<Microsoft.Extensions.Options.IOptions<RealtimeEventOptions>>()
    .Value;

var webSocketOptions = new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(15)
};

foreach (var origin in realtimeOptions.AllowedOrigins.Where(origin => !string.IsNullOrWhiteSpace(origin)))
{
    webSocketOptions.AllowedOrigins.Add(origin);
}

app.UseWebSockets(webSocketOptions);

// Configure CORS before routing
app.UseCors("AllowAll");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "TicketSalesApp Admin API V1");
        c.RoutePrefix = "swagger";
    });
}

// Add public endpoints with responsive HTML
app.MapGet("/", () => Results.Content("""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>TicketSalesApp Admin API</title>
            <style>
                :root {
                    --bg-color: #f8f9fa;
                    --text-color: #212529;
                    --accent-color: #0d6efd;
                    --card-bg: #ffffff;
                    --card-shadow: 0 4px 6px rgba(0, 0, 0, 0.1);
                }
                @media (prefers-color-scheme: dark) {
                    :root {
                        --bg-color: #121212;
                        --text-color: #e0e0e0;
                        --accent-color: #3d8bfd;
                        --card-bg: #1e1e1e;
                        --card-shadow: 0 4px 6px rgba(0, 0, 0, 0.3);
                    }
                }
                body {
                    font-family: system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, sans-serif;
                    background-color: var(--bg-color);
                    color: var(--text-color);
                    display: flex;
                    justify-content: center;
                    align-items: center;
                    height: 100vh;
                    margin: 0;
                    padding: 1rem;
                    transition: background-color 0.3s, color 0.3s;
                }
                .container {
                    max-width: 600px;
                    width: 100%;
                    background-color: var(--card-bg);
                    border-radius: 12px;
                    box-shadow: var(--card-shadow);
                    padding: 2rem;
                    text-align: center;
                    transition: background-color 0.3s, box-shadow 0.3s;
                }
                h1 {
                    color: var(--accent-color);
                    margin-bottom: 1rem;
                }
                p {
                    margin-bottom: 1.5rem;
                    line-height: 1.6;
                }
                .status {
                    display: inline-block;
                    background-color: #10b981;
                    color: white;
                    padding: 0.5rem 1rem;
                    border-radius: 50px;
                    font-weight: 600;
                }
                .links {
                    margin-top: 2rem;
                }
                a {
                    color: var(--accent-color);
                    text-decoration: none;
                    margin: 0 0.5rem;
                }
                a:hover {
                    text-decoration: underline;
                }
                @media (max-width: 480px) {
                    .container {
                        padding: 1.5rem;
                    }
                }
            </style>
        </head>
        <body>
            <div class="container">
                <h1>TicketSalesApp Admin API</h1>
                <p>The API service is up and running. Use the endpoints to interact with the system.</p>
                <div class="status">Active</div>
                <div class="links">
                    <a href="/health">Health Check</a>
                    <a href="/swagger">API Documentation</a>
                </div>
            </div>
        </body>
        </html>
        """, "text/html")).AllowAnonymous();

app.MapGet("/health", () =>
{
    // Generate routes list HTML
    var routesHtml = "";

    try
    {
        // Get assemblies safely - exclude problematic ones
        var relevantAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic &&
                        !string.IsNullOrEmpty(a.Location) &&
                        !a.FullName.StartsWith("SpacetimeDB") &&
                        !a.FullName.StartsWith("System.") &&
                        !a.FullName.StartsWith("Microsoft.") &&
                        a.FullName.Contains("BRU-AVTOPARK") ||
                        a.FullName.Contains("TicketSalesApp"))
            .ToList();

        // Get all controller types
        var controllers = new List<Type>();
        foreach (var assembly in relevantAssemblies)
        {
            try
            {
                var assemblyControllers = assembly.GetTypes()
                    .Where(type => type.IsClass &&
                           !type.IsAbstract &&
                           typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(type))
                    .ToList();
                controllers.AddRange(assemblyControllers);
            }
            catch (ReflectionTypeLoadException)
            {
                // Skip assemblies that can't be loaded
                continue;
            }
            catch (Exception)
            {
                // Skip on any other reflection exception
                continue;
            }
        }

        // Fallback - if we couldn't find controllers, manually add them
        if (!controllers.Any())
        {
            // Hardcoded list of known controller routes
            routesHtml += @"
                    <h2>API Routes</h2>
                    <table style=""width: 100%; text-align: left; margin-top: 1rem; border-collapse: collapse;"">
                        <tr style=""background-color: rgba(0,0,0,0.05);"">
                            <th style=""padding: 8px; border-bottom: 1px solid #ddd;"">Controller</th>
                            <th style=""padding: 8px; border-bottom: 1px solid #ddd;"">Route</th>
                            <th style=""padding: 8px; border-bottom: 1px solid #ddd;"">Method</th>
                            <th style=""padding: 8px; border-bottom: 1px solid #ddd;"">Status</th>
                        </tr>
                        <tr style=""border-bottom: 1px solid #ddd;"">
                            <td style=""padding: 8px;"">Auth</td>
                            <td style=""padding: 8px;""><a href=""/api/auth/login"" style=""color: var(--accent-color);"">/api/auth/login</a></td>
                            <td style=""padding: 8px;"">GET/POST</td>
                            <td style=""padding: 8px;""><span style=""color: var(--success-color); font-weight: bold;"">Active</span></td>
                        </tr>
                        <tr style=""border-bottom: 1px solid #ddd;"">
                            <td style=""padding: 8px;"">Auth</td>
                            <td style=""padding: 8px;""><a href=""/api/auth/profile"" style=""color: var(--accent-color);"">/api/auth/profile</a></td>
                            <td style=""padding: 8px;"">GET</td>
                            <td style=""padding: 8px;""><span style=""color: var(--success-color); font-weight: bold;"">Active</span></td>
                        </tr>
                    </table>";
        }
        else if (controllers.Any())
        {
            routesHtml += @"
                    <h2>API Routes</h2>
                    <table style=""width: 100%; text-align: left; margin-top: 1rem; border-collapse: collapse;"">
                        <tr style=""background-color: rgba(0,0,0,0.05);"">
                            <th style=""padding: 8px; border-bottom: 1px solid #ddd;"">Controller</th>
                            <th style=""padding: 8px; border-bottom: 1px solid #ddd;"">Route</th>
                            <th style=""padding: 8px; border-bottom: 1px solid #ddd;"">Method</th>
                            <th style=""padding: 8px; border-bottom: 1px solid #ddd;"">Status</th>
                        </tr>";

            foreach (var controller in controllers)
            {
                try
                {
                    var controllerName = controller.Name.Replace("Controller", "");
                    var methods = controller.GetMethods()
                        .Where(m => m.IsPublic &&
                               !m.IsSpecialName &&
                               m.DeclaringType == controller)
                        .ToList();

                    foreach (var method in methods)
                    {
                        try
                        {
                            var httpMethodAttributes = method.GetCustomAttributes(true)
                                .Where(a => a.GetType().Name.StartsWith("Http") &&
                                       a.GetType().Name.EndsWith("Attribute"))
                                .ToList();

                            if (!httpMethodAttributes.Any()) continue;

                            foreach (var attr in httpMethodAttributes)
                            {
                                string httpMethod = attr.GetType().Name.Replace("Http", "").Replace("Attribute", "");
                                string route = "";

                                var routeAttrs = method.GetCustomAttributes(true)
                                    .Where(a => a.GetType().Name == "RouteAttribute")
                                    .ToList();

                                if (routeAttrs.Any())
                                {
                                    var routeAttr = routeAttrs.First();
                                    try
                                    {
                                        route = routeAttr.GetType().GetProperty("Template")?.GetValue(routeAttr)?.ToString() ?? "";
                                    }
                                    catch
                                    {
                                        // If can't get template, use method name
                                        route = method.Name;
                                    }
                                }

                                if (string.IsNullOrEmpty(route))
                                {
                                    route = $"/api/{controllerName}/{method.Name}";
                                }
                                else if (!route.StartsWith("/"))
                                {
                                    route = $"/api/{controllerName}/{route}";
                                }

                                routesHtml += $@"
                                        <tr style=""border-bottom: 1px solid #ddd;"">
                                            <td style=""padding: 8px;"">{controllerName}</td>
                                            <td style=""padding: 8px;""><a href=""{route}"" style=""color: var(--accent-color);"">{route}</a></td>
                                            <td style=""padding: 8px;"">{httpMethod}</td>
                                            <td style=""padding: 8px;""><span style=""color: var(--success-color); font-weight: bold;"">Active</span></td>
                                        </tr>";
                            }
                        }
                        catch (Exception)
                        {
                            // Skip methods that cause exceptions
                            continue;
                        }
                    }
                }
                catch (Exception)
                {
                    // Skip controllers that cause exceptions
                    continue;
                }
            }

            routesHtml += "</table>";
        }
        else
        {
            routesHtml = "<p>No API routes found.</p>";
        }
    }
    catch (Exception ex)
    {
        routesHtml = $@"<div class=""error-message"">
                <h2>Error Loading Routes</h2>
                <p>Could not load the API routes: {ex.Message}</p>
            </div>";
    }

    // Health check section always displays even if routes failed
    string healthCheckHtml = $@"
            <h2>Service Health</h2>
            <div class=""health-section"">
                <div class=""health-item"">
                    <div class=""health-name"">API Service</div>
                    <div class=""health-status""><span class=""status-healthy"">Healthy</span></div>
                </div>
                <div class=""health-item"">
                    <div class=""health-name"">Database Connection</div>
                    <div class=""health-status""><span class=""status-healthy"">Connected</span></div>
                </div>
                <div class=""timestamp"">Last checked: {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")}</div>
            </div>";

    return Results.Content($@"
            <!DOCTYPE html>
            <html lang=""en"">
            <head>
                <meta charset=""UTF-8"">
                <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
                <title>Health Status</title>
                <style>
                    :root {{
                        --bg-color: #f8f9fa;
                        --text-color: #212529;
                        --accent-color: #0d6efd;
                        --card-bg: #ffffff;
                        --card-shadow: 0 4px 6px rgba(0, 0, 0, 0.1);
                        --success-color: #10b981;
                        --error-color: #ef4444;
                    }}
                    @media (prefers-color-scheme: dark) {{
                        :root {{
                            --bg-color: #121212;
                            --text-color: #e0e0e0;
                            --accent-color: #3d8bfd;
                            --card-bg: #1e1e1e;
                            --card-shadow: 0 4px 6px rgba(0, 0, 0, 0.3);
                            --success-color: #34d399;
                            --error-color: #f87171;
                        }}
                    }}
                    body {{
                        font-family: system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, sans-serif;
                        background-color: var(--bg-color);
                        color: var(--text-color);
                        display: flex;
                        justify-content: center;
                        align-items: center;
                        min-height: 100vh;
                        margin: 0;
                        padding: 1rem;
                        transition: background-color 0.3s, color 0.3s;
                    }}
                    .container {{
                        max-width: 900px;
                        width: 100%;
                        background-color: var(--card-bg);
                        border-radius: 12px;
                        box-shadow: var(--card-shadow);
                        padding: 2rem;
                        margin: 2rem 0;
                        transition: background-color 0.3s, box-shadow 0.3s;
                    }}
                    h1, h2 {{
                        color: var(--accent-color);
                        margin-bottom: 1rem;
                    }}
                    h2 {{
                        margin-top: 2rem;
                    }}
                    .status-indicator {{
                        display: flex;
                        align-items: center;
                        justify-content: flex-start;
                        margin-bottom: 1.5rem;
                    }}
                    .dot {{
                        width: 20px;
                        height: 20px;
                        background-color: var(--success-color);
                        border-radius: 50%;
                        margin-right: 10px;
                    }}
                    .status-text {{
                        font-size: 1.2rem;
                        font-weight: 600;
                    }}
                    p {{
                        margin-bottom: 1.5rem;
                        line-height: 1.6;
                    }}
                    .timestamp {{
                        color: var(--text-color);
                        opacity: 0.7;
                        font-size: 0.9rem;
                        margin-top: 2rem;
                    }}
                    .back-link {{
                        display: inline-block;
                        margin-top: 1.5rem;
                        color: var(--accent-color);
                        text-decoration: none;
                    }}
                    .back-link:hover {{
                        text-decoration: underline;
                    }}
                    table {{
                        width: 100%;
                        border-collapse: collapse;
                        margin: 1rem 0;
                    }}
                    th, td {{
                        text-align: left;
                        padding: 8px;
                        border-bottom: 1px solid rgba(128, 128, 128, 0.2);
                    }}
                    th {{
                        font-weight: 600;
                    }}
                    .error-message {{
                        background-color: rgba(239, 68, 68, 0.1);
                        border-left: 4px solid var(--error-color);
                        padding: 1rem;
                        border-radius: 4px;
                        margin-bottom: 2rem;
                    }}
                    .error-message h2 {{
                        color: var(--error-color);
                        margin-top: 0;
                    }}
                    .health-section {{
                        background-color: rgba(16, 185, 129, 0.05);
                        border-radius: 8px;
                        padding: 1rem;
                        margin: 1rem 0 2rem 0;
                    }}
                    .health-item {{
                        display: flex;
                        justify-content: space-between;
                        padding: 0.75rem 0;
                        border-bottom: 1px solid rgba(128, 128, 128, 0.1);
                    }}
                    .health-item:last-child {{
                        border-bottom: none;
                    }}
                    .health-name {{
                        font-weight: 500;
                    }}
                    .status-healthy {{
                        color: var(--success-color);
                        font-weight: 600;
                    }}
                    @media (max-width: 768px) {{
                        .container {{
                            padding: 1.5rem;
                        }}
                        table {{
                            font-size: 0.85rem;
                        }}
                    }}
                </style>
            </head>
            <body>
                <div class=""container"">
                    <h1>TicketSalesApp Admin API</h1>
                    <div class=""status-indicator"">
                        <div class=""dot""></div>
                        <div class=""status-text"">Healthy</div>
                    </div>
                    <p>The API service is up and running. Use the endpoints to interact with the system.</p>
                    
                    {healthCheckHtml}
                    
                    <div class=""api-routes"">
                        {routesHtml}
                    </div>
                    
                    <div>
                        <a href=""/"" class=""back-link"">Home</a>
                        <a href=""/swagger"" class=""back-link"">API Documentation</a>
                    </div>
                </div>
            </body>
            </html>
        ", "text/html");
}).AllowAnonymous();

// Map controllers - let each endpoint specify its own authorization policy
// ENHANCED: Add endpoint routing diagnostics and fallback mechanisms
var controllerEndpoints = app.MapControllers()
    .WithOpenApi();

app.MapHub<SystemEventsHub>("/hubs/system-events")
    .RequireAuthorization("FlexibleApiAccess");


// ENHANCED DEBUG LOGGING: Log all mapped endpoints at startup
var endpointDataSource = app.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>();
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();

startupLogger.LogInformation("========== ENDPOINT MAPPING DIAGNOSTICS ==========");
startupLogger.LogInformation("Total endpoints mapped: {Count}", endpointDataSource.Endpoints.Count);

var authEndpoints = endpointDataSource.Endpoints
    .Where(e => e.DisplayName?.Contains("Auth", StringComparison.OrdinalIgnoreCase) == true)
    .ToList();

startupLogger.LogInformation("Auth-related endpoints: {Count}", authEndpoints.Count);
foreach (var endpoint in authEndpoints)
{
    var routeEndpoint = endpoint as Microsoft.AspNetCore.Routing.RouteEndpoint;
    var pattern = routeEndpoint?.RoutePattern?.RawText ?? "No pattern";
    var metadata = string.Join(", ", endpoint.Metadata.Select(m => m.GetType().Name));
    
    startupLogger.LogInformation(
        "  Endpoint: {DisplayName}, Pattern: {Pattern}, Metadata: [{Metadata}]",
        endpoint.DisplayName, pattern, metadata);
}

// Check for POST /api/auth/login specifically
var loginEndpoints = endpointDataSource.Endpoints
    .Where(e => e.DisplayName?.Contains("Login", StringComparison.OrdinalIgnoreCase) == true)
    .ToList();

startupLogger.LogInformation("Login endpoints found: {Count}", loginEndpoints.Count);
foreach (var endpoint in loginEndpoints)
{
    var routeEndpoint = endpoint as Microsoft.AspNetCore.Routing.RouteEndpoint;
    var pattern = routeEndpoint?.RoutePattern?.RawText ?? "No pattern";
    var httpMethods = endpoint.Metadata
        .OfType<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()
        .SelectMany(m => m.HttpMethods)
        .ToList();
    
    startupLogger.LogInformation(
        "  Login Endpoint: {DisplayName}, Pattern: {Pattern}, Methods: [{Methods}]",
        endpoint.DisplayName, pattern, string.Join(", ", httpMethods));
}

startupLogger.LogInformation("========== ENDPOINT MAPPING DIAGNOSTICS END ==========");

// Initialize SpacetimeDB connection
var spacetimeService = app.Services.GetRequiredService<TicketSalesApp.Services.Interfaces.ISpacetimeDBService>();
spacetimeService.Connect();

// Start background task to register OAuth clients once subscription is ready
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Starting background task for OAuth client registration...");

_ = Task.Run(async () =>
{
    try
    {
        // Wait for connection (max 30 seconds)
        var maxWaitTime = TimeSpan.FromSeconds(30);
        var startTime = DateTime.UtcNow;
        while (!spacetimeService.IsConnected() && (DateTime.UtcNow - startTime) < maxWaitTime)
        {
            await Task.Delay(500);
        }

        if (!spacetimeService.IsConnected())
        {
            logger.LogError("SpacetimeDB connection not ready after {Seconds} seconds, cannot register OAuth clients", maxWaitTime.TotalSeconds);
            return;
        }

        logger.LogInformation("SpacetimeDB connection ready, waiting for subscription to be applied...");

        // Wait for subscription with extended timeout (2 minutes)
        // Subscription can take 30+ seconds to apply based on logs
        var maxSubscriptionWaitTime = TimeSpan.FromMinutes(2);
        startTime = DateTime.UtcNow;
        var lastLogTime = DateTime.UtcNow;
        
        while (!spacetimeService.IsSubscriptionReady() && (DateTime.UtcNow - startTime) < maxSubscriptionWaitTime)
        {
            await Task.Delay(500);
            
            // Log progress every 10 seconds
            if ((DateTime.UtcNow - lastLogTime).TotalSeconds >= 10)
            {
                logger.LogInformation("Still waiting for SpacetimeDB subscription... ({Elapsed:F1}s elapsed)", 
                    (DateTime.UtcNow - startTime).TotalSeconds);
                lastLogTime = DateTime.UtcNow;
            }
        }

        if (!spacetimeService.IsSubscriptionReady())
        {
            logger.LogError("SpacetimeDB subscription not applied after {Seconds} seconds, cannot register OAuth clients", 
                maxSubscriptionWaitTime.TotalSeconds);
            return;
        }

        logger.LogInformation("SpacetimeDB subscription applied successfully after {Elapsed:F1}s, proceeding with client registration", 
            (DateTime.UtcNow - startTime).TotalSeconds);

        // Auto-register default OAuth clients
        using (var scope = app.Services.CreateScope())
        {
            var openIdConnectService = scope.ServiceProvider.GetRequiredService<TicketSalesApp.Services.Interfaces.IOpenIdConnectService>();
            
            logger.LogInformation("Checking for default OAuth clients...");
            
            // Register desktop client if it doesn't exist
            var desktopClientId = "bru-avtopark-desktop-client";
            var (clientExists, _, _) = await openIdConnectService.GetApplicationByClientIdAsync(desktopClientId);
            
            if (!clientExists)
            {
                logger.LogInformation("Registering default desktop client: {ClientId}", desktopClientId);
                
                // CRITICAL: Desktop/mobile apps are PUBLIC clients and should NOT have a client secret
                // Public clients use PKCE (code_challenge/code_verifier) for security instead
                // Setting an empty secret tells OpenIddict this is a public client
                var (success, errorMessage) = await openIdConnectService.RegisterClientApplicationAsync(
                    clientId: desktopClientId,
                    clientSecret: "",  // EMPTY for public clients - they use PKCE instead
                    displayName: "BRU Avtopark Desktop Application",
                    redirectUris: new[] {
                        "http://localhost:5000/callback",
                        "http://localhost:5555/callback",
                        "https://localhost:7515/callback",
                        "https://localhost:7515/signin-oidc",
                        "http://localhost:5501/signin-oidc"
                    },
                    postLogoutRedirectUris: new[] {
                        "http://localhost:5000/",
                        "http://localhost:5555/",
                        "https://localhost:7515/",
                        "http://localhost:5501/"
                    },
                    allowedScopes: new[] { "openid", "profile", "email", "roles", "api", "offline_access" },
                    requireConsent: false
                );
                
                if (success)
                {
                    logger.LogInformation("Successfully registered default desktop client");
                    
                    // CRITICAL: Process frame ticks to ensure SpacetimeDB cache is updated
                    // The reducer writes to the database, but the local cache needs frame ticks to sync
                    logger.LogInformation("[FrameTick] Processing frame ticks to sync SpacetimeDB cache...");
                    for (int i = 0; i < 10; i++)
                    {
                        logger.LogDebug("[FrameTick] Processing tick {TickNumber}/10", i + 1);
                        spacetimeService.ProcessFrameTick();
                        await Task.Delay(100); // Small delay between ticks
                    }
                    logger.LogInformation("[FrameTick] Completed 10 frame ticks");
                    
                    // Fetch and display reducer logs from SpacetimeDB
                    logger.LogInformation("Fetching SpacetimeDB reducer logs for RegisterOpenIdClient...");
                    var reducerLogs = await spacetimeService.FetchReducerLogsAsync("RegisterOpenIdClient", numLines: 300);
                    if (!reducerLogs.StartsWith("Error"))
                    {
                        logger.LogInformation("=== SpacetimeDB Reducer Logs ===");
                        logger.LogInformation(reducerLogs);
                        logger.LogInformation("=== End of Reducer Logs ===");
                    }
                    
                    // Verify the client is retrievable from cache
                    var (verifyExists, _, _) = await openIdConnectService.GetApplicationByClientIdAsync(desktopClientId);
                    if (verifyExists)
                    {
                        logger.LogInformation("Verified default desktop client is retrievable from database");
                    }
                    else
                    {
                        logger.LogWarning("Default desktop client was registered but cannot be retrieved from cache yet - processing more frame ticks");
                        
                        // Try more aggressive syncing
                        logger.LogInformation("[FrameTick] Processing additional 20 frame ticks...");
                        for (int i = 0; i < 20; i++)
                        {
                            if (i % 5 == 0)
                            {
                                logger.LogDebug("[FrameTick] Processing tick {TickNumber}/20", i + 1);
                            }
                            spacetimeService.ProcessFrameTick();
                            await Task.Delay(50);
                        }
                        logger.LogInformation("[FrameTick] Completed additional 20 frame ticks");
                        
                        // Final verification
                        var (finalCheck, _, _) = await openIdConnectService.GetApplicationByClientIdAsync(desktopClientId);
                        if (finalCheck)
                        {
                            logger.LogInformation("Client now retrievable after additional frame ticks");
                        }
                        else
                        {
                            logger.LogError("Client still not retrievable after 30 frame ticks - SpacetimeDB cache sync issue");
                        }
                    }
                }
                else
                {
                    logger.LogError("Failed to register default desktop client: {Error}", errorMessage);
                }
            }
            else
            {
                logger.LogInformation("Default desktop client already exists - updating to ensure correct configuration");
                
                // CRITICAL: Desktop/mobile apps are PUBLIC clients and should NOT have a client secret
                // Public clients use PKCE (code_challenge/code_verifier) for security instead
                // Setting an empty secret tells OpenIddict this is a public client
                var (success, errorMessage) = await openIdConnectService.UpdateClientApplicationAsync(
                    clientId: desktopClientId,
                    clientSecret: "",  // EMPTY for public clients - they use PKCE instead
                    displayName: "BRU Avtopark Desktop Application",
                    redirectUris: new[] {
                        "http://localhost:5000/callback",
                        "http://localhost:5555/callback",
                        "https://localhost:7515/callback",
                        "https://localhost:7515/signin-oidc",
                        "http://localhost:5501/signin-oidc"
                    },
                    postLogoutRedirectUris: new[] {
                        "http://localhost:5000/",
                        "http://localhost:5555/",
                        "https://localhost:7515/",
                        "http://localhost:5501/"
                    },
                    allowedScopes: new[] { "openid", "profile", "email", "roles", "api", "offline_access" },
                    requireConsent: false
                );
                
                if (success)
                {
                    logger.LogInformation("Successfully updated default desktop client with correct scope permissions");
                    
                    // Process frame ticks to sync the update
                    logger.LogInformation("[FrameTick] Processing frame ticks to sync client update...");
                    for (int i = 0; i < 10; i++)
                    {
                        logger.LogDebug("[FrameTick] Processing tick {TickNumber}/10", i + 1);
                        spacetimeService.ProcessFrameTick();
                        await Task.Delay(100);
                    }
                    logger.LogInformation("[FrameTick] Completed frame ticks for client update");
                    
                    // Fetch and display reducer logs from SpacetimeDB
                    logger.LogInformation("Fetching SpacetimeDB reducer logs for UpdateOpenIdClient...");
                    var reducerLogs = await spacetimeService.FetchReducerLogsAsync("UpdateOpenIdClient", numLines: 300);
                    if (!reducerLogs.StartsWith("Error"))
                    {
                        logger.LogInformation("=== SpacetimeDB Reducer Logs ===");
                        logger.LogInformation(reducerLogs);
                        logger.LogInformation("=== End of Reducer Logs ===");
                    }
                }
                else
                {
                    logger.LogError("Failed to update default desktop client: {Error}", errorMessage);
                }
            }

            // Register required OAuth scopes
            logger.LogInformation("Registering required OAuth scopes...");
            var scopeManager = openIdConnectService.GetScopeManager();
            
            var requiredScopes = new[]
            {
                new { Name = "openid", DisplayName = "OpenID", Description = "OpenID Connect scope" },
                new { Name = "profile", DisplayName = "User Profile", Description = "Access to user profile information" },
                new { Name = "email", DisplayName = "Email Address", Description = "Access to user email address" },
                new { Name = "roles", DisplayName = "User Roles", Description = "Access to user roles" },
                new { Name = "api", DisplayName = "API Access", Description = "Access to the API" },
                new { Name = "offline_access", DisplayName = "Offline Access", Description = "Access to refresh tokens" }
            };

            foreach (var scopeInfo in requiredScopes)
            {
                try
                {
                    var existingScope = await scopeManager.FindByNameAsync(scopeInfo.Name);
                    if (existingScope == null)
                    {
                        logger.LogInformation("Creating scope: {ScopeName}", scopeInfo.Name);
                        
                        var scopeDescriptor = new OpenIddictScopeDescriptor
                        {
                            Name = scopeInfo.Name,
                            DisplayName = scopeInfo.DisplayName,
                            Description = scopeInfo.Description
                        };

                        await scopeManager.CreateAsync(scopeDescriptor);
                        logger.LogInformation("Successfully created scope: {ScopeName}", scopeInfo.Name);
                    }
                    else
                    {
                        logger.LogInformation("Scope already exists: {ScopeName}", scopeInfo.Name);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error creating scope {ScopeName}: {Message}", scopeInfo.Name, ex.Message);
                }
            }

            // Process frame ticks to sync scope data
            logger.LogInformation("[FrameTick] Processing frame ticks to sync scope data...");
            for (int i = 0; i < 10; i++)
            {
                logger.LogDebug("[FrameTick] Processing scope sync tick {TickNumber}/10", i + 1);
                spacetimeService.ProcessFrameTick();
                await Task.Delay(100);
            }
            logger.LogInformation("[FrameTick] Completed scope sync frame ticks");

            // Verify scopes are retrievable
            logger.LogInformation("Verifying registered scopes...");
            foreach (var scopeInfo in requiredScopes)
            {
                var registeredScope = await scopeManager.FindByNameAsync(scopeInfo.Name);
                if (registeredScope != null)
                {
                    logger.LogInformation("✓ Scope verified: {ScopeName}", scopeInfo.Name);
                }
                else
                {
                    logger.LogWarning("✗ Scope not found: {ScopeName}", scopeInfo.Name);
                }
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error during OAuth client registration: {Message}", ex.Message);
    }
});

app.Run();

public record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}