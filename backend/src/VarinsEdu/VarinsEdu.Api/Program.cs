using Microsoft.EntityFrameworkCore;
using VarinsEdu.Api.Tenancy;
using VarinsEdu.Domain.Common;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Api.Seeding;
using VarinsEdu.Infrastructure.Security;
using VarinsEdu.Infrastructure.Seeding;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenant, HttpCurrentTenant>();

builder.Services.AddSingleton<IPasswordHasher, PasswordHasherService>();
builder.Services.AddSingleton(
    builder.Configuration.GetSection(SeedOptions.SectionName).Get<SeedOptions>() ?? new SeedOptions());
builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddHostedService<SeedHostedService>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
