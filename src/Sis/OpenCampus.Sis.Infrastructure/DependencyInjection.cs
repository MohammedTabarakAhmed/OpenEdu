using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCampus.Sis.Infrastructure.Persistence;

namespace OpenCampus.Sis.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSisInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<SisDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", SisDbContext.Schema)));

        return services;
    }
}
