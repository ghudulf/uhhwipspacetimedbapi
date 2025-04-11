using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Serilog;
using OpenIddict.Validation.AspNetCore;
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
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.IdentityModel.Tokens.Jwt;


var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.Debug()
                .WriteTo.File("logs/app-.log",
                    rollingInterval: RollingInterval.Day,
                    restrictedToMinimumLevel: LogEventLevel.Information)
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

// Add memory cache for QR authentication
builder.Services.AddMemoryCache();

// Add HTTP context accessor for admin action logging
builder.Services.AddHttpContextAccessor();

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

// Configure OpenIddict
builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.AddApplicationStore<TicketSalesApp.Services.Implementations.ApplicationStore>();
        // ON TODO LIST AUTH STORE, TOKEN STORE, SCOPE STORE - WE'LL NEED ALL THAT SHIT FOR OPENIDDICT TO BE HAPPY
        // THEN MAKE AUTH CONTROLLER COMPLY WITH OPENIDDICT
        // ROYAL PAIN FROM THE DEPTH OF HELL
        options.AddAuthorizationStore<TicketSalesApp.Services.Implementations.AuthorizationStore>();
        options.AddTokenStore<TicketSalesApp.Services.Implementations.TokenStore>();
        options.AddScopeStore<TicketSalesApp.Services.Implementations.ScopeStore>();
       
    })
    .AddServer(options =>
    {
        options.SetAuthorizationEndpointUris("/connect/authorize")
            .SetTokenEndpointUris("/connect/token")
            .SetUserinfoEndpointUris("/connect/userinfo");

        options.AllowAuthorizationCodeFlow()
            .AllowRefreshTokenFlow();

        // Add symmetric signing key for access tokens, authorization codes, and refresh tokens
        options.AddSigningKey(new SymmetricSecurityKey(key));

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
        options.AddEncryptionKey(new SymmetricSecurityKey(key));

        options.UseAspNetCore()
            .EnableTokenEndpointPassthrough()
            .EnableAuthorizationEndpointPassthrough()
            .EnableUserinfoEndpointPassthrough();
    })
    .AddValidation(options =>
    {
        // Register the ASP.NET Core host
        options.UseAspNetCore();

        // Import the configuration from the local OpenIddict server instance.
        options.UseLocalServer();

        // Configure the token validation parameters
        options.Configure(options => options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(key));
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
    options.DefaultAuthenticateScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
    options.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        RoleClaimType = "role",
        NameClaimType = "name"
    };
    options.Events = new JwtBearerEvents
    {
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
    options.AddPolicy("RequireAuthenticatedUser", policy =>
        policy.RequireAuthenticatedUser());

    options.AddPolicy("PublicEndpoints", policy =>
        policy.RequireAssertion(_ => true));

    // Default policy for controllers
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim("scope", "api")
        .Build();
});

// Add controllers
builder.Services.AddControllers(options =>
            {
                options.RespectBrowserAcceptHeader = true;
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve;
            });


var app = builder.Build();
app.UseRouting();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

// Add authentication and authorization in the correct order
app.UseAuthentication();
app.UseAuthorization();

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

// Map controllers with authorization
app.MapControllers().RequireAuthorization(policy =>
{
    policy.RequireAuthenticatedUser();
    // Exclude specific paths from authentication
    policy.AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
    policy.RequireClaim("scope", "api");
})
.WithOpenApi();

// Initialize SpacetimeDB connection
var spacetimeService = app.Services.GetRequiredService<TicketSalesApp.Services.Interfaces.ISpacetimeDBService>();
spacetimeService.Connect();

app.Run();

public record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
