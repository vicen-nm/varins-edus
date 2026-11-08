using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;
using VarinsEdu.Infrastructure.Seeding;
using VarinsEdu.Infrastructure.Auditing;
using Xunit;

namespace VarinsEdu.Tests;

public class PasswordServiceTests
{
    private const string OldPassword = "old-password-123";
    private const string NewPassword = "Clave-Nueva-Segura-1";
    private const string StaffCode = "ABCD-2345";
    private const string StudentCode = "WXYZ-6789";
    private const string WrongCode = "QQQQ-9999";

    private static readonly DateTimeOffset Start = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly PasswordHasherService Hasher = new();
    private static readonly ResetCodeHasher ResetHasher = new(new string('k', 48));
    private static readonly RefreshTokenHasher RefreshHasher = new(new string('k', 48));
    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

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

    private sealed record World(
        DbContextOptions<AppDbContext> Options,
        FakeClock Clock,
        Guid InstitutionId,
        Guid StaffId,
        Guid StudentUserId);

    private static User NewUser(Guid? institutionId, string username, Guid? personId = null, bool active = true) => new()
    {
        Id = Guid.NewGuid(),
        InstitutionId = institutionId,
        PersonId = personId,
        Username = username,
        PasswordHash = Hasher.Hash(OldPassword),
        IsActive = active,
        CreatedAt = Start
    };

    private static PasswordResetCode NewCode(Guid institutionId, Guid userId, string code) => new()
    {
        Id = Guid.NewGuid(),
        InstitutionId = institutionId,
        UserId = userId,
        CodeHash = ResetHasher.Hash(code),
        ExpiresAt = Start + PasswordService.CodeLifetime,
        CreatedAt = Start
    };

    // "pedro@demo-a" (staff, locked, with a code), "111111111@demo-a" (student, with a code),
    // "inactive@demo-a" (with a code), "nocode@demo-a" and a platform user "root".
    private static async Task<World> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var inst = Guid.NewGuid();
        var person = new Person
        {
            Id = Guid.NewGuid(),
            InstitutionId = inst,
            IdentificationNumber = "111111111",
            FirstName = "Ana",
            LastName = "Prueba"
        };

        var staff = NewUser(inst, "pedro@demo-a");
        staff.FailedLoginAttempts = 3;
        staff.LockedUntil = Start.AddMinutes(10);

        var student = NewUser(inst, "111111111@demo-a", person.Id);
        var inactive = NewUser(inst, "inactive@demo-a", active: false);

        using var db = new AppDbContext(options, new PlatformTenant());

        db.Institutions.Add(new Institution { Id = inst, Name = "Colegio A", Slug = "demo-a" });
        db.People.Add(person);
        db.Users.AddRange(staff, student, inactive, NewUser(inst, "nocode@demo-a"), NewUser(null, "root"));
        db.PasswordResetCodes.AddRange(
            NewCode(inst, staff.Id, StaffCode),
            NewCode(inst, student.Id, StudentCode),
            NewCode(inst, inactive.Id, StaffCode));

        await db.SaveChangesAsync();

        return new World(options, new FakeClock(Start), inst, staff.Id, student.Id);
    }

    private static PasswordService ServiceFor(World w, Guid? userId = null) =>
        new(
            w.Options,
            new AppDbContext(w.Options, new FakeTenant(w.InstitutionId)),
            new FakeUser(userId),
            Hasher,
            ResetHasher,
            w.Clock,
            RefreshHasher);

    private static RefreshTokenService Sessions(World w) =>
        new(w.Options, RefreshHasher, new SessionPolicy(), new FakeUser(null), new AuditSuppression(), w.Clock);

    private static AppDbContext Reader(World w) => new(w.Options, new PlatformTenant());

    private static ResetPasswordCommand Reset(string username, string code, string password = NewPassword) =>
        new(username, code, password);

    [Fact]
    public async Task A_valid_code_sets_the_password_unlocks_the_account_and_works_only_once()
    {
        var w = await SeedAsync();

        var result = await ServiceFor(w).ResetAsync(Reset("Pedro@Demo-A", StaffCode));

        Assert.True(result.Succeeded);

        using (var db = Reader(w))
        {
            var user = await db.Users.SingleAsync(u => u.Username == "pedro@demo-a");

            Assert.True(Hasher.Verify(user.PasswordHash, NewPassword));
            Assert.False(Hasher.Verify(user.PasswordHash, OldPassword));
            Assert.Equal(0, user.FailedLoginAttempts);
            Assert.Null(user.LockedUntil);
            Assert.NotNull((await db.PasswordResetCodes.SingleAsync(c => c.UserId == user.Id)).UsedAt);
        }

        var again = await ServiceFor(w).ResetAsync(Reset("pedro@demo-a", StaffCode, "Otra-Clave-Larga-9"));

        Assert.Equal(PasswordError.Failed, again.Error);
    }

    [Fact]
    public async Task Wrong_codes_count_attempts_and_five_of_them_lock_the_code()
    {
        var w = await SeedAsync();

        for (var i = 0; i < PasswordService.MaxFailedCodeAttempts; i++)
        {
            Assert.Equal(PasswordError.Failed, (await ServiceFor(w).ResetAsync(Reset("pedro@demo-a", WrongCode))).Error);
        }

        Assert.Equal(PasswordError.Failed, (await ServiceFor(w).ResetAsync(Reset("pedro@demo-a", StaffCode))).Error);

        using var db = Reader(w);
        var user = await db.Users.SingleAsync(u => u.Username == "pedro@demo-a");
        var code = await db.PasswordResetCodes.SingleAsync(c => c.UserId == user.Id);

        Assert.Equal(PasswordService.MaxFailedCodeAttempts, code.FailedAttempts);
        Assert.True(Hasher.Verify(user.PasswordHash, OldPassword));
    }

    [Fact]
    public async Task An_expired_code_is_refused()
    {
        var w = await SeedAsync();
        w.Clock.Now = Start + PasswordService.CodeLifetime + TimeSpan.FromHours(1);

        Assert.Equal(PasswordError.Failed, (await ServiceFor(w).ResetAsync(Reset("pedro@demo-a", StaffCode))).Error);
    }

    [Fact]
    public async Task Weak_passwords_are_refused_without_using_an_attempt_and_staff_need_ten_characters()
    {
        var w = await SeedAsync();

        Assert.Equal(PasswordError.Weak, (await ServiceFor(w).ResetAsync(Reset("pedro@demo-a", StaffCode, "12345678"))).Error);
        Assert.Equal(PasswordError.Weak, (await ServiceFor(w).ResetAsync(Reset("pedro@demo-a", StaffCode, "pedro-clave-1"))).Error);

        // Nine characters: enough for a student, not for staff.
        Assert.Equal(PasswordError.Weak, (await ServiceFor(w).ResetAsync(Reset("pedro@demo-a", StaffCode, "Zx9Kq4mPa"))).Error);

        using (var db = Reader(w))
        {
            var code = await db.PasswordResetCodes.SingleAsync(c => c.UserId == w.StaffId);

            Assert.Equal(0, code.FailedAttempts);
            Assert.Null(code.UsedAt);
        }

        Assert.True((await ServiceFor(w).ResetAsync(Reset("111111111@demo-a", StudentCode, "Zx9Kq4mPa"))).Succeeded);
    }

    [Fact]
    public async Task Unknown_inactive_codeless_and_platform_users_all_fail_the_same_way()
    {
        var w = await SeedAsync();

        foreach (var username in new[] { "nobody@demo-a", "inactive@demo-a", "nocode@demo-a", "root" })
        {
            Assert.Equal(PasswordError.Failed, (await ServiceFor(w).ResetAsync(Reset(username, StaffCode))).Error);
        }
    }

    [Fact]
    public async Task Resets_are_audited_without_the_username()
    {
        var w = await SeedAsync();

        await ServiceFor(w).ResetAsync(Reset("pedro@demo-a", WrongCode));
        await ServiceFor(w).ResetAsync(Reset("pedro@demo-a", StaffCode));

        using var db = Reader(w);

        var failed = Assert.Single(await db.AuditLogs.Where(a => a.Action == "auth.password.reset.failed").ToListAsync());
        Assert.Contains("wrong_code", failed.Metadata);
        Assert.DoesNotContain("pedro", failed.Metadata);

        var succeeded = Assert.Single(await db.AuditLogs.Where(a => a.Action == "auth.password.reset.succeeded").ToListAsync());
        Assert.Equal(w.StaffId, succeeded.UserId);
    }

    [Fact]
    public async Task Changing_the_password_needs_the_current_one_and_a_strong_new_one()
    {
        var w = await SeedAsync();
        var change = (string current, string next) => ServiceFor(w, w.StudentUserId).ChangeAsync(new(current, next));

        Assert.Equal(PasswordError.InvalidCurrentPassword, (await change("not-the-password", NewPassword)).Error);

        using (var db = Reader(w))
        {
            Assert.Equal(1, (await db.Users.SingleAsync(u => u.Id == w.StudentUserId)).FailedLoginAttempts);
        }

        Assert.Equal(PasswordError.Weak, (await change(OldPassword, OldPassword)).Error);
        Assert.Equal(PasswordError.Weak, (await change(OldPassword, "12345678")).Error);

        Assert.True((await change(OldPassword, NewPassword)).Succeeded);

        using var check = Reader(w);
        var student = await check.Users.SingleAsync(u => u.Id == w.StudentUserId);

        Assert.True(Hasher.Verify(student.PasswordHash, NewPassword));
        Assert.Equal(0, student.FailedLoginAttempts);
    }

    [Fact]
    public async Task Wrong_current_passwords_lock_the_account_and_no_change_is_allowed_while_locked()
    {
        var w = await SeedAsync();
        var change = (string current, string next) => ServiceFor(w, w.StudentUserId).ChangeAsync(new(current, next));

        for (var i = 0; i < LoginPolicy.MaxFailedAttempts; i++)
        {
            await change("not-the-password", NewPassword);
        }

        Assert.Equal(PasswordError.InvalidCurrentPassword, (await change(OldPassword, NewPassword)).Error);

        using var db = Reader(w);
        var student = await db.Users.SingleAsync(u => u.Id == w.StudentUserId);

        Assert.Equal(Start + LoginPolicy.LockDuration, student.LockedUntil);
        Assert.True(Hasher.Verify(student.PasswordHash, OldPassword));
    }


    [Fact]
    public async Task Resetting_the_password_closes_every_session_of_the_account()
    {
        var w = await SeedAsync();
        var sessions = Sessions(w);
        var user = new AuthenticatedUser(w.StaffId, "pedro@demo-a", w.InstitutionId, []);

        await sessions.StartAsync(user, persistent: false);
        await sessions.StartAsync(user, persistent: true);

        Assert.True((await ServiceFor(w).ResetAsync(Reset("pedro@demo-a", StaffCode))).Succeeded);

        using var db = Reader(w);
        var rows = await db.RefreshTokens.Where(t => t.UserId == w.StaffId).ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.NotNull(r.RevokedAt));
    }

    [Fact]
    public async Task Changing_the_password_keeps_the_current_session_and_closes_the_others()
    {
        var w = await SeedAsync();
        var sessions = Sessions(w);
        var user = new AuthenticatedUser(w.StudentUserId, "111111111@demo-a", w.InstitutionId, []);

        var current = await sessions.StartAsync(user, persistent: false);
        var other = await sessions.StartAsync(user, persistent: false);

        var result = await ServiceFor(w, w.StudentUserId)
            .ChangeAsync(new ChangePasswordCommand(OldPassword, NewPassword, current.Token));

        Assert.True(result.Succeeded);

        using var db = Reader(w);
        var kept = await db.RefreshTokens.SingleAsync(t => t.TokenHash == RefreshHasher.Hash(current.Token));
        var closed = await db.RefreshTokens.SingleAsync(t => t.TokenHash == RefreshHasher.Hash(other.Token));

        Assert.Null(kept.RevokedAt);
        Assert.NotNull(closed.RevokedAt);
    }
}
