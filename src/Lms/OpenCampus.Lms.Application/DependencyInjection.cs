using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using OpenCampus.Lms.Application.Assessment;
using OpenCampus.Lms.Application.Content;
using OpenCampus.Lms.Application.Provisioning;

namespace OpenCampus.Lms.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddLmsApplication(this IServiceCollection services)
    {
        services.AddScoped<SectionScopeResolver>();
        services.AddScoped<ContentService>();
        services.AddScoped<ResourceService>();
        services.AddScoped<LearnerContentService>();
        services.AddScoped<AssignmentService>();
        services.AddScoped<AttendanceService>();
        services.AddScoped<LmsProvisioner>();
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        return services;
    }
}
