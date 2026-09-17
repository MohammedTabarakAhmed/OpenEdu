using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json.Serialization;
using OpenCampus.Api.ErrorHandling;
using OpenCampus.Api.Integration;
using OpenCampus.Api.Middleware;
using OpenCampus.Api.Persistence;
using OpenCampus.Api.Security;
using OpenCampus.Api.Validation;
using OpenCampus.Identity.Application;
using OpenCampus.Identity.Application.Abstractions;
using OpenCampus.Identity.Application.Security;
using OpenCampus.Identity.Infrastructure;
using OpenCampus.Identity.Infrastructure.Persistence;
using OpenCampus.Identity.Infrastructure.Security;
using OpenCampus.Lms.Application;
using OpenCampus.Lms.Application.Abstractions;
using OpenCampus.Lms.Application.Storage;
using OpenCampus.Lms.Infrastructure;
using OpenCampus.Lms.Infrastructure.Persistence;
using OpenCampus.Sis.Application;
using OpenCampus.Sis.Application.Abstractions;
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
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<HttpCurrentUser>();
builder.Services.AddScoped<OpenCampus.Identity.Application.Abstractions.ICurrentUser>(sp => sp.GetRequiredService<HttpCurrentUser>());
builder.Services.AddScoped<OpenCampus.Sis.Application.Abstractions.ICurrentUser>(sp => sp.GetRequiredService<HttpCurrentUser>());
builder.Services.AddScoped<OpenCampus.Lms.Application.Abstractions.ICurrentUser>(sp => sp.GetRequiredService<HttpCurrentUser>());

// Key material lives in a configured directory excluded from source control (SDD 9.2, Appendix B).
var keyDirectory = builder.Configuration["DataProtection:KeyDirectory"]
    ?? throw new InvalidOperationException("Setting 'DataProtection:KeyDirectory' is not configured.");
builder.Services.AddDataProtection()
    .SetApplicationName("OpenCampus")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, keyDirectory)));

// Module composition occurs only here (MB-05).
builder.Services.AddIdentityApplication();
builder.Services.AddIdentityInfrastructure(builder.Configuration, connectionString, builder.Environment.ContentRootPath);
builder.Services.AddSisApplication();
builder.Services.AddSisInfrastructure(builder.Configuration, connectionString);
builder.Services.AddLmsApplication();
builder.Services.AddLmsInfrastructure(builder.Configuration, connectionString, builder.Environment.ContentRootPath);
// Cross-module contracts (6.5, MB-02), implemented at the composition root: SIS → Identity, LMS → SIS.
builder.Services.AddScoped<IUserDirectory, IdentityUserDirectory>();
builder.Services.AddScoped<ISectionAccess, SisSectionAccess>();
builder.Services.AddScoped<IAssessmentOutcomes, SisAssessmentOutcomes>();
builder.Services.AddScoped<IAuditTrail, IdentityAuditTrail>();

// SEC-22 at the framework level: the multipart and request body limits mirror Storage:MaxUploadSizeBytes,
// so an oversized upload is cut off before the application layer sees it (which enforces the same bound again).
var maxUpload = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()?.MaxUploadSizeBytes
    ?? new StorageOptions().MaxUploadSizeBytes;
const long multipartOverhead = 64 * 1024;
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = maxUpload);
builder.Services.Configure<KestrelServerOptions>(options => options.Limits.MaxRequestBodySize = maxUpload + multipartOverhead);

// SEC-03/SEC-04: bearer tokens are validated against the public half of the persisted RSA key.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<ISigningKeyProvider, IOptions<TokenOptions>>((bearer, keys, tokens) =>
    {
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = tokens.Value.Issuer,
            ValidAudience = tokens.Value.Audience,
            IssuerSigningKey = keys.ValidationKey,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = JwtRegisteredClaimNames.UniqueName,
            RoleClaimType = JwtAccessTokenIssuer.RoleClaim,
        };
    });

// SEC-10: deny by default. An endpoint without an explicit declaration requires an authenticated caller.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build())
    .AddPermissionPolicies(); // SEC-11

var accountProtection = builder.Configuration.GetSection(AccountProtectionOptions.SectionName).Get<AccountProtectionOptions>()
    ?? new AccountProtectionOptions();
builder.Services.AddOpenCampusRateLimiting(accountProtection);

builder.Services.AddHealthChecks()
    .AddDbContextCheck<IdentityDbContext>()
    .AddDbContextCheck<SisDbContext>()
    .AddDbContextCheck<LmsDbContext>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services
    .AddControllers(options => options.Filters.Add<ValidationFilter>())
    // Reference sets (13.6) travel as their names, e.g. "Open", "InPerson", not as integers.
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// API-04: binding failures (malformed JSON, unreadable form) use the same problem shape as the validation filter.
// Registered after AddControllers so the framework's own default does not override it.
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
    options.InvalidModelStateResponseFactory = context => ProblemMapping.ToInvalidModelStateResult(context));
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Demonstration"))
{
    await DatabaseInitializer.MigrateAsync(app.Services);
}

// SDD 18.7: reference data on startup in all environments. The integration test fixture
// creates its isolated database first and then provisions explicitly (TST-01).
if (!app.Environment.IsEnvironment("Test"))
{
    await DatabaseInitializer.ProvisionAsync(app.Services, app.Environment.ContentRootPath);
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseHttpsRedirection();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/api/health").AllowAnonymous();
app.MapControllers();

app.Run();

public partial class Program;
