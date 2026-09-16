using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenCampus.Sis.Application.Abstractions;
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

        services.AddScoped<IProgrammeRepository, ProgrammeRepository>();
        services.AddScoped<ICourseRepository, CourseRepository>();
        services.AddScoped<ISectionRepository, SectionRepository>();
        services.AddScoped<ILearnerRepository, LearnerRepository>();
        services.AddScoped<IEnrolmentRepository, EnrolmentRepository>();
        services.AddScoped<ISisUnitOfWork, SisUnitOfWork>();

        return services;
    }
}
