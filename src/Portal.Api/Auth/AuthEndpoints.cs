using System.Security.Claims;
using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Portal.Domain.Entities;
using Portal.Infrastructure.Persistence;

namespace Portal.Api.Auth;

public static partial class AuthEndpoints
{
    private const string RateLimitPolicy = "auth";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/signup", SignupAsync).RequireRateLimiting(RateLimitPolicy);
        group.MapPost("/login", LoginAsync).RequireRateLimiting(RateLimitPolicy);
        group.MapPost("/logout", LogoutAsync).RequireAuthorization();
        group.MapGet("/me", MeAsync).RequireAuthorization();
    }

    private static async Task<IResult> SignupAsync(
        SignupRequest request, IValidator<SignupRequest> validator, PortalDbContext db,
        IPasswordHasher<User> hasher, HttpContext http, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

        var normalized = User.Normalize(request.Email);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct))
            return EmailTaken();

        var user = new User
        {
            Email = request.Email.Trim(),
            NormalizedEmail = normalized,
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = "",
        };
        user.PasswordHash = hasher.HashPassword(user, request.Password);

        var org = new Organization
        {
            Name = request.OrganizationName.Trim(),
            Slug = MakeSlug(request.OrganizationName),
        };
        org.Members.Add(new OrganizationMember { UserId = user.Id, Role = OrganizationRole.Owner });

        db.Users.Add(user);
        db.Organizations.Add(org);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return EmailTaken();
        }

        await SignInAsync(http, user);
        return Results.Created("/api/auth/me", await BuildCurrentUserAsync(db, user.Id, ct));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, IValidator<LoginRequest> validator, PortalDbContext db,
        IPasswordHasher<User> hasher, HttpContext http, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

        var normalized = User.Normalize(request.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);

        // Always run a hash verification so response time does not reveal whether the email exists.
        var hash = user?.PasswordHash ?? DummyHash.Value;
        var result = hasher.VerifyHashedPassword(user ?? DummyHash.User, hash, request.Password);

        if (user is null || result == PasswordVerificationResult.Failed)
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password.");

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, request.Password);
            await db.SaveChangesAsync(ct);
        }

        await SignInAsync(http, user);
        return Results.Ok(await BuildCurrentUserAsync(db, user.Id, ct));
    }

    private static async Task<IResult> LogoutAsync(HttpContext http, CancellationToken ct)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(ClaimsPrincipal principal, PortalDbContext db, CancellationToken ct)
    {
        var id = UserId(principal);
        var dto = id is null ? null : await BuildCurrentUserAsync(db, id.Value, ct);
        return dto is null ? Results.Unauthorized() : Results.Ok(dto);
    }

    /// <summary>The authenticated user's id from either the session cookie or an OAuth access token.</summary>
    public static Guid? UserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private static Task SignInAsync(HttpContext http, User user)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.DisplayName),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);
        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }

    private static async Task<CurrentUserDto?> BuildCurrentUserAsync(PortalDbContext db, Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.DisplayName,
                Memberships = u.Memberships.Select(m => new { m.OrganizationId, m.Organization!.Name, m.Role }).ToList(),
            })
            .FirstOrDefaultAsync(ct);

        return user is null
            ? null
            : new CurrentUserDto(user.Id, user.Email, user.DisplayName,
                user.Memberships.Select(m => new MembershipDto(m.OrganizationId, m.Name, m.Role.ToString())).ToList());
    }

    private static IResult EmailTaken() =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "An account with this email already exists.");

    private static string MakeSlug(string name)
    {
        var slug = NonSlugChars().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > 80) slug = slug[..80].Trim('-');
        return $"{(slug.Length == 0 ? "org" : slug)}-{Guid.CreateVersion7().ToString("N")[^8..]}";
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugChars();

    private static class DummyHash
    {
        public static readonly User User = new() { Email = "", NormalizedEmail = "", DisplayName = "", PasswordHash = "" };
        public static readonly string Value = new PasswordHasher<User>().HashPassword(User, Guid.NewGuid().ToString());
    }
}
