using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Institutions;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;
using VarinsEdu.Infrastructure.Seeding;
using Xunit;

namespace VarinsEdu.Tests;

public class InstitutionServiceTests
{
    private const string AdminPassword = "a-long-local-password";

    private sealed class FakeTenant(Guid? institutionId) : ICurrentTenant
    {
        public Guid? InstitutionId { get; } = institutionId;
        public bool IsPlatformScope => false;
    }

    private static DbContextOptions<AppDbContext> NewOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static InstitutionService PlatformService(DbContextOptions<AppDbContext> options)
    {
        var tenant = new PlatformTenant();
        return new InstitutionService(
            new AppDbContext(options, tenant), new PasswordHasherService(), tenant, TimeProvider.System);
    }

    private static CreateInstitutionCommand ValidCommand(
        string slug = "demo-a",
        string username = "demo.a.admin") =>
        new("Colegio Demo A", slug, username, AdminPassword);

    [Fact]
    public async Task Creates_institution_with_defaults_and_first_admin()
    {
        var options = NewOptions();

        var result = await PlatformService(options).CreateAsync(ValidCommand());

        Assert.True(result.Succeeded);

        using var db = new AppDbContext(options, new PlatformTenant());

        var institution = await db.Institutions.SingleAsync();
        Assert.Equal(result.InstitutionId, institution.Id);
        Assert.Equal("demo-a", institution.Slug);

        Assert.Equal(RoleTemplates.ForInstitutions.Count, await db.Roles.CountAsync(r => r.InstitutionId == institution.Id));

        var modules = await db.InstitutionModules.ToListAsync();
        Assert.Equal(ModuleKeys.All.Length, modules.Count);
        Assert.All(modules, m => Assert.False(m.IsEnabled));

        Assert.Equal(1, await db.InstitutionSettings.CountAsync());

        var admin = await db.Users.SingleAsync();
        Assert.Equal(institution.Id, admin.InstitutionId);
        Assert.Equal("demo.a.admin", admin.Username);
        Assert.NotEqual(AdminPassword, admin.PasswordHash);
        Assert.True(new PasswordHasherService().Verify(admin.PasswordHash, AdminPassword));

        var link = await db.UserRoles.SingleAsync();
        var role = await db.Roles.SingleAsync(r => r.Id == link.RoleId);
        Assert.Equal(RoleKeys.InstitutionAdmin, role.Key);
    }

    [Fact]
    public async Task Institution_admin_does_not_receive_platform_permissions()
    {
        var options = NewOptions();

        await PlatformService(options).CreateAsync(ValidCommand());

        using var db = new AppDbContext(options, new PlatformTenant());
        var adminRole = await db.Roles
            .Include(r => r.RolePermissions)
            .SingleAsync(r => r.Key == RoleKeys.InstitutionAdmin);

        var granted = adminRole.RolePermissions.Select(rp => rp.PermissionKey).ToList();

        Assert.Equal(PermissionKeys.ForInstitutions.Length, granted.Count);
        Assert.DoesNotContain(PermissionKeys.InstitutionsManage, granted);
    }

    [Fact]
    public async Task Duplicate_slug_is_rejected()
    {
        var service = PlatformService(NewOptions());

        await service.CreateAsync(ValidCommand());
        var second = await service.CreateAsync(ValidCommand(username: "someone.else"));

        Assert.Equal(InstitutionCreateError.SlugTaken, second.Error);
    }

    [Fact]
    public async Task Duplicate_username_is_rejected()
    {
        var service = PlatformService(NewOptions());

        await service.CreateAsync(ValidCommand());
        var second = await service.CreateAsync(ValidCommand(slug: "demo-b"));

        Assert.Equal(InstitutionCreateError.UsernameTaken, second.Error);
    }

    [Theory]
    [InlineData("ab", "demo-a", "demo.a.admin", AdminPassword, InstitutionCreateError.InvalidName)]
    [InlineData("Colegio Demo", "Bad Slug!", "demo.a.admin", AdminPassword, InstitutionCreateError.InvalidSlug)]
    [InlineData("Colegio Demo", "demo-a", "ab", AdminPassword, InstitutionCreateError.InvalidUsername)]
    [InlineData("Colegio Demo", "demo-a", "demo.a.admin", "short", InstitutionCreateError.WeakPassword)]
    public async Task Invalid_data_is_rejected(
        string name,
        string slug,
        string username,
        string password,
        InstitutionCreateError expected)
    {
        var options = NewOptions();

        var result = await PlatformService(options).CreateAsync(new CreateInstitutionCommand(name, slug, username, password));

        Assert.Equal(expected, result.Error);

        // Nothing must be created when validation fails.
        using var db = new AppDbContext(options, new PlatformTenant());
        Assert.Empty(await db.Institutions.ToListAsync());
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task Slug_is_normalized_to_lowercase()
    {
        var options = NewOptions();

        var result = await PlatformService(options).CreateAsync(ValidCommand(slug: "  Liceo-Demo  "));

        Assert.True(result.Succeeded);

        using var db = new AppDbContext(options, new PlatformTenant());
        Assert.Equal("liceo-demo", (await db.Institutions.SingleAsync()).Slug);
    }

    [Fact]
    public async Task Non_platform_caller_cannot_create_institutions()
    {
        var tenant = new FakeTenant(Guid.NewGuid());
        var service = new InstitutionService(
            new AppDbContext(NewOptions(), tenant), new PasswordHasherService(), tenant, TimeProvider.System);

        await Assert.ThrowsAsync<TenantViolationException>(() => service.CreateAsync(ValidCommand()));
    }
}
