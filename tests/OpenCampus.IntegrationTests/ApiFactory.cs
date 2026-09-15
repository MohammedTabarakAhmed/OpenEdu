using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCampus.Identity.Infrastructure.Persistence;
using OpenCampus.Lms.Infrastructure.Persistence;
using OpenCampus.Sis.Infrastructure.Persistence;

namespace OpenCampus.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=OpenCampus.Test;Integrated Security=true;TrustServerCertificate=true";

    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("OPENCAMPUS_TEST_CONNECTION") ?? DefaultConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.UseSetting("ConnectionStrings:OpenCampus", ConnectionString);
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await identity.Database.EnsureDeletedAsync();
        await identity.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SisDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<LmsDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }
}
