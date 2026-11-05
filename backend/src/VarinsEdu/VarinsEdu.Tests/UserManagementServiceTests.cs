using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Accounts;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;
using VarinsEdu.Infrastructure.Seeding;
using Xunit;

namespace VarinsEdu.Tests;

public class UserManagementServiceTests
{
    private static readonly PasswordHasherService Hasher = new();
    private static readonly ResetCodeHasher ResetHasher = new(new string('k', 48));

    private sealed class FakeTenant(Guid? institutionId) : ICurrentTenant
    {
        public Guid? InstitutionId { get; } = institutionId;
        public bool IsPlatformScope => false;
    }

    private sealed class FakeUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
        public string? IpAddress => "203.0.113.7";
    }

    private sealed record Scenario(
        DbContextOptions<AppDbContext> Options,
        Guid InstitutionId,
        Guid AdminId,
        Guid TeacherId,
        Guid StudentUserId,
        Guid OtherInstitutionId,
        Guid OtherAdminId);

    private static Role NewRole(Guid institutionId, string key, params string[] permissions)
    {
        var role = new Role { Id = Guid.NewGuid(), InstitutionId = institutionId, Key = key, Name = key, IsSystem = true };

        foreach (var permission in permissions)
        {
            role.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionKey = permission });
        }

        return role;
    }

    private static User NewUser(Guid institutionId, string username, Guid? personId, Role role)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            InstitutionId = institutionId,
            PersonId = personId,
            Username = username,
            PasswordHash = Hasher.Hash("a-long-local-password"),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });

        return user;
    }

    // Institution A: an administrator, a teacher and a student account. Institution B: its own administrator.
    private static async Task<Scenario> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        using var db = new AppDbContext(options, new PlatformTenant());

        db.Institutions.AddRange(
            new Institution { Id = a, Name = "Colegio A", Slug = "demo-a" },
            new Institution { Id = b, Name = "Colegio B", Slug = "demo-b" });

        var admin = NewRole(a, RoleKeys.InstitutionAdmin, PermissionKeys.UsersManage);
        var teacher = NewRole(a, RoleKeys.Teacher, PermissionKeys.StudentsView);
        var cafeteria = NewRole(a, RoleKeys.CafeteriaStaff, PermissionKeys.AccessScan, PermissionKeys.AccessView);
        var student = NewRole(a, RoleKeys.Student);
        var otherAdmin = NewRole(b, RoleKeys.InstitutionAdmin, PermissionKeys.UsersManage);
        db.Roles.AddRange(admin, teacher, cafeteria, student, otherAdmin);

        var person = new Person
        {
            Id = Guid.NewGuid(),
            InstitutionId = a,
            IdentificationNumber = "111111111",
            FirstName = "Ana",
            LastName = "Prueba"
        };
        db.People.Add(person);

        var adminUser = NewUser(a, "demo.a.admin", null, admin);
        var teacherUser = NewUser(a, "pedro@demo-a", null, teacher);
        var studentUser = NewUser(a, "111111111@demo-a", person.Id, student);
        var otherUser = NewUser(b, "demo.b.admin", null, otherAdmin);
        db.Users.AddRange(adminUser, teacherUser, studentUser, otherUser);

        await db.SaveChangesAsync();

        return new Scenario(options, a, adminUser.Id, teacherUser.Id, studentUser.Id, b, otherUser.Id);
    }

    private static UserManagementService ServiceFor(DbContextOptions<AppDbContext> options, Guid? institutionId, Guid? actingUserId)
    {
        var tenant = new FakeTenant(institutionId);

        return new UserManagementService(
            new AppDbContext(options, tenant),
            tenant,
            new FakeUser(actingUserId),
            Hasher,
            ResetHasher,
            TimeProvider.System);
    }

    private static AppDbContext Reader(DbContextOptions<AppDbContext> options) =>
        new(options, new PlatformTenant());

    [Fact]
    public async Task Creating_a_staff_account_returns_a_one_time_code()
    {
        var s = await SeedAsync();

        var result = await ServiceFor(s.Options, s.InstitutionId, s.AdminId)
            .CreateStaffAsync(new CreateStaffInput("  Maria.Perez ", RoleKeys.Teacher));

        Assert.Null(result.Error);
        Assert.Equal("maria.perez@demo-a", result.Value!.Username);

        using var db = Reader(s.Options);
        var user = await db.Users.SingleAsync(u => u.Username == "maria.perez@demo-a");

        Assert.Null(user.PersonId);
        Assert.Equal(s.InstitutionId, user.InstitutionId);
        Assert.True(user.IsActive);

        var roleKey = await db.UserRoles.Where(ur => ur.UserId == user.Id).Select(ur => ur.Role.Key).SingleAsync();
        Assert.Equal(RoleKeys.Teacher, roleKey);

        var row = await db.PasswordResetCodes.SingleAsync(c => c.UserId == user.Id);

        Assert.NotEqual(result.Value.Code, row.CodeHash);
        Assert.True(ResetHasher.Verify(row.CodeHash, result.Value.Code));
        Assert.True(row.ExpiresAt > DateTimeOffset.UtcNow.AddHours(23));
    }

    [Fact]
    public async Task Invalid_data_is_rejected_and_usernames_are_unique()
    {
        var s = await SeedAsync();
        var service = ServiceFor(s.Options, s.InstitutionId, s.AdminId);

        Assert.Equal("invalid_username", (await service.CreateStaffAsync(new("ab", RoleKeys.Teacher))).Error);
        Assert.Equal("invalid_username", (await service.CreateStaffAsync(new("has space", RoleKeys.Teacher))).Error);
        Assert.Equal("invalid_username", (await service.CreateStaffAsync(new("bad@other-school", RoleKeys.Teacher))).Error);
        Assert.Equal("invalid_role", (await service.CreateStaffAsync(new("maria.perez", RoleKeys.Student))).Error);
        Assert.Equal("invalid_role", (await service.CreateStaffAsync(new("maria.perez", RoleKeys.PlatformAdmin))).Error);
        Assert.Equal("invalid_role", (await service.CreateStaffAsync(new("maria.perez", null))).Error);

        Assert.Null((await service.CreateStaffAsync(new("maria.perez", RoleKeys.Teacher))).Error);
        Assert.Equal("username_taken", (await service.CreateStaffAsync(new("MARIA.PEREZ", RoleKeys.Teacher))).Error);
    }

    [Fact]
    public async Task The_list_separates_staff_and_students_and_never_shows_other_institutions()
    {
        var s = await SeedAsync();
        var service = ServiceFor(s.Options, s.InstitutionId, s.AdminId);

        var staff = await service.ListAsync(null, null, 0, 50);
        var students = await service.ListAsync("student", null, 0, 50);
        var all = await service.ListAsync("all", null, 0, 50);
        var searched = await service.ListAsync("all", "pedro", 0, 50);

        Assert.Equal(
            new[] { "demo.a.admin", "pedro@demo-a" },
            staff.Items.Select(i => i.Username).Order().ToArray());
        Assert.All(staff.Items, i => Assert.False(i.IsStudent));

        var student = Assert.Single(students.Items);
        Assert.Equal("111111111@demo-a", student.Username);
        Assert.True(student.IsStudent);

        Assert.Equal(3, all.Total);
        Assert.DoesNotContain(all.Items, i => i.Username == "demo.b.admin");

        Assert.Equal("pedro@demo-a", Assert.Single(searched.Items).Username);
    }

    [Fact]
    public async Task Roles_can_be_replaced_but_not_for_yourself_students_or_other_institutions()
    {
        var s = await SeedAsync();
        var service = ServiceFor(s.Options, s.InstitutionId, s.AdminId);

        Assert.Null((await service.SetRolesAsync(s.TeacherId, new[] { RoleKeys.CafeteriaStaff })).Error);

        using (var db = Reader(s.Options))
        {
            var keys = await db.UserRoles.Where(ur => ur.UserId == s.TeacherId).Select(ur => ur.Role.Key).ToListAsync();

            Assert.Equal(new[] { RoleKeys.CafeteriaStaff }, keys);
        }

        Assert.Equal("cannot_modify_self", (await service.SetRolesAsync(s.AdminId, new[] { RoleKeys.Teacher })).Error);
        Assert.Equal("invalid_role", (await service.SetRolesAsync(s.TeacherId, new[] { RoleKeys.Student })).Error);
        Assert.Equal("invalid_role", (await service.SetRolesAsync(s.TeacherId, Array.Empty<string>())).Error);
        Assert.Equal("user_not_found", (await service.SetRolesAsync(Guid.NewGuid(), new[] { RoleKeys.Teacher })).Error);
        Assert.Equal("user_not_found", (await service.SetRolesAsync(s.OtherAdminId, new[] { RoleKeys.Teacher })).Error);
        Assert.Equal("not_staff", (await service.SetRolesAsync(s.StudentUserId, new[] { RoleKeys.Teacher })).Error);
    }

    [Fact]
    public async Task Accounts_can_be_deactivated_but_the_last_administrator_is_protected()
    {
        var s = await SeedAsync();
        var admin = ServiceFor(s.Options, s.InstitutionId, s.AdminId);

        Assert.Null((await admin.SetActiveAsync(s.TeacherId, false)).Error);

        using (var db = Reader(s.Options))
        {
            Assert.False((await db.Users.SingleAsync(u => u.Id == s.TeacherId)).IsActive);
        }

        Assert.Null((await admin.SetActiveAsync(s.TeacherId, true)).Error);
        Assert.Equal("cannot_modify_self", (await admin.SetActiveAsync(s.AdminId, false)).Error);

        // A session that outlived its administrator's rights tries to remove the only administrator.
        var stale = ServiceFor(s.Options, s.InstitutionId, Guid.NewGuid());

        Assert.Equal("last_admin", (await stale.SetActiveAsync(s.AdminId, false)).Error);
        Assert.Equal("last_admin", (await stale.SetRolesAsync(s.AdminId, new[] { RoleKeys.Teacher })).Error);
    }

    [Fact]
    public async Task A_new_reset_code_replaces_the_old_one_and_resets_the_attempts()
    {
        var s = await SeedAsync();
        var service = ServiceFor(s.Options, s.InstitutionId, s.AdminId);

        var first = (await service.IssueResetCodeAsync(s.TeacherId)).Value!;

        using (var db = Reader(s.Options))
        {
            var row = await db.PasswordResetCodes.SingleAsync(c => c.UserId == s.TeacherId);
            row.FailedAttempts = 5;
            row.UsedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        var second = (await ServiceFor(s.Options, s.InstitutionId, s.AdminId).IssueResetCodeAsync(s.TeacherId)).Value!;

        using var check = Reader(s.Options);
        var stored = await check.PasswordResetCodes.SingleAsync(c => c.UserId == s.TeacherId);

        Assert.False(ResetHasher.Verify(stored.CodeHash, first.Code));
        Assert.True(ResetHasher.Verify(stored.CodeHash, second.Code));
        Assert.Equal(0, stored.FailedAttempts);
        Assert.Null(stored.UsedAt);

        // Students who forgot their password get a code the same way.
        Assert.Null((await service.IssueResetCodeAsync(s.StudentUserId)).Error);
    }

    [Fact]
    public async Task Reset_codes_need_an_active_user_of_the_same_institution_and_not_yourself()
    {
        var s = await SeedAsync();
        var service = ServiceFor(s.Options, s.InstitutionId, s.AdminId);

        Assert.Equal("user_not_found", (await service.IssueResetCodeAsync(s.OtherAdminId)).Error);
        Assert.Equal("user_not_found", (await service.IssueResetCodeAsync(Guid.NewGuid())).Error);
        Assert.Equal("cannot_modify_self", (await service.IssueResetCodeAsync(s.AdminId)).Error);

        await service.SetActiveAsync(s.TeacherId, false);

        Assert.Equal("user_inactive", (await service.IssueResetCodeAsync(s.TeacherId)).Error);
    }

    [Fact]
    public async Task Platform_users_cannot_manage_accounts()
    {
        var service = ServiceFor((await SeedAsync()).Options, null, Guid.NewGuid());

        await Assert.ThrowsAsync<TenantViolationException>(() => service.ListAsync(null, null, 0, 50));
        await Assert.ThrowsAsync<TenantViolationException>(() => service.CreateStaffAsync(new("maria.perez", RoleKeys.Teacher)));
        await Assert.ThrowsAsync<TenantViolationException>(() => service.IssueResetCodeAsync(Guid.NewGuid()));
    }
}
