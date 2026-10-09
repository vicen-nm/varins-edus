using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;
using VarinsEdu.Infrastructure.Seeding;
using Xunit;

namespace VarinsEdu.Tests;

public class AuthServiceTests
{
    private const string Password = "a-long-local-password";
    private readonly PasswordHasherService _hasher = new();

    // Nobody is logged in yet when credentials are checked.
    private sealed class NoTenant : ICurrentTenant
    {
        public Guid? InstitutionId => null;
        public bool IsPlatformScope => false;
    }

    private Role NewRole(Guid? institutionId, string key, params string[] permissions)
    {
        var role = new Role
        {
            Id = Guid.NewGuid(),
            InstitutionId = institutionId,
            Key = key,
            Name = key,
            IsSystem = true
        };

        foreach (var permission in permissions)
        {
            role.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionKey = permission });
        }

        return role;
    }

    private User NewUser(string username, Guid? institutionId, bool isActive, params Role[] roles)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            InstitutionId = institutionId,
            Username = username,
            PasswordHash = _hasher.Hash(Password),
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow
        };

        foreach (var role in roles)
        {
            user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        }

        return user;
    }

    private (DbContextOptions<AppDbContext> Options, Guid ActiveInstitution) CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var active = Guid.NewGuid();
        var inactive = Guid.NewGuid();

        using var db = new AppDbContext(options, new PlatformTenant());

        db.Institutions.AddRange(
            new Institution { Id = active, Name = "Active", Slug = "active", IsActive = true },
            new Institution { Id = inactive, Name = "Inactive", Slug = "inactive", IsActive = false });

        var teacher = NewRole(active, "teacher", PermissionKeys.StudentsView);
        var admin = NewRole(active, "institution_admin", PermissionKeys.StudentsView, PermissionKeys.UsersManage);
        var platformRole = NewRole(null, "platform_admin", PermissionKeys.UsersManage);
        db.Roles.AddRange(teacher, admin, platformRole);

        db.Users.AddRange(
            NewUser("ana", active, true, teacher, admin),
            NewUser("inactive-user", active, false, teacher),
            NewUser("ghost", inactive, true),
            NewUser("platform", null, true, platformRole));

        db.SaveChanges();

        return (options, active);
    }

    // Nobody is logged in yet, so there is no current user either.
    private sealed class NoUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string? IpAddress => "203.0.113.7";
    }

    private AuthService CreateService(DbContextOptions<AppDbContext> options) =>
        new(new AppDbContext(options, new NoTenant()), _hasher, new NoUser(), TimeProvider.System);

    [Fact]
    public async Task Valid_credentials_return_the_user_with_distinct_permissions()
    {
        var (options, institutionId) = CreateDb();

        var user = await CreateService(options).ValidateCredentialsAsync("ana", Password);

        Assert.NotNull(user);
        Assert.Equal(institutionId, user.InstitutionId);
        Assert.False(user.IsPlatform);
        Assert.Equal(new[] { PermissionKeys.StudentsView, PermissionKeys.UsersManage }.Order(), user.Permissions);
    }

    [Fact]
    public async Task Username_is_not_case_sensitive()
    {
        var (options, _) = CreateDb();

        var user = await CreateService(options).ValidateCredentialsAsync("  ANA ", Password);

        Assert.NotNull(user);
        Assert.Equal("ana", user.Username);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        var (options, _) = CreateDb();

        Assert.Null(await CreateService(options).ValidateCredentialsAsync("ana", "not-the-password"));
    }

    [Fact]
    public async Task Unknown_user_is_rejected()
    {
        var (options, _) = CreateDb();

        Assert.Null(await CreateService(options).ValidateCredentialsAsync("nobody", Password));
    }

    [Fact]
    public async Task Inactive_user_is_rejected()
    {
        var (options, _) = CreateDb();

        Assert.Null(await CreateService(options).ValidateCredentialsAsync("inactive-user", Password));
    }

    [Fact]
    public async Task User_of_an_inactive_institution_is_rejected()
    {
        var (options, _) = CreateDb();

        Assert.Null(await CreateService(options).ValidateCredentialsAsync("ghost", Password));
    }

    [Fact]
    public async Task Platform_user_is_flagged_as_platform()
    {
        var (options, _) = CreateDb();

        var user = await CreateService(options).ValidateCredentialsAsync("platform", Password);

        Assert.NotNull(user);
        Assert.True(user.IsPlatform);
        Assert.Null(user.InstitutionId);
    }

    private static async Task<List<AuditLog>> ReadAuditAsync(DbContextOptions<AppDbContext> options)
    {
        using var db = new AppDbContext(options, new PlatformTenant());
        return await db.AuditLogs.ToListAsync();
    }

    [Fact]
    public async Task Successful_login_is_audited()
    {
        var (options, institutionId) = CreateDb();

        var user = await CreateService(options).ValidateCredentialsAsync("ana", Password);

        var log = Assert.Single(await ReadAuditAsync(options));
        Assert.Equal("auth.login.succeeded", log.Action);
        Assert.Equal(user!.UserId, log.UserId);
        Assert.Equal(institutionId, log.InstitutionId);
        Assert.Equal("203.0.113.7", log.IpAddress);
    }

    [Fact]
    public async Task Failed_login_for_an_unknown_user_is_audited_without_a_user()
    {
        var (options, _) = CreateDb();

        await CreateService(options).ValidateCredentialsAsync("nobody", Password);

        var log = Assert.Single(await ReadAuditAsync(options));
        Assert.Equal("auth.login.failed", log.Action);
        Assert.Null(log.UserId);
        Assert.Null(log.InstitutionId);
        Assert.Contains("unknown_user", log.Metadata);
        Assert.Contains("nobody", log.Metadata);
    }

    [Fact]
    public async Task Wrong_password_is_audited_with_the_user_and_never_stores_the_password()
    {
        var (options, institutionId) = CreateDb();

        await CreateService(options).ValidateCredentialsAsync("ana", "not-the-password");

        var log = Assert.Single(await ReadAuditAsync(options));
        Assert.Equal("auth.login.failed", log.Action);
        Assert.NotNull(log.UserId);
        Assert.Equal(institutionId, log.InstitutionId);
        Assert.Contains("wrong_password", log.Metadata);
        Assert.DoesNotContain("not-the-password", log.Metadata);
    }
}
