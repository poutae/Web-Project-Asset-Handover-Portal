using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Validation.AspNetCore;
using Portal.Api.Auth;
using Portal.Api.Infrastructure;
using Portal.Domain.Entities;
using Portal.Infrastructure.Persistence;

if (string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase)
    || Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") is null)
{
    DotEnv.Load(Directory.GetCurrentDirectory());
}

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();
var config = builder.Configuration;
var env = builder.Environment;

builder.Services.AddOpenApi();
builder.Services.AddValidatorsFromAssemblyContaining<SignupRequestValidator>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

builder.Services.AddDbContext<PortalDbContext>(options =>
{
    options.UseSqlServer(config.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings__Default is not configured."),
        sql => sql.MigrationsAssembly(typeof(PortalDbContext).Assembly.FullName));
    options.UseOpenIddict();
});

// --- Authentication: session cookie for the web app, OAuth 2.0 bearer tokens for API clients ---
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "portal.session";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
        o.LoginPath = "/login";
        o.ReturnUrlParameter = "returnUrl";
        // API calls get 401/403 instead of a redirect to the login page.
        o.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            else ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization(o =>
{
    o.DefaultPolicy = new AuthorizationPolicyBuilder(
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddOpenIddict()
    .AddCore(o => o.UseEntityFrameworkCore().UseDbContext<PortalDbContext>())
    .AddServer(o =>
    {
        o.SetAuthorizationEndpointUris("/connect/authorize").SetTokenEndpointUris("/connect/token");
        o.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow();
        o.RequireProofKeyForCodeExchange();
        o.RegisterScopes(OpenIddict.Abstractions.OpenIddictConstants.Scopes.Email,
            OpenIddict.Abstractions.OpenIddictConstants.Scopes.Profile,
            OpenIddict.Abstractions.OpenIddictConstants.Scopes.OfflineAccess);

        var signingPath = config["Oidc:SigningCertificatePath"];
        var encryptionPath = config["Oidc:EncryptionCertificatePath"];
        if (signingPath is not null && encryptionPath is not null)
        {
            o.AddSigningCertificate(X509CertificateLoader.LoadPkcs12FromFile(signingPath, config["Oidc:SigningCertificatePassword"]));
            o.AddEncryptionCertificate(X509CertificateLoader.LoadPkcs12FromFile(encryptionPath, config["Oidc:EncryptionCertificatePassword"]));
        }
        else if (env.IsDevelopment() || env.IsEnvironment("Testing"))
        {
            // Keys live in memory only: tokens stop validating after a restart. Fine for development.
            o.AddEphemeralSigningKey().AddEphemeralEncryptionKey();
        }
        else
        {
            throw new InvalidOperationException(
                "Oidc__SigningCertificatePath and Oidc__EncryptionCertificatePath must be configured outside Development.");
        }

        o.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableTokenEndpointPassthrough()
            .DisableTransportSecurityRequirement(); // TLS is terminated by the reverse proxy; enforced by the cookie policy and HSTS.
    })
    .AddValidation(o =>
    {
        o.UseLocalServer();
        o.UseAspNetCore();
    });
builder.Services.AddHostedService<OidcClientSeeder>();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var permits = config.GetValue("Portal:Auth:RateLimitPerMinute", 20);
    o.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

if (env.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");
api.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapOAuthEndpoints();

app.Run();

public partial class Program;
