using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenCampus.Identity.Application.Provisioning;
using OpenCampus.Identity.Infrastructure.Persistence;
using OpenCampus.Lms.Infrastructure.Persistence;
using OpenCampus.Sis.Infrastructure.Persistence;

namespace OpenCampus.Api.Persistence;

public static class DatabaseInitializer
{
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<SisDbContext>().Database.MigrateAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<LmsDbContext>().Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// SDD 18.7: provisions reference data (and, where enabled, demonstration data). Credentials
    /// generated on first run are written to the configured data directory, never to the log (SEC-02).
    /// </summary>
    public static async Task ProvisionAsync(IServiceProvider services, string contentRootPath, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        var generated = await scope.ServiceProvider.GetRequiredService<IdentityProvisioner>().RunAsync(cancellationToken);
        if (generated.Count == 0)
        {
            return;
        }

        var options = scope.ServiceProvider.GetRequiredService<IOptions<ProvisioningOptions>>().Value;
        var path = Path.Combine(contentRootPath, options.CredentialsFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var lines = generated.Select(c => $"{c.Purpose}: user '{c.UserName}' password '{c.Password}'");
        await File.AppendAllLinesAsync(path, [$"# Generated {DateTime.UtcNow:O}", .. lines, string.Empty], cancellationToken);

        scope.ServiceProvider.GetRequiredService<ILogger<IdentityProvisioner>>()
            .LogWarning("Initial credentials were generated and written to {Path}. Change them after first login.", path);
    }
}
