using OpenCampus.Api.Persistence;
using OpenCampus.Identity.Infrastructure;
using OpenCampus.Lms.Infrastructure;
using OpenCampus.Sis.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("OpenCampus")
    ?? throw new InvalidOperationException("Connection string 'OpenCampus' is not configured.");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddIdentityInfrastructure(connectionString);
builder.Services.AddSisInfrastructure(connectionString);
builder.Services.AddLmsInfrastructure(connectionString);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Demonstration"))
{
    await DatabaseInitializer.MigrateAsync(app.Services);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
