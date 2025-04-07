using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Serilog;
using OpenIddict.Validation.AspNetCore;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Abstractions;
using OpenIddict.Validation.ServerIntegration;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();


// Configure Swagger

builder.Services.AddSwaggerGen(c =>
            {
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

// Add other services
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IAuthenticationService, TicketSalesApp.Services.Implementations.AuthenticationService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IRoleService, TicketSalesApp.Services.Implementations.RoleService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IUserService, TicketSalesApp.Services.Implementations.UserService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.ITicketSalesService, TicketSalesApp.Services.Implementations.TicketSalesService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IExportService, TicketSalesApp.Services.Implementations.ExportService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IQRAuthenticationService, TicketSalesApp.Services.Implementations.QRAuthenticationService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IAdminActionLogger, TicketSalesApp.Services.Implementations.AdminActionLogger>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IDataService, TicketSalesApp.Services.Implementations.DataService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IBusService, TicketSalesApp.Services.Implementations.BusService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IPermissionService, TicketSalesApp.Services.Implementations.PermissionService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.ITicketService, TicketSalesApp.Services.Implementations.TicketService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IRouteService, TicketSalesApp.Services.Implementations.RouteService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IRouteScheduleService, TicketSalesApp.Services.Implementations.RouteScheduleService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IEmployeeService, TicketSalesApp.Services.Implementations.EmployeeService>();
builder.Services.AddSingleton<TicketSalesApp.Services.Interfaces.IMaintenanceService, TicketSalesApp.Services.Implementations.MaintenanceService>();
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

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
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

// Add controllers
builder.Services.AddControllers();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "BRU-AVTOPARK API V1");
        c.RoutePrefix = "swagger";
    });
}

// Initialize SpacetimeDB connection
var spacetimeService = app.Services.GetRequiredService<TicketSalesApp.Services.Interfaces.ISpacetimeDBService>();
spacetimeService.Connect();

// Use authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

// Map controllers
app.MapControllers();

app.MapDefaultEndpoints();

app.Run();

public record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
