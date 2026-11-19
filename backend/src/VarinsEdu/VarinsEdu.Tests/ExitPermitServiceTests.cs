using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Auditing;
using VarinsEdu.Infrastructure.Exits;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;
using VarinsEdu.Infrastructure.Seeding;
using VarinsEdu.Infrastructure.Students;
using Xunit;

namespace VarinsEdu.Tests;

public class ExitPermitServiceTests
{
    private static readonly string Secret = new('k', 48);

    // 10:00 in an institution whose time zone is UTC.
    private static readonly DateTimeOffset Start = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 17);

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
        Guid AnaId,
        Guid BetoId,
        Guid StaffId,
        Guid GuardId);

    private static DbContextOptions<AppDbContext> NewOptions() =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static User NewUser(Guid id, Guid institutionId, string username) => new()
    {
        Id = id,
        InstitutionId = institutionId,
        Username = username,
        PasswordHash = "x",
        CreatedAt = Start
    };

    // One institution (time zone UTC) with Ana (group 10-1) and Beto (no group), a staff member and a gate guard.
    private static async Task<World> SeedAsync(
        DbContextOptions<AppDbContext>? options = null,
        FakeClock? clock = null,
        string slug = "demo-a",
        Guid? institutionId = null)
    {
        options ??= NewOptions();
        clock ??= new FakeClock(Start);

        var inst = institutionId ?? Guid.NewGuid();
        var group = Guid.NewGuid();
        var ana = Guid.NewGuid();
        var beto = Guid.NewGuid();
        var staff = Guid.NewGuid();
        var guard = Guid.NewGuid();

        using var db = new AppDbContext(options, new PlatformTenant());

        db.Institutions.Add(new Institution { Id = inst, Name = "Colegio " + slug, Slug = slug, TimeZone = "UTC" });
        db.Groups.Add(new Group { Id = group, InstitutionId = inst, Name = "10-1" });
        db.People.AddRange(
            new Person { Id = ana, InstitutionId = inst, IdentificationNumber = "111111111", FirstName = "Ana", LastName = "Prueba Uno" },
            new Person { Id = beto, InstitutionId = inst, IdentificationNumber = "222222222", FirstName = "Beto", LastName = "Prueba Dos" });
        db.StudentProfiles.AddRange(
            new StudentProfile { PersonId = ana, InstitutionId = inst, GroupId = group },
            new StudentProfile { PersonId = beto, InstitutionId = inst });
        db.Users.AddRange(NewUser(staff, inst, $"staff@{slug}"), NewUser(guard, inst, $"guard@{slug}"));

        await db.SaveChangesAsync();

        return new World(options, clock, inst, ana, beto, staff, guard);
    }

    // A new card code every time, like the student's phone does.
    private static string Card(World w, Guid personId, Guid? institutionId = null) =>
        new CardTokenService(Secret, w.Clock).Issue(personId, institutionId ?? w.InstitutionId).Token;

    private static ExitPermitService ServiceFor(World w, Guid? institutionId, Guid? userId)
    {
        var tenant = new FakeTenant(institutionId);
        var db = new AppDbContext(w.Options, tenant);
        var user = new FakeUser(userId);
        var suppression = new AuditSuppression();

        var cards = new CardScanService(db, tenant, user, new CardTokenService(Secret, w.Clock), suppression, w.Clock);

        return new ExitPermitService(db, tenant, user, cards, suppression, w.Clock);
    }

    // A new service every time: each one has its own database context, like each request does.
    private static ExitPermitService StaffOf(World w) => ServiceFor(w, w.InstitutionId, w.StaffId);

    private static ExitPermitService GuardOf(World w) => ServiceFor(w, w.InstitutionId, w.GuardId);

    private static AppDbContext Reader(DbContextOptions<AppDbContext> options) =>
        new(options, new PlatformTenant());

    // Today 09:00 to 11:00 unless said otherwise: valid at the 10:00 the clock starts at.
    private static ExitPermitInput Window(
        string cedula,
        string reason = "Cita médica",
        DateOnly? date = null,
        TimeOnly? start = null,
        TimeOnly? end = null) =>
        new(cedula, date ?? Today, start ?? new TimeOnly(9, 0), end ?? new TimeOnly(11, 0), reason);

    private static async Task<Guid> CreateAsync(World w, ExitPermitInput input) =>
        (await StaffOf(w).CreateAsync(input)).Value!.Id;

    [Fact]
    public async Task Creating_a_permit_stores_it_active_for_the_student()
    {
        var w = await SeedAsync();

        var created = await StaffOf(w).CreateAsync(Window("1-1111-1111"));

        Assert.Null(created.Error);

        using var db = Reader(w.Options);
        var stored = await db.ExitPermits.SingleAsync();

        Assert.Equal(created.Value!.Id, stored.Id);
        Assert.Equal(w.AnaId, stored.PersonId);
        Assert.Equal(w.InstitutionId, stored.InstitutionId);
        Assert.Equal(ExitPermitStatuses.Active, stored.Status);
        Assert.Equal(Today, stored.ServiceDate);
        Assert.Equal(new TimeOnly(9, 0), stored.FromTime);
        Assert.Equal(new TimeOnly(11, 0), stored.ToTime);
        Assert.Equal("Cita médica", stored.Reason);
        Assert.Equal(w.StaffId, stored.CreatedByUserId);
        Assert.Equal(Start, stored.CreatedAt);
    }

    [Fact]
    public async Task Invalid_permits_are_rejected()
    {
        var w = await SeedAsync();
        var staff = StaffOf(w);

        Assert.Equal("invalid_exit_reason", (await staff.CreateAsync(Window("111111111", reason: " "))).Error);
        Assert.Equal("invalid_exit_reason", (await staff.CreateAsync(Window("111111111", reason: new string('x', 201)))).Error);

        Assert.Equal("invalid_exit_date", (await staff.CreateAsync(new("111111111", null, new TimeOnly(9, 0), new TimeOnly(11, 0), "x"))).Error);
        Assert.Equal("invalid_exit_date", (await staff.CreateAsync(Window("111111111", date: Today.AddDays(-1)))).Error);
        Assert.Equal("invalid_exit_date", (await staff.CreateAsync(Window("111111111", date: Today.AddDays(61)))).Error);

        Assert.Equal("invalid_exit_window", (await staff.CreateAsync(new("111111111", Today, null, new TimeOnly(11, 0), "x"))).Error);
        Assert.Equal("invalid_exit_window", (await staff.CreateAsync(Window("111111111", start: new TimeOnly(11, 0), end: new TimeOnly(9, 0)))).Error);
        Assert.Equal("invalid_exit_window", (await staff.CreateAsync(Window("111111111", start: new TimeOnly(9, 0), end: new TimeOnly(9, 0)))).Error);

        // A window that already ended today, down to the minute.
        Assert.Equal("invalid_exit_window", (await staff.CreateAsync(Window("111111111", start: new TimeOnly(8, 0), end: new TimeOnly(10, 0)))).Error);

        Assert.Equal("student_not_found", (await staff.CreateAsync(Window("0-0000-0000"))).Error);
        Assert.Equal("student_not_found", (await staff.CreateAsync(Window(" "))).Error);

        using (var db = Reader(w.Options))
        {
            Assert.Empty(await db.ExitPermits.ToListAsync());
        }

        // The limits themselves are allowed.
        Assert.Null((await staff.CreateAsync(Window("111111111", date: Today.AddDays(1)))).Error);
        Assert.Null((await staff.CreateAsync(Window("111111111", date: Today.AddDays(60)))).Error);
    }

    [Fact]
    public async Task The_preview_shows_only_permits_valid_now_and_does_not_use_the_card_up()
    {
        var w = await SeedAsync();
        await CreateAsync(w, Window("111111111"));
        var token = Card(w, w.AnaId);

        var preview = await GuardOf(w).PreviewAsync(token);

        Assert.Null(preview.Error);
        Assert.Equal("Ana", preview.Value!.Student.FirstName);
        Assert.Equal("10-1", preview.Value.Student.Group);

        var option = Assert.Single(preview.Value.Permits);

        Assert.Equal(new TimeOnly(9, 0), option.FromTime);
        Assert.Equal(new TimeOnly(11, 0), option.ToTime);

        // The same code can be previewed again: it is only used up when the exit is registered.
        Assert.Null((await GuardOf(w).PreviewAsync(token)).Error);

        using (var db = Reader(w.Options))
        {
            Assert.Empty(await db.CardScans.ToListAsync());
            Assert.Equal(ExitPermitStatuses.Active, (await db.ExitPermits.SingleAsync()).Status);
        }

        // A student without a permit.
        Assert.Equal("no_valid_permit", (await GuardOf(w).PreviewAsync(Card(w, w.BetoId))).Error);

        // At 11:00 the window [09:00, 11:00) is over, and at 08:00 it has not started.
        w.Clock.Now = Start.AddHours(1);
        Assert.Equal("no_valid_permit", (await GuardOf(w).PreviewAsync(Card(w, w.AnaId))).Error);

        w.Clock.Now = Start.AddHours(-2);
        Assert.Equal("no_valid_permit", (await GuardOf(w).PreviewAsync(Card(w, w.AnaId))).Error);
    }

    [Fact]
    public async Task Registering_uses_the_permit_and_the_card_up()
    {
        var w = await SeedAsync();
        var id = await CreateAsync(w, Window("111111111"));
        var token = Card(w, w.AnaId);

        var result = await GuardOf(w).RegisterAsync(token, id);

        Assert.Null(result.Error);
        Assert.Equal("Ana", result.Value!.Student.FirstName);
        Assert.Equal(Start, result.Value.At);

        using (var db = Reader(w.Options))
        {
            var permit = await db.ExitPermits.SingleAsync();

            Assert.Equal(ExitPermitStatuses.Used, permit.Status);
            Assert.Equal(Start, permit.UsedAt);
            Assert.Equal(w.GuardId, permit.UsedByUserId);
            Assert.Equal(1, await db.CardScans.CountAsync());
        }

        // The same card code again.
        Assert.Equal("card_already_used", (await GuardOf(w).RegisterAsync(token, id)).Error);

        // A new card code, but the permit is already used.
        Assert.Equal("exit_permit_unavailable", (await GuardOf(w).RegisterAsync(Card(w, w.AnaId), id)).Error);
        Assert.Equal("no_valid_permit", (await GuardOf(w).PreviewAsync(Card(w, w.AnaId))).Error);

        using var check = Reader(w.Options);
        Assert.Equal(1, await check.CardScans.CountAsync());
    }

    [Fact]
    public async Task A_refusal_does_not_use_the_card_up()
    {
        var w = await SeedAsync();
        var cancelled = await CreateAsync(w, Window("111111111"));
        Assert.Null((await StaffOf(w).CancelAsync(cancelled)).Error);

        var token = Card(w, w.AnaId);

        Assert.Equal("exit_permit_unavailable", (await GuardOf(w).RegisterAsync(token, cancelled)).Error);

        using (var db = Reader(w.Options))
        {
            Assert.Empty(await db.CardScans.ToListAsync());
        }

        // The same card code still works with a valid permit.
        var valid = await CreateAsync(w, Window("111111111"));

        Assert.Null((await GuardOf(w).RegisterAsync(token, valid)).Error);
    }

    [Fact]
    public async Task A_permit_of_another_student_looks_like_one_that_does_not_exist()
    {
        var w = await SeedAsync();
        var anaPermit = await CreateAsync(w, Window("111111111"));

        Assert.Equal("exit_permit_not_found", (await GuardOf(w).RegisterAsync(Card(w, w.BetoId), anaPermit)).Error);
        Assert.Equal("exit_permit_not_found", (await GuardOf(w).RegisterAsync(Card(w, w.AnaId), Guid.NewGuid())).Error);

        using var db = Reader(w.Options);
        Assert.Empty(await db.CardScans.ToListAsync());
        Assert.Equal(ExitPermitStatuses.Active, (await db.ExitPermits.SingleAsync()).Status);
    }

    [Fact]
    public async Task Only_active_permits_can_be_cancelled()
    {
        var w = await SeedAsync();

        var id = await CreateAsync(w, Window("111111111"));

        Assert.Null((await StaffOf(w).CancelAsync(id)).Error);

        using (var db = Reader(w.Options))
        {
            var permit = await db.ExitPermits.SingleAsync();

            Assert.Equal(ExitPermitStatuses.Cancelled, permit.Status);
            Assert.Equal(w.StaffId, permit.CancelledByUserId);
            Assert.Equal(Start, permit.CancelledAt);
        }

        Assert.Equal("exit_permit_not_cancellable", (await StaffOf(w).CancelAsync(id)).Error);
        Assert.Equal("exit_permit_not_found", (await StaffOf(w).CancelAsync(Guid.NewGuid())).Error);

        // A permit that was already used cannot be cancelled either.
        var used = await CreateAsync(w, Window("222222222"));
        Assert.Null((await GuardOf(w).RegisterAsync(Card(w, w.BetoId), used)).Error);

        Assert.Equal("exit_permit_not_cancellable", (await StaffOf(w).CancelAsync(used)).Error);
    }

    [Fact]
    public async Task The_list_shows_the_status_of_each_permit_and_only_this_institution()
    {
        var options = NewOptions();
        var w = await SeedAsync(options);
        var other = await SeedAsync(options, w.Clock, "demo-b");
        var staff = StaffOf(w);

        var used = (await staff.CreateAsync(Window("111111111"))).Value!.Id;
        var expiring = (await staff.CreateAsync(Window("222222222", end: new TimeOnly(10, 30)))).Value!.Id;
        var tomorrow = (await staff.CreateAsync(
            Window("111111111", date: Today.AddDays(1), start: new TimeOnly(8, 0), end: new TimeOnly(9, 0)))).Value!.Id;
        var cancelled = (await staff.CreateAsync(Window("222222222", start: new TimeOnly(9, 30), end: new TimeOnly(11, 30)))).Value!.Id;

        Assert.Null((await GuardOf(w).RegisterAsync(Card(w, w.AnaId), used)).Error);
        Assert.Null((await staff.CancelAsync(cancelled)).Error);

        await ServiceFor(other, other.InstitutionId, other.StaffId).CreateAsync(Window("111111111"));

        // 10:45: the permit that ended at 10:30 is now expired.
        w.Clock.Now = Start.AddMinutes(45);

        var all = await staff.ListAsync(null, null, null, 0, 50);

        Assert.Equal(4, all.Total);
        Assert.Equal(Today.AddDays(1), all.Items.Last().Date);
        Assert.All(all.Items, i => Assert.Equal("staff@demo-a", i.CreatedBy));

        var usedItem = Assert.Single((await staff.ListAsync(null, "used", null, 0, 50)).Items);

        Assert.Equal(used, usedItem.Id);
        Assert.Equal("Ana", usedItem.FirstName);
        Assert.Equal("10-1", usedItem.Group);
        Assert.Equal("guard@demo-a", usedItem.UsedBy);
        Assert.NotNull(usedItem.UsedAt);

        Assert.Equal(cancelled, Assert.Single((await staff.ListAsync(null, "cancelled", null, 0, 50)).Items).Id);
        Assert.Equal(expiring, Assert.Single((await staff.ListAsync(null, "expired", null, 0, 50)).Items).Id);
        Assert.Equal(tomorrow, Assert.Single((await staff.ListAsync(null, "active", null, 0, 50)).Items).Id);
        Assert.Equal(tomorrow, Assert.Single((await staff.ListAsync(Today.AddDays(1), null, null, 0, 50)).Items).Id);
        Assert.Equal(2, (await staff.ListAsync(null, null, "beto", 0, 50)).Total);

        Assert.Equal(1, (await ServiceFor(other, other.InstitutionId, other.StaffId).ListAsync(null, null, null, 0, 50)).Total);
    }

    [Fact]
    public async Task Platform_users_cannot_manage_exit_permits()
    {
        var w = await SeedAsync();
        var service = ServiceFor(w, null, w.StaffId);

        await Assert.ThrowsAsync<TenantViolationException>(() => service.ListAsync(null, null, null, 0, 50));
        await Assert.ThrowsAsync<TenantViolationException>(() => service.CreateAsync(Window("111111111")));
        await Assert.ThrowsAsync<TenantViolationException>(() => service.CancelAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<TenantViolationException>(() => service.PreviewAsync(Card(w, w.AnaId)));
        await Assert.ThrowsAsync<TenantViolationException>(() => service.RegisterAsync(Card(w, w.AnaId), Guid.NewGuid()));
    }

    [Fact]
    public async Task Permit_changes_are_audited_without_the_reason()
    {
        var institutionId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditSaveChangesInterceptor(
                new FakeTenant(institutionId),
                new FakeUser(null),
                TimeProvider.System,
                new AuditSuppression()))
            .Options;

        var w = await SeedAsync(options, null, "demo-a", institutionId);

        var id = await CreateAsync(w, Window("111111111", "Cita en el hospital"));
        Assert.Null((await StaffOf(w).CancelAsync(id)).Error);

        using var db = Reader(options);
        var logs = await db.AuditLogs.Where(a => a.EntityType == "ExitPermit").ToListAsync();

        Assert.Equal(2, logs.Count);
        Assert.All(logs, a => Assert.DoesNotContain("hospital", a.Metadata));
        Assert.Contains(logs, a => a.Action == "ExitPermit.updated" && a.Metadata!.Contains("cancelled"));
        Assert.Contains(logs, a => a.Metadata!.Contains("[redacted]"));
    }
}
