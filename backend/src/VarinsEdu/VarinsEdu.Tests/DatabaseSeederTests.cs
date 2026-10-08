using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;
using VarinsEdu.Infrastructure.Seeding;
using Xunit;

namespace VarinsEdu.Tests;

public class DatabaseSeederTests
{
    private const string AdminPassword = "a-long-local-password";

    private static (DbContextOptions<AppDbContext> Options, DatabaseSeeder Seeder) Create(SeedOptions seedOptions)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var seeder = new DatabaseSeeder(
            options,
            new PasswordHasherService(),
            seedOptions,
            NullLogger<DatabaseSeeder>.Instance);

        return (options, seeder);
    }

    private static AppDbContext PlatformContext(DbContextOptions<AppDbContext> options) =>
        new(options, new PlatformTenant());

    private static async Task<Guid> AddInstitutionAsync(DbContextOptions<AppDbContext> options)
    {
        var id = Guid.NewGuid();

        using var db = PlatformContext(options);
        db.Institutions.Add(new Institution { Id = id, Name = "Demo", Slug = "demo" });
        await db.SaveChangesAsync();

        return id;
    }

    [Fact]
    public async Task Permissions_are_created_and_not_duplicated()
    {
        var (options, seeder) = Create(new SeedOptions());

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        using var db = PlatformContext(options);
        Assert.Equal(PermissionKeys.All.Length, await db.Permissions.CountAsync());
    }

    [Fact]
    public async Task Platform_role_gets_every_permission()
    {
        var (options, seeder) = Create(new SeedOptions());

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        using var db = PlatformContext(options);
        var role = await db.Roles
            .Include(r => r.RolePermissions)
            .SingleAsync(r => r.InstitutionId == null && r.Key == RoleKeys.PlatformAdmin);

        Assert.Equal(PermissionKeys.All.Length, role.RolePermissions.Count);
    }

    [Fact]
    public async Task Platform_admin_is_created_once_with_a_hashed_password()
    {
        var (options, seeder) = Create(new SeedOptions
        {
            PlatformAdminUsername = "Admin.One",
            PlatformAdminPassword = AdminPassword
        });

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        using var db = PlatformContext(options);
        var user = Assert.Single(await db.Users.ToListAsync());

        Assert.Equal("admin.one", user.Username);
        Assert.Null(user.InstitutionId);
        Assert.NotEqual(AdminPassword, user.PasswordHash);
        Assert.True(new PasswordHasherService().Verify(user.PasswordHash, AdminPassword));
        Assert.Single(await db.UserRoles.ToListAsync());
    }

    [Fact]
    public async Task No_platform_admin_is_created_without_configuration()
    {
        var (options, seeder) = Create(new SeedOptions());

        await seeder.SeedAsync();

        using var db = PlatformContext(options);
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task Short_platform_admin_password_is_rejected()
    {
        var (_, seeder) = Create(new SeedOptions
        {
            PlatformAdminUsername = "admin",
            PlatformAdminPassword = "short"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync());
    }

    [Fact]
    public async Task Institution_defaults_create_roles_modules_and_settings()
    {
        var (options, seeder) = Create(new SeedOptions());
        var institutionId = await AddInstitutionAsync(options);

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        using var db = PlatformContext(options);

        var roles = await db.Roles
            .Include(r => r.RolePermissions)
            .Where(r => r.InstitutionId == institutionId)
            .ToListAsync();
        Assert.Equal(RoleTemplates.ForInstitutions.Count, roles.Count);

        var admin = roles.Single(r => r.Key == RoleKeys.InstitutionAdmin);
        var student = roles.Single(r => r.Key == RoleKeys.Student);
        Assert.Equal(PermissionKeys.ForInstitutions.Length, admin.RolePermissions.Count);
        Assert.Empty(student.RolePermissions);

        var modules = await db.InstitutionModules.Where(m => m.InstitutionId == institutionId).ToListAsync();
        Assert.Equal(ModuleKeys.All.Length, modules.Count);
        Assert.All(modules, m => Assert.False(m.IsEnabled));

        Assert.Equal(1, await db.InstitutionSettings.CountAsync(s => s.InstitutionId == institutionId));
    }

    [Fact]
    public async Task Removed_permissions_are_not_restored()
    {
        var (options, seeder) = Create(new SeedOptions());
        var institutionId = await AddInstitutionAsync(options);

        await seeder.SeedAsync();

        using (var db = PlatformContext(options))
        {
            var admin = await db.Roles
                .Include(r => r.RolePermissions)
                .SingleAsync(r => r.InstitutionId == institutionId && r.Key == RoleKeys.InstitutionAdmin);

            db.RolePermissions.Remove(admin.RolePermissions.First());
            await db.SaveChangesAsync();
        }

        await seeder.SeedAsync();

        using (var db = PlatformContext(options))
        {
            var admin = await db.Roles
                .Include(r => r.RolePermissions)
                .SingleAsync(r => r.InstitutionId == institutionId && r.Key == RoleKeys.InstitutionAdmin);

            Assert.Equal(PermissionKeys.ForInstitutions.Length - 1, admin.RolePermissions.Count);
        }
    }
}
