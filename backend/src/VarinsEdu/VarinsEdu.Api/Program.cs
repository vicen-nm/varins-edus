using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using System.Text;
using VarinsEdu.Api.Errors;
using VarinsEdu.Api.Security;
using VarinsEdu.Api.Seeding;
using VarinsEdu.Api.Setup;
using VarinsEdu.Api.Tenancy;
using VarinsEdu.Domain.Common;
using VarinsEdu.Infrastructure.Access;
using VarinsEdu.Infrastructure.Auditing;
using VarinsEdu.Infrastructure.Imports;
using VarinsEdu.Infrastructure.Institutions;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;
using VarinsEdu.Infrastructure.Seeding;
using VarinsEdu.Infrastructure.Students;

var builder = WebApplication.CreateBuilder(args);

// --- Web API basics ---
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddFrontendCors(builder.Configuration);
builder.Services.AddAppHealthChecks();

// --- Who is calling (read from the token) ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenant, HttpCurrentTenant>();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// --- Database (with the audit interceptor) ---
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("Default"))
        .AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>()));

// --- Security ---
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPasswordHasher, PasswordHasherService>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

if (jwtOptions.Key.Length < 32)
{
    throw new InvalidOperationException("Jwt:Key must be configured with at least 32 characters.");
}

builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<BrandingService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep claim names exactly as we wrote them (sub, institution_id, permission...).
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options => options.AddPermissionPolicies());

// --- Application services ---
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<InstitutionService>();
builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddHostedService<SeedHostedService>();
builder.Services.AddScoped<StudentImportService>();
builder.Services.AddScoped<AuditSuppression>();
builder.Services.AddSingleton(new ActivationCodeHasher(jwtOptions.Key));
builder.Services.AddScoped<StudentImportCommitter>();
builder.Services.AddAppRateLimiting();
builder.Services.AddScoped<StudentActivationService>();
builder.Services.AddScoped<StudentManagementService>();
builder.Services.AddSingleton<CardTokenService>(serviceProvider =>
    new CardTokenService(jwtOptions.Key, serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<AccessPointTokenService>(serviceProvider =>
    new AccessPointTokenService(jwtOptions.Key, serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddScoped<StudentCardService>();
builder.Services.AddScoped<AccessPointService>();
builder.Services.AddScoped<AccessRegistrationService>();

// --- Seed data at startup ---
builder.Services.AddSingleton(
    builder.Configuration.GetSection(SeedOptions.SectionName).Get<SeedOptions>() ?? new SeedOptions());

var app = builder.Build();

// The error handler goes first so it can catch whatever happens further down the pipeline.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseCors(CorsSetup.PolicyName);
app.UseHttpsRedirection();
app.UseCors(CorsSetup.PolicyName);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapAppHealthChecks();

app.Run();
