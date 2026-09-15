using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCampus.Lms.Infrastructure.Persistence;

namespace OpenCampus.Lms.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddLmsInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<LmsDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", LmsDbContext.Schema)));

        return services;
    }
}
