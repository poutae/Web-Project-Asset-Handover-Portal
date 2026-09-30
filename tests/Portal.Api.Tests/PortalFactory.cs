using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Portal.Infrastructure.Persistence;

namespace Portal.Api.Tests;

/// <summary>
/// Boots the API against a throw-away SQL Server database (migrated, dropped on dispose).
/// Set PORTAL_TEST_SQL to a connection string WITHOUT a database name; defaults to local Windows-auth SQL Server.
/// </summary>
public class PortalFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public PortalFactory()
    {
        var server = Environment.GetEnvironmentVariable("PORTAL_TEST_SQL")
            ?? "Server=localhost;Trusted_Connection=True;TrustServerCertificate=True";
        _connectionString = $"{server.TrimEnd(';')};Database=PortalTests_{Guid.NewGuid():N}";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("Portal:PublicBaseUrl", "http://localhost:5173");
        builder.UseSetting("Portal:Auth:RateLimitPerMinute", "10000");

        // Migrate before the host starts (the OAuth client seeder needs the schema).
        using var db = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlServer(_connectionString).UseOpenIddict().Options);
        db.Database.Migrate();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        using var db = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlServer(_connectionString).UseOpenIddict().Options);
        db.Database.EnsureDeleted();
    }
}
