using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using OpenCampus.Identity.Application.Administration;
using OpenCampus.Identity.Application.Authentication;
using OpenCampus.Identity.Application.Provisioning;
using OpenCampus.Identity.Application.Registration;

namespace OpenCampus.Identity.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthenticationService>();
        services.AddScoped<UserAdministrationService>();
        services.AddScoped<IdentityProvisioner>();
        services.AddScoped<RegistrationNotifier>();
        services.AddScoped<RegistrationService>();
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        return services;
    }
}
