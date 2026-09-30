using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Portal.Api.Auth;

namespace Portal.Api.Tests;

public class AuthTests : IClassFixture<PortalFactory>
{
    private readonly PortalFactory _factory;

    public AuthTests(PortalFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = true,
        AllowAutoRedirect = false,
        // The session cookie is Secure outside Development, so talk to the API over https like a real client.
        BaseAddress = new Uri("https://localhost"),
    });

    private static SignupRequest NewSignup(string? email = null) =>
        new(email ?? $"{Guid.NewGuid():N}@example.com", "correct horse battery", "Test User", "Acme Agency");

    [Fact]
    public async Task Signup_creates_account_organization_and_session()
    {
        var client = NewClient();
        var response = await client.PostAsJsonAsync("/api/auth/signup", NewSignup());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var me = await client.GetFromJsonAsync<CurrentUserDto>("/api/auth/me");
        Assert.NotNull(me);
        var membership = Assert.Single(me.Memberships);
        Assert.Equal("Owner", membership.Role);
        Assert.Equal("Acme Agency", membership.OrganizationName);
    }

    [Fact]
    public async Task Signup_sets_http_only_cookie()
    {
        var response = await NewClient().PostAsJsonAsync("/api/auth/signup", NewSignup());
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("portal.session="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Signup_with_duplicate_email_returns_conflict_case_insensitively()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        Assert.Equal(HttpStatusCode.Created, (await NewClient().PostAsJsonAsync("/api/auth/signup", NewSignup(email))).StatusCode);

        var duplicate = await NewClient().PostAsJsonAsync("/api/auth/signup", NewSignup(email.ToUpperInvariant()));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Signup_with_weak_password_is_rejected()
    {
        var response = await NewClient().PostAsJsonAsync("/api/auth/signup", NewSignup() with { Password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_correct_password_succeeds()
    {
        var signup = NewSignup();
        await NewClient().PostAsJsonAsync("/api/auth/signup", signup);

        var client = NewClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(signup.Email, signup.Password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_unauthorized_and_no_session()
    {
        var signup = NewSignup();
        await NewClient().PostAsJsonAsync("/api/auth/signup", signup);

        var client = NewClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(signup.Email, "wrong password!!"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Login_with_unknown_email_returns_unauthorized()
    {
        var response = await NewClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("nobody@example.com", "whatever password"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        var client = NewClient();
        await client.PostAsJsonAsync("/api/auth/signup", NewSignup());

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/auth/me")]
    [InlineData("POST", "/api/auth/logout")]
    public async Task Protected_endpoints_require_authentication(string method, string url)
    {
        var response = await NewClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_is_public()
    {
        Assert.Equal(HttpStatusCode.OK, (await NewClient().GetAsync("/api/health")).StatusCode);
    }

    [Fact]
    public async Task Authorize_endpoint_redirects_anonymous_users_to_login()
    {
        var url = "/connect/authorize?client_id=portal-web&response_type=code&redirect_uri=http%3A%2F%2Flocalhost%3A5173%2Fauth%2Fcallback" +
                  "&scope=openid%20profile&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256&state=abc";
        var response = await NewClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = new Uri(response.RequestMessage!.RequestUri!, response.Headers.Location);
        Assert.Equal("/login", location.AbsolutePath);
    }

    [Fact]
    public async Task Authorize_endpoint_rejects_requests_without_pkce()
    {
        var url = "/connect/authorize?client_id=portal-web&response_type=code&redirect_uri=http%3A%2F%2Flocalhost%3A5173%2Fauth%2Fcallback&scope=openid";
        var response = await NewClient().GetAsync(url);
        // The request is refused outright: no login redirect, no authorization code flow.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }
}
