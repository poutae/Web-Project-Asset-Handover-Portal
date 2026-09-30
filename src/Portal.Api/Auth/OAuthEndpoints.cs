using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Portal.Infrastructure.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Portal.Api.Auth;

/// <summary>OAuth 2.0 / OpenID Connect authorization-code (+PKCE) and refresh-token endpoints.</summary>
public static class OAuthEndpoints
{
    public static void MapOAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post], AuthorizeAsync).ExcludeFromDescription();
        app.MapPost("/connect/token", TokenAsync).ExcludeFromDescription();
    }

    private static async Task<IResult> AuthorizeAsync(HttpContext http, PortalDbContext db, CancellationToken ct)
    {
        var request = http.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        var session = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!session.Succeeded)
        {
            // Not signed in: send the user to the login page, then back here.
            var props = new AuthenticationProperties
            {
                RedirectUri = http.Request.PathBase + http.Request.Path + QueryString.Create(
                    http.Request.HasFormContentType ? http.Request.Form.ToList() : http.Request.Query.ToList()),
            };
            return Results.Challenge(props, [CookieAuthenticationDefaults.AuthenticationScheme]);
        }

        var userId = AuthEndpoints.UserId(session.Principal!);
        var user = userId is null ? null : await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Results.Forbid(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

        var identity = new ClaimsIdentity(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, user.Id.ToString())
                .SetClaim(Claims.Email, user.Email)
                .SetClaim(Claims.Name, user.DisplayName);

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(request.GetScopes());
        principal.SetDestinations(_ => [Destinations.AccessToken, Destinations.IdentityToken]);

        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> TokenAsync(HttpContext http, PortalDbContext db, CancellationToken ct)
    {
        var request = http.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
            return Results.Forbid(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

        var result = await http.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var principal = result.Principal!;

        // Reject tokens of users that no longer exist.
        var subject = principal.GetClaim(Claims.Subject);
        if (!Guid.TryParse(subject, out var id) || !await db.Users.AnyAsync(u => u.Id == id, ct))
            return Results.Forbid(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

        principal.SetDestinations(_ => [Destinations.AccessToken, Destinations.IdentityToken]);
        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
}
