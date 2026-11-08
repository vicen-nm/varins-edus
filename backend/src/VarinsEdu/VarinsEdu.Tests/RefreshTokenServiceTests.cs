using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Infrastructure.Auditing;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;
using VarinsEdu.Infrastructure.Seeding;
using Xunit;

namespace VarinsEdu.Tests;

public class RefreshTokenServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly RefreshTokenHasher Hasher = new(new string('k', 48));

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string? IpAddress => "203.0.113.7";
    }

    private sealed record World(
        DbContextOptions<AppDbContext> Options,
        FakeClock Clock,
        Guid InstitutionId,
        Guid InactiveInstitutionId,
        Guid AnaId,
        Guid OffId,
        Guid GhostId,
        Guid RootId);

    private static Role NewRole(Guid? institutionId, string key, string permission)
    {
        var role = new Role { Id = Guid.NewGuid(), InstitutionId = institutionId, Key = key, Name = key, IsSystem = true };
        role.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionKey = permission });

        return role;
    }

    private static User NewUser(Guid? institutionId, string username, bool active, Role? role)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            InstitutionId = institutionId,
            Username = username,
            PasswordHash = "x",
            IsActive = active,
            CreatedAt = Start
        };

        if (role is not null)
        {
            user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        }

        return user;
    }

    // "ana" (teacher) and "off" (deactivated) in an active institution, "ghost" in an inactive one,
    // and "root", a platform user.
    private static async Task<World> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var active = Guid.NewGuid();
        var inactive = Guid.NewGuid();

        var teacher = NewRole(active, RoleKeys.Teacher, PermissionKeys.StudentsView);
        var platform = NewRole(null, RoleKeys.PlatformAdmin, PermissionKeys.UsersManage);

        var ana = NewUser(active, "ana", true, teacher);
        var off = NewUser(active, "off", false, teacher);
        var ghost = NewUser(inactive, "ghost", true, null);
        var root = NewUser(null, "root", true, platform);

        using var db = new AppDbContext(options, new PlatformTenant());

        db.Institutions.AddRange(
            new Institution { Id = active, Name = "Active", Slug = "active", IsActive = true },
            new Institution { Id = inactive, Name = "Inactive", Slug = "inactive", IsActive = false });
        db.Roles.AddRange(teacher, platform);
        db.Users.AddRange(ana, off, ghost, root);

        await db.SaveChangesAsync();

        return new World(options, new FakeClock(Start), active, inactive, ana.Id, off.Id, ghost.Id, root.Id);
    }

    private static RefreshTokenService ServiceFor(World w) =>
        new(w.Options, Hasher, new SessionPolicy(), new FakeUser(), new AuditSuppression(), w.Clock);

    private static AppDbContext Reader(World w) => new(w.Options, new PlatformTenant());

    private static AuthenticatedUser AsAna(World w) => new(w.AnaId, "ana", w.InstitutionId, []);

    [Fact]
    public async Task Refreshing_rotates_the_token_and_stores_only_a_fingerprint()
    {
        var w = await SeedAsync();
        var service = ServiceFor(w);

        var first = await service.StartAsync(AsAna(w), persistent: false);
        w.Clock.Now = Start.AddMinutes(10);

        var result = await service.RefreshAsync(first.Token);

        Assert.Equal(RefreshStatus.Ok, result.Status);
        Assert.NotEqual(first.Token, result.Session!.Token);
        Assert.Equal("ana", result.User!.Username);
        Assert.Equal(new[] { PermissionKeys.StudentsView }, result.User.Permissions);

        using var db = Reader(w);
        var rows = await db.RefreshTokens.OrderBy(t => t.CreatedAt).ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.NotEqual(first.Token, r.TokenHash));
        Assert.All(rows, r => Assert.NotEqual(result.Session.Token, r.TokenHash));
        Assert.NotNull(rows[0].UsedAt);
        Assert.Null(rows[1].UsedAt);
        Assert.Equal(rows[0].FamilyId, rows[1].FamilyId);
    }

    [Fact]
    public async Task A_token_used_again_within_the_grace_period_is_a_conflict_and_revokes_nothing()
    {
        var w = await SeedAsync();
        var service = ServiceFor(w);

        var first = await service.StartAsync(AsAna(w), persistent: false);
        w.Clock.Now = Start.AddMinutes(1);
        var second = await service.RefreshAsync(first.Token);

        w.Clock.Now = Start.AddMinutes(1).AddSeconds(5);

        Assert.Equal(RefreshStatus.Conflict, (await service.RefreshAsync(first.Token)).Status);
        Assert.Equal(RefreshStatus.Ok, (await service.RefreshAsync(second.Session!.Token)).Status);

        using var db = Reader(w);
        Assert.All(await db.RefreshTokens.ToListAsync(), r => Assert.Null(r.RevokedAt));
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == "auth.refresh.reuse").ToListAsync());
    }

    [Fact]
    public async Task A_token_reused_after_the_grace_period_ends_the_whole_session_and_is_audited()
    {
        var w = await SeedAsync();
        var service = ServiceFor(w);

        var first = await service.StartAsync(AsAna(w), persistent: false);
        w.Clock.Now = Start.AddMinutes(1);
        var second = await service.RefreshAsync(first.Token);

        w.Clock.Now = Start.AddMinutes(2);

        Assert.Equal(RefreshStatus.Invalid, (await service.RefreshAsync(first.Token)).Status);

        // Even the newest token of the session is dead now.
        Assert.Equal(RefreshStatus.Invalid, (await service.RefreshAsync(second.Session!.Token)).Status);

        using var db = Reader(w);
        Assert.All(await db.RefreshTokens.ToListAsync(), r => Assert.NotNull(r.RevokedAt));

        var audit = Assert.Single(await db.AuditLogs.Where(a => a.Action == "auth.refresh.reuse").ToListAsync());
        Assert.Equal(w.AnaId, audit.UserId);
        Assert.Equal(w.InstitutionId, audit.InstitutionId);
        Assert.Contains("reuse", audit.Metadata);
    }

    [Fact]
    public async Task Sessions_end_when_idle_and_at_the_absolute_limit()
    {
        var w = await SeedAsync();
        var service = ServiceFor(w);

        // Idle: seven days without using it.
        var idle = await service.StartAsync(AsAna(w), persistent: true);
        w.Clock.Now = Start.AddDays(7).AddMinutes(1);

        Assert.Equal(RefreshStatus.Invalid, (await service.RefreshAsync(idle.Token)).Status);

        // Used every six days, it still ends 30 days after sign-in.
        w.Clock.Now = Start;
        var token = (await service.StartAsync(AsAna(w), persistent: true)).Token;

        foreach (var day in new[] { 6, 12, 18, 24, 29 })
        {
            w.Clock.Now = Start.AddDays(day);

            var renewed = await service.RefreshAsync(token);

            Assert.Equal(RefreshStatus.Ok, renewed.Status);
            token = renewed.Session!.Token;

            if (day == 29)
            {
                Assert.Equal(Start.AddDays(30), renewed.Session.ExpiresAt);
            }
        }

        w.Clock.Now = Start.AddDays(30).AddMinutes(1);

        Assert.Equal(RefreshStatus.Invalid, (await service.RefreshAsync(token)).Status);
    }

    [Fact]
    public async Task Deactivated_users_and_inactive_institutions_cannot_refresh()
    {
        var w = await SeedAsync();
        var service = ServiceFor(w);

        var off = await service.StartAsync(new AuthenticatedUser(w.OffId, "off", w.InstitutionId, []), false);
        var ghost = await service.StartAsync(new AuthenticatedUser(w.GhostId, "ghost", w.InactiveInstitutionId, []), false);

        Assert.Equal(RefreshStatus.Invalid, (await service.RefreshAsync(off.Token)).Status);
        Assert.Equal(RefreshStatus.Invalid, (await service.RefreshAsync(ghost.Token)).Status);
    }

    [Fact]
    public async Task Logging_out_ends_the_session_and_is_audited()
    {
        var w = await SeedAsync();
        var service = ServiceFor(w);

        var session = await service.StartAsync(AsAna(w), persistent: false);

        await service.LogoutAsync(session.Token);

        Assert.Equal(RefreshStatus.Invalid, (await service.RefreshAsync(session.Token)).Status);

        using var db = Reader(w);
        var audit = Assert.Single(await db.AuditLogs.Where(a => a.Action == "auth.logout").ToListAsync());
        Assert.Equal(w.AnaId, audit.UserId);

        // An unknown token does nothing and does not fail.
        await service.LogoutAsync("not-a-token");
        await service.LogoutAsync(null);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-real-token")]
    public async Task Garbage_tokens_are_invalid(string? token)
    {
        var w = await SeedAsync();

        Assert.Equal(RefreshStatus.Invalid, (await ServiceFor(w).RefreshAsync(token)).Status);
    }

    [Fact]
    public async Task Platform_users_can_refresh_and_the_persistent_flag_survives_the_rotation()
    {
        var w = await SeedAsync();
        var service = ServiceFor(w);

        var session = await service.StartAsync(new AuthenticatedUser(w.RootId, "root", null, []), persistent: true);
        w.Clock.Now = Start.AddHours(1);

        var result = await service.RefreshAsync(session.Token);

        Assert.Equal(RefreshStatus.Ok, result.Status);
        Assert.True(session.Persistent);
        Assert.True(result.Session!.Persistent);
        Assert.True(result.User!.IsPlatform);
        Assert.Equal(new[] { PermissionKeys.UsersManage }, result.User.Permissions);
    }
}
