using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using OpenCampus.Sis.Application.Catalogue;
using OpenCampus.Sis.Application.Certificates;
using OpenCampus.Sis.Application.Courses;
using OpenCampus.Sis.Application.Enrolments;
using OpenCampus.Sis.Application.External;
using OpenCampus.Sis.Application.Grading;
using OpenCampus.Sis.Application.Learners;
using OpenCampus.Sis.Application.Programmes;
using OpenCampus.Sis.Application.Provisioning;
using OpenCampus.Sis.Application.Reports;
using OpenCampus.Sis.Application.Sections;

namespace OpenCampus.Sis.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddSisApplication(this IServiceCollection services)
    {
        services.AddScoped<ProgrammeService>();
        services.AddScoped<CourseService>();
        services.AddScoped<SectionService>();
        services.AddScoped<LearnerService>();
        services.AddScoped<EnrolmentService>();
        services.AddScoped<LearnerSelfService>();
        services.AddScoped<EnrolmentStandingService>();
        services.AddScoped<GradingService>();
        services.AddScoped<CertificateService>();
        services.AddScoped<LearnerNotifier>();
        services.AddScoped<ReportingService>();
        services.AddScoped<SisProvisioner>();
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        return services;
    }
}
