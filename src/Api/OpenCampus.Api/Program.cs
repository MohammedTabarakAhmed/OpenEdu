using OpenCampus.Api.Middleware;
using OpenCampus.Api.Persistence;
using OpenCampus.Identity.Infrastructure;
using OpenCampus.Identity.Infrastructure.Persistence;
using OpenCampus.Lms.Infrastructure;
using OpenCampus.Lms.Infrastructure.Persistence;
using OpenCampus.Sis.Infrastructure;
using OpenCampus.Sis.Infrastructure.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

var connectionString = builder.Configuration.GetConnectionString("OpenCampus")
    ?? throw new InvalidOperationException("Connection string 'OpenCampus' is not configured.");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddIdentityInfrastructure(connectionString);
builder.Services.AddSisInfrastructure(connectionString);
builder.Services.AddLmsInfrastructure(connectionString);

builder.Services.AddHealthChecks()
    .AddDbContextCheck<IdentityDbContext>()
    .AddDbContextCheck<SisDbContext>()
    .AddDbContextCheck<LmsDbContext>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Demonstration"))
{
    await DatabaseInitializer.MigrateAsync(app.Services);
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapHealthChecks("/api/health");
app.MapControllers();

app.Run();

public partial class Program;
