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

    /// <summary>The file store root of this run: isolated from the host's data directory and removed afterwards (TST-01/02).</summary>
    public string FileStoreRoot { get; } = Path.Combine(Path.GetTempPath(), "opencampus-tests", "files-" + Guid.NewGuid().ToString("N"));

    /// <summary>Where the local external adapters (8.3) persist their evidence for this run; removed afterwards.</summary>
    public string NotificationRoot => Path.Combine(FileStoreRoot, "notifications");

    /// <summary>Upload bound of the test host (SEC-22); small so limit tests stay cheap.</summary>
    public const long MaxUploadSizeBytes = 64 * 1024;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.UseSetting("ConnectionStrings:OpenCampus", ConnectionString);
        builder.UseSetting("Storage:RootPath", FileStoreRoot);
        builder.UseSetting("Storage:MaxUploadSizeBytes", MaxUploadSizeBytes.ToString());
        builder.UseSetting("Notification:OutputPath", NotificationRoot);

        // Test-only tuning: the shared host must not rate-limit the suite (a dedicated host covers SEC-16),
        // and the PBKDF2 floor keeps credential-heavy tests fast while remaining a valid configuration.
        builder.UseSetting("AccountProtection:RateLimitPermittedRequests", "1000");
        builder.UseSetting("PasswordHashing:Iterations", "100000");
    }

    /// <summary>A client on an HTTPS origin (so Secure cookies are honoured) that leaves cookie handling to the test.</summary>
    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = false,
    });

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await identity.Database.EnsureDeletedAsync();
        await identity.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SisDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<LmsDbContext>().Database.MigrateAsync();

        await OpenCampus.Api.Persistence.DatabaseInitializer.ProvisionAsync(Services, Server.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.EnsureDeletedAsync();
        if (Directory.Exists(FileStoreRoot))
        {
            Directory.Delete(FileStoreRoot, recursive: true);
        }

        await base.DisposeAsync();
    }
}
