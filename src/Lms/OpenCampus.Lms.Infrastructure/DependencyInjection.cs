using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenCampus.Lms.Application.Abstractions;
using OpenCampus.Lms.Application.External;
using OpenCampus.Lms.Application.Provisioning;
using OpenCampus.Lms.Application.Storage;
using OpenCampus.Lms.Infrastructure.External;
using OpenCampus.Lms.Infrastructure.Persistence;
using OpenCampus.Lms.Infrastructure.Persistence.Repositories;
using OpenCampus.Lms.Infrastructure.Storage;

namespace OpenCampus.Lms.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddLmsInfrastructure(this IServiceCollection services, IConfiguration configuration, string connectionString, string contentRootPath)
    {
        services.AddDbContext<LmsDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", LmsDbContext.Schema)));

        // Appendix B "Storage": root path, maximum upload size and permitted extensions are externalised (SEC-22).
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .Validate(o => o.Validate(), "Storage configuration is invalid: root path, a positive maximum upload size and at least one permitted extension of the form '.ext' are required.")
            .ValidateOnStart();

        services.AddOptions<LmsProvisioningOptions>()
            .Bind(configuration.GetSection(LmsProvisioningOptions.SectionName));

        services.AddScoped<ICourseContentRepository, CourseContentRepository>();
        services.AddScoped<IAssignmentRepository, AssignmentRepository>();
        services.AddScoped<IAttendanceRepository, AttendanceRepository>();
        services.AddScoped<ILmsUnitOfWork, LmsUnitOfWork>();
        // 18.5: the physical mechanism is selected here, at the composition root's request, behind the application-layer abstraction.
        services.AddSingleton<IFileStore>(sp => new LocalFileStore(sp.GetRequiredService<IOptions<StorageOptions>>(), contentRootPath));

        // External contracts (8.2, 18.6): EXT-01 and EXT-03 local adapters, selected from Appendix B "Notification".
        services.AddOptions<NotificationOptions>()
            .Bind(configuration.GetSection(NotificationOptions.SectionName))
            .Validate(o => o.Validate(), "Notification configuration is invalid: Implementation must be 'Local' (the only available adapter set), with an output path and a sender identity.")
            .ValidateOnStart();
        services.AddSingleton<IEmailDispatcher>(sp => new LocalEmailDispatcher(sp.GetRequiredService<IOptions<NotificationOptions>>(), contentRootPath, sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IOriginalityChecker, LocalOriginalityChecker>();

        return services;
    }
}
