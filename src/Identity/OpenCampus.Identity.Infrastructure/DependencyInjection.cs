using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenCampus.Identity.Application.Abstractions;
using OpenCampus.Identity.Application.External;
using OpenCampus.Identity.Application.Provisioning;
using OpenCampus.Identity.Application.Registration;
using OpenCampus.Identity.Application.Security;
using OpenCampus.Identity.Infrastructure.External;
using OpenCampus.Identity.Infrastructure.Persistence;
using OpenCampus.Identity.Infrastructure.Persistence.Repositories;
using OpenCampus.Identity.Infrastructure.Security;

namespace OpenCampus.Identity.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString,
        string contentRootPath)
    {
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema)));

        // Appendix B: every value below is externalised; invalid values fail the host at startup.
        services.AddOptions<TokenOptions>()
            .Bind(configuration.GetSection(TokenOptions.SectionName))
            .Validate(o => o.Validate(), "Tokens configuration is invalid: issuer, audience and signing key path are required; access token lifetime must not exceed 15 minutes (SEC-03) and refresh lifetime must not exceed 14 days (SEC-05).")
            .ValidateOnStart();

        services.AddOptions<AccountProtectionOptions>()
            .Bind(configuration.GetSection(AccountProtectionOptions.SectionName))
            .Validate(o => o.Validate(), "AccountProtection configuration is invalid: lockout threshold, lockout duration, rate limit window and permitted requests must all be positive.")
            .ValidateOnStart();

        services.AddOptions<PasswordHashingOptions>()
            .Bind(configuration.GetSection(PasswordHashingOptions.SectionName))
            .Validate(o => o.Validate(), $"PasswordHashing:Iterations must be at least {PasswordHashingOptions.MinimumIterations}.")
            .ValidateOnStart();

        services.AddOptions<ProvisioningOptions>()
            .Bind(configuration.GetSection(ProvisioningOptions.SectionName))
            .Validate(o => o.Validate(), "Provisioning configuration is invalid: administrator user name, e-mail and credentials file path are required.")
            .ValidateOnStart();

        // Increment 7: self-registration switch, link origin and token lifetimes; the e-mail adapter is the local EXT-01
        // pattern (CON-04), writing the same email.jsonl as the SIS adapter under the shared "Notification" section.
        services.AddOptions<RegistrationOptions>()
            .Bind(configuration.GetSection(RegistrationOptions.SectionName))
            .Validate(o => o.Validate(), "Registration configuration is invalid: ClientBaseUrl must be an absolute http(s) URL, VerificationLifetime between 5 minutes and 7 days, and ResendCooldown non-negative and shorter than the lifetime.")
            .ValidateOnStart();
        services.AddOptions<NotificationOptions>()
            .Bind(configuration.GetSection(NotificationOptions.SectionName))
            .Validate(o => o.Validate(), "Notification configuration is invalid: Implementation must be 'Local', with an output path and a sender identity.")
            .ValidateOnStart();
        services.AddSingleton<IEmailDispatcher>(sp => new LocalEmailDispatcher(sp.GetRequiredService<IOptions<NotificationOptions>>(), contentRootPath, sp.GetRequiredService<TimeProvider>()));

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IRefreshTokenGenerator, RefreshTokenGenerator>();
        services.AddSingleton<ISigningKeyProvider>(sp =>
        {
            var tokens = sp.GetRequiredService<IOptions<TokenOptions>>().Value;
            return new FileSigningKeyProvider(Path.Combine(contentRootPath, tokens.SigningKeyPath));
        });
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<ITotpService, Rfc6238TotpService>();
        services.AddSingleton<IMfaChallengeIssuer, DataProtectionMfaChallengeIssuer>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserSessionRepository, UserSessionRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IAuditEventRepository, AuditEventRepository>();
        services.AddScoped<IIdentityUnitOfWork, IdentityUnitOfWork>();

        return services;
    }
}
