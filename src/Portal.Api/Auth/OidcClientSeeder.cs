using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Portal.Api.Auth;

/// <summary>Registers the first-party web client with the OAuth 2.0 server (public client, PKCE required).</summary>
public class OidcClientSeeder(IServiceProvider services, IConfiguration config) : IHostedService
{
    public const string WebClientId = "portal-web";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        if (await manager.FindByClientIdAsync(WebClientId, cancellationToken) is not null) return;

        var baseUrl = (config["Portal:PublicBaseUrl"] ?? throw new InvalidOperationException("Portal__PublicBaseUrl is not configured.")).TrimEnd('/');

        await manager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = WebClientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "Portal web app",
            RedirectUris = { new Uri($"{baseUrl}/auth/callback") },
            PostLogoutRedirectUris = { new Uri($"{baseUrl}/") },
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Prefixes.Scope + Scopes.OfflineAccess,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        }, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
