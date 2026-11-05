using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Infrastructure.Auditing;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;
using VarinsEdu.Infrastructure.Seeding;
using Xunit;

namespace VarinsEdu.Tests;

public class LoginLockoutTests
{
    private const string Password = "a-long-local-password";
    private const string Wrong = "not-the-password";

    private static readonly DateTimeOffset Start = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly PasswordHasherService Hasher = new();

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

    private sealed record World(DbContextOptions<AppDbContext> Options, FakeClock Clock, AuditSuppression Suppression);

    private static User NewUser(string username, Guid? institutionId) => new()
    {
        Id = Guid.NewGuid(),
        InstitutionId = institutionId,
        Username = username,
        PasswordHash = Hasher.Hash(Password),
        IsActive = true,
        CreatedAt = Start
    };

    // "ana" belongs to an active institution and "root" is a platform user.
    private static async Task<World> SeedAsync(bool audited = false)
    {
        var clock = new FakeClock(Start);
        var suppression = new AuditSuppression();

        var builder = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString());

        if (audited)
        {
            builder.AddInterceptors(new AuditSaveChangesInterceptor(new NoTenant(), new FakeUser(), clock, suppression));
        }

        var options = builder.Options;
        var institutionId = Guid.NewGuid();

        using var db = new AppDbContext(options, new PlatformTenant());
        db.Institutions.Add(new Institution { Id = institutionId, Name = "Active", Slug = "active", IsActive = true });
        db.Users.AddRange(NewUser("ana", institutionId), NewUser("root", null));
        await db.SaveChangesAsync();

        return new World(options, clock, suppression);
    }

    private static AuthService ServiceFor(World w) =>
        new(new AppDbContext(w.Options, new NoTenant()), Hasher, new FakeUser(), w.Clock, w.Options, w.Suppression);

    private static AppDbContext Reader(World w) => new(w.Options, new PlatformTenant());

    private static async Task FailAsync(World w, string username, int times)
    {
        for (var i = 0; i < times; i++)
        {
            Assert.Null(await ServiceFor(w).ValidateCredentialsAsync(username, Wrong));
        }
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_password()
    {
        var w = await SeedAsync();

        await FailAsync(w, "ana", 5);

        Assert.Null(await ServiceFor(w).ValidateCredentialsAsync("ana", Password));

        using var db = Reader(w);
        var ana = await db.Users.SingleAsync(u => u.Username == "ana");

        Assert.Equal(Start + LoginPolicy.LockDuration, ana.LockedUntil);
    }

    [Fact]
    public async Task The_lock_ends_after_its_duration()
    {
        var w = await SeedAsync();

        await FailAsync(w, "ana", 5);
        w.Clock.Now = Start + LoginPolicy.LockDuration + TimeSpan.FromSeconds(1);

        Assert.NotNull(await ServiceFor(w).ValidateCredentialsAsync("ana", Password));

        using var db = Reader(w);
        var ana = await db.Users.SingleAsync(u => u.Username == "ana");

        Assert.Null(ana.LockedUntil);
        Assert.Equal(0, ana.FailedLoginAttempts);
    }

    [Fact]
    public async Task A_successful_login_resets_the_counter_and_records_the_time()
    {
        var w = await SeedAsync();

        await FailAsync(w, "ana", 2);
        Assert.NotNull(await ServiceFor(w).ValidateCredentialsAsync("ana", Password));

        using (var db = Reader(w))
        {
            var ana = await db.Users.SingleAsync(u => u.Username == "ana");

            Assert.Equal(0, ana.FailedLoginAttempts);
            Assert.Equal(Start, ana.LastLoginAt);
        }

        // The counter started over: four more failures do not lock the account.
        await FailAsync(w, "ana", 4);
        Assert.NotNull(await ServiceFor(w).ValidateCredentialsAsync("ana", Password));
    }

    [Fact]
    public async Task An_attempt_while_locked_does_not_extend_the_lock_and_is_audited()
    {
        var w = await SeedAsync();

        await FailAsync(w, "ana", 5);
        w.Clock.Now = Start.AddMinutes(10);

        Assert.Null(await ServiceFor(w).ValidateCredentialsAsync("ana", Wrong));

        using var db = Reader(w);
        var ana = await db.Users.SingleAsync(u => u.Username == "ana");
        var failures = await db.AuditLogs.Where(a => a.Action == "auth.login.failed").ToListAsync();

        Assert.Equal(Start + LoginPolicy.LockDuration, ana.LockedUntil);
        Assert.Contains(failures, a => a.Metadata!.Contains("\"locked\""));
    }

    [Fact]
    public async Task Platform_accounts_are_protected_too()
    {
        var w = await SeedAsync();

        await FailAsync(w, "root", 5);

        Assert.Null(await ServiceFor(w).ValidateCredentialsAsync("root", Password));

        using var db = Reader(w);
        Assert.NotNull((await db.Users.SingleAsync(u => u.Username == "root")).LockedUntil);
    }

    [Fact]
    public async Task Counting_attempts_does_not_fill_the_audit_log_with_user_updates()
    {
        var w = await SeedAsync(audited: true);

        await FailAsync(w, "ana", 5);

        using var db = Reader(w);
        var actions = await db.AuditLogs.Select(a => a.Action).ToListAsync();

        Assert.Equal(5, actions.Count(a => a == "auth.login.failed"));
        Assert.Equal(1, actions.Count(a => a == "auth.account.locked"));
        Assert.DoesNotContain("User.updated", actions);
    }
}
