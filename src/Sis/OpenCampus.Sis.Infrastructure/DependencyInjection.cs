using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenCampus.Sis.Application.Abstractions;
using OpenCampus.Sis.Application.Certificates;
using OpenCampus.Sis.Application.External;
using OpenCampus.Sis.Application.Grading;
using OpenCampus.Sis.Application.Provisioning;
using OpenCampus.Sis.Infrastructure.Caching;
using OpenCampus.Sis.Application.Reports;
using OpenCampus.Sis.Infrastructure.Certificates;
using OpenCampus.Sis.Infrastructure.External;
using OpenCampus.Sis.Infrastructure.Persistence;
using OpenCampus.Sis.Infrastructure.Persistence.Repositories;

namespace OpenCampus.Sis.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSisInfrastructure(this IServiceCollection services, IConfiguration configuration, string connectionString, string contentRootPath)
    {
        services.AddDbContext<SisDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", SisDbContext.Schema)));

        services.AddOptions<SisProvisioningOptions>()
            .Bind(configuration.GetSection(SisProvisioningOptions.SectionName));

        // Appendix B "Academic": pass and attendance thresholds (BR-10, BR-12) are externalised and validated at start-up.
        services.AddOptions<AcademicOptions>()
            .Bind(configuration.GetSection(AcademicOptions.SectionName))
            .Validate(o => o.Validate(), "Academic configuration is invalid: pass and attendance thresholds must be percentages between 0 and 100.")
            .ValidateOnStart();

        // 18.4: programmes are the first cacheable reference-data read path; the decorator caches only the batched
        // read-only lookup, with bounded expiry from configuration and explicit invalidation through IReferenceDataCache.
        services.AddMemoryCache();
        services.AddOptions<ReferenceDataCacheOptions>()
            .Bind(configuration.GetSection(ReferenceDataCacheOptions.SectionName))
            .Validate(o => o.Validate(), "ReferenceDataCache:ProgrammeExpiry must be between 1 second and 24 hours.")
            .ValidateOnStart();
        services.AddScoped<ProgrammeRepository>();
        services.AddScoped<CachedProgrammeRepository>(sp => new CachedProgrammeRepository(
            sp.GetRequiredService<ProgrammeRepository>(), sp.GetRequiredService<IMemoryCache>(), sp.GetRequiredService<IOptions<ReferenceDataCacheOptions>>()));
        services.AddScoped<IProgrammeRepository>(sp => sp.GetRequiredService<CachedProgrammeRepository>());
        services.AddScoped<IReferenceDataCache>(sp => sp.GetRequiredService<CachedProgrammeRepository>());
        services.AddScoped<ICourseRepository, CourseRepository>();
        services.AddScoped<ISectionRepository, SectionRepository>();
        services.AddScoped<ILearnerRepository, LearnerRepository>();
        services.AddScoped<ILearnerNumberGenerator, SequenceLearnerNumberGenerator>();
        services.AddScoped<IEnrolmentRepository, EnrolmentRepository>();
        services.AddScoped<IGradeEntryRepository, GradeEntryRepository>();
        services.AddScoped<ICertificateRepository, CertificateRepository>();
        services.AddScoped<IReportRepository, ReportRepository>();
        services.AddScoped<ISisUnitOfWork, SisUnitOfWork>();

        // Certificates (Increment 6): the same configured storage root as the LMS (Appendix B "Storage"), beneath "certificates/";
        // the PDF library and the code generator are selected here, behind the application-layer abstractions (18.5, 8.1).
        services.AddOptions<CertificateStorageOptions>()
            .Bind(configuration.GetSection(CertificateStorageOptions.SectionName))
            .Validate(o => o.Validate(), "Storage configuration is invalid: a root path is required.")
            .ValidateOnStart();
        services.AddSingleton<ICertificateStore>(sp => new LocalCertificateStore(sp.GetRequiredService<IOptions<CertificateStorageOptions>>(), contentRootPath));
        services.AddSingleton<ICertificateDocumentRenderer, PdfSharpCertificateRenderer>();
        services.AddSingleton<IVerificationCodeGenerator, VerificationCodeGenerator>();

        // External contracts (8.2, 18.6): the implementation set is selected from Appendix B "Notification"; only the local
        // adapters exist under DEP-02, and any other value fails validation at start-up rather than falling back silently.
        services.AddOptions<NotificationOptions>()
            .Bind(configuration.GetSection(NotificationOptions.SectionName))
            .Validate(o => o.Validate(), "Notification configuration is invalid: Implementation must be 'Local' (the only available adapter set), with an output path and a sender identity.")
            .ValidateOnStart();
        services.AddSingleton<IEmailDispatcher>(sp => new LocalEmailDispatcher(sp.GetRequiredService<IOptions<NotificationOptions>>(), contentRootPath, sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<ISmsDispatcher>(sp => new LocalSmsDispatcher(sp.GetRequiredService<IOptions<NotificationOptions>>(), contentRootPath, sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IRecordsArchive>(sp => new LocalRecordsArchive(sp.GetRequiredService<IOptions<NotificationOptions>>(), contentRootPath, sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<ILibraryCatalogue, LocalLibraryCatalogue>();

        return services;
    }
}
