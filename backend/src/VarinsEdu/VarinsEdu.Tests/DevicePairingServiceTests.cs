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

public class DevicePairingServiceTests
{
    private const string Code = "ABCD-2345";
    private const string WrongCode = "QQQQ-9999";

    private static readonly DateTimeOffset Start = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly ResetCodeHasher ResetHasher = new(new string('k', 48));
    private static readonly RefreshTokenHasher RefreshHasher = new(new string('k', 48));

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

    private sealed record World(DbContextOptions<AppDbContext> Options, FakeClock Clock, Guid InstitutionId, Guid DeviceId);

    private static User NewUser(Guid institutionId, string username, bool device, bool active = true) => new()
    {
        Id = Guid.NewGuid(),
        InstitutionId = institutionId,
        Username = username,
        PasswordHash = "x",
        IsActive = active,
        IsDevice = device,
        CreatedAt = Start
    };

    private static PasswordResetCode NewCode(Guid institutionId, Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        InstitutionId = institutionId,
        UserId = userId,
        CodeHash = ResetHasher.Hash(Code),
        ExpiresAt = Start + DevicePairingService.CodeLifetime,
        CreatedAt = Start
    };

    // "screen@demo-a" (a device with a code), "pedro@demo-a" (staff with a code), "off@demo-a" (a deactivated
    // device with a code) and "nocode@demo-a" (a device without a code).
    private static async Task<World> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var inst = Guid.NewGuid();

        var role = new Role { Id = Guid.NewGuid(), InstitutionId = inst, Key = RoleKeys.CafeteriaDisplay, Name = "display", IsSystem = true };
        role.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionKey = PermissionKeys.AccessDisplay });

        var screen = NewUser(inst, "screen@demo-a", device: true);
        screen.UserRoles.Add(new UserRole { UserId = screen.Id, RoleId = role.Id });

        var staff = NewUser(inst, "pedro@demo-a", device: false);
        var off = NewUser(inst, "off@demo-a", device: true, active: false);
        var noCode = NewUser(inst, "nocode@demo-a", device: true);

        using var db = new AppDbContext(options, new PlatformTenant());

        db.Institutions.Add(new Institution { Id = inst, Name = "Colegio A", Slug = "demo-a" });
        db.Roles.Add(role);
        db.Users.AddRange(screen, staff, off, noCode);
        db.PasswordResetCodes.AddRange(NewCode(inst, screen.Id), NewCode(inst, staff.Id), NewCode(inst, off.Id));

        await db.SaveChangesAsync();

        return new World(options, new FakeClock(Start), inst, screen.Id);
    }

    private static RefreshTokenService Sessions(World w) =>
        new(w.Options, RefreshHasher, new SessionPolicy(), new FakeUser(), new AuditSuppression(), w.Clock);

    private static DevicePairingService ServiceFor(World w) =>
        new(w.Options, ResetHasher, Sessions(w), new FakeUser(), w.Clock);

    private static AppDbContext Reader(World w) => new(w.Options, new PlatformTenant());

    [Fact]
    public async Task A_valid_code_pairs_the_device_with_a_long_persistent_session()
    {
        var w = await SeedAsync();

        var result = await ServiceFor(w).PairAsync("Screen@Demo-A", Code);

        Assert.True(result.Succeeded);
        Assert.True(result.User!.IsDevice);
        Assert.Equal(new[] { PermissionKeys.AccessDisplay }, result.User.Permissions);
        Assert.True(result.Session!.Persistent);
        Assert.Equal(Start.AddDays(90), result.Session.ExpiresAt);

        using var db = Reader(w);
        Assert.NotNull((await db.PasswordResetCodes.SingleAsync(c => c.UserId == w.DeviceId)).UsedAt);
    }

    [Fact]
    public async Task The_code_works_only_once()
    {
        var w = await SeedAsync();

        Assert.True((await ServiceFor(w).PairAsync("screen@demo-a", Code)).Succeeded);
        Assert.False((await ServiceFor(w).PairAsync("screen@demo-a", Code)).Succeeded);
    }

    [Fact]
    public async Task Pairing_again_ends_the_sessions_of_the_previous_screen()
    {
        var w = await SeedAsync();

        var first = (await ServiceFor(w).PairAsync("screen@demo-a", Code)).Session!;

        // The administrator issues a new code.
        using (var db = Reader(w))
        {
            var row = await db.PasswordResetCodes.SingleAsync(c => c.UserId == w.DeviceId);

            row.CodeHash = ResetHasher.Hash("WXYZ-6789");
            row.UsedAt = null;
            row.ExpiresAt = w.Clock.Now + DevicePairingService.CodeLifetime;
            await db.SaveChangesAsync();
        }

        var second = (await ServiceFor(w).PairAsync("screen@demo-a", "WXYZ-6789")).Session!;

        Assert.Equal(RefreshStatus.Invalid, (await Sessions(w).RefreshAsync(first.Token)).Status);
        Assert.Equal(RefreshStatus.Ok, (await Sessions(w).RefreshAsync(second.Token)).Status);
    }

    [Fact]
    public async Task Five_wrong_codes_lock_the_code()
    {
        var w = await SeedAsync();

        for (var i = 0; i < DevicePairingService.MaxFailedAttempts; i++)
        {
            Assert.False((await ServiceFor(w).PairAsync("screen@demo-a", WrongCode)).Succeeded);
        }

        Assert.False((await ServiceFor(w).PairAsync("screen@demo-a", Code)).Succeeded);

        using var db = Reader(w);
        var row = await db.PasswordResetCodes.SingleAsync(c => c.UserId == w.DeviceId);

        Assert.Equal(DevicePairingService.MaxFailedAttempts, row.FailedAttempts);
        Assert.Null(row.UsedAt);
    }

    [Fact]
    public async Task An_expired_code_is_refused()
    {
        var w = await SeedAsync();
        w.Clock.Now = Start + DevicePairingService.CodeLifetime + TimeSpan.FromMinutes(1);

        Assert.False((await ServiceFor(w).PairAsync("screen@demo-a", Code)).Succeeded);
    }

    [Fact]
    public async Task Only_active_devices_with_a_code_can_be_paired()
    {
        var w = await SeedAsync();

        // Staff with a valid code, an unknown name, a deactivated device and a device without a code.
        foreach (var username in new[] { "pedro@demo-a", "nobody@demo-a", "off@demo-a", "nocode@demo-a" })
        {
            Assert.False((await ServiceFor(w).PairAsync(username, Code)).Succeeded);
        }

        Assert.False((await ServiceFor(w).PairAsync("screen@demo-a", null)).Succeeded);
        Assert.False((await ServiceFor(w).PairAsync(null, Code)).Succeeded);
    }

    [Fact]
    public async Task Pairing_is_audited_and_failures_never_store_the_username()
    {
        var w = await SeedAsync();

        await ServiceFor(w).PairAsync("screen@demo-a", WrongCode);
        await ServiceFor(w).PairAsync("screen@demo-a", Code);

        using var db = Reader(w);

        var failed = Assert.Single(await db.AuditLogs.Where(a => a.Action == "auth.device.pair.failed").ToListAsync());
        Assert.Contains("wrong_code", failed.Metadata);
        Assert.DoesNotContain("screen", failed.Metadata);

        var paired = Assert.Single(await db.AuditLogs.Where(a => a.Action == "auth.device.paired").ToListAsync());
        Assert.Equal(w.DeviceId, paired.UserId);
        Assert.Equal(w.InstitutionId, paired.InstitutionId);
    }
}
