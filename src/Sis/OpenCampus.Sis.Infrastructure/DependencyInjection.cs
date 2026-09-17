using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenCampus.Sis.Application.Abstractions;
using OpenCampus.Sis.Application.Grading;
using OpenCampus.Sis.Application.Provisioning;
using OpenCampus.Sis.Infrastructure.Persistence;
using OpenCampus.Sis.Infrastructure.Persistence.Repositories;

namespace OpenCampus.Sis.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSisInfrastructure(this IServiceCollection services, IConfiguration configuration, string connectionString)
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

        services.AddScoped<IProgrammeRepository, ProgrammeRepository>();
        services.AddScoped<ICourseRepository, CourseRepository>();
        services.AddScoped<ISectionRepository, SectionRepository>();
        services.AddScoped<ILearnerRepository, LearnerRepository>();
        services.AddScoped<IEnrolmentRepository, EnrolmentRepository>();
        services.AddScoped<IGradeEntryRepository, GradeEntryRepository>();
        services.AddScoped<ISisUnitOfWork, SisUnitOfWork>();

        return services;
    }
}
