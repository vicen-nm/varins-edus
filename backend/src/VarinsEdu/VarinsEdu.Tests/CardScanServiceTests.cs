using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Auditing;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;
using VarinsEdu.Infrastructure.Seeding;
using VarinsEdu.Infrastructure.Students;
using Xunit;

namespace VarinsEdu.Tests;

public class CardScanServiceTests
{
    private static readonly string Secret = new('k', 48);
    private static readonly DateTimeOffset Start = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

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
        Guid PersonId,
        Guid StaffId);

    // One institution with Ana (group 10-1) and a staff member who scans.
    private static async Task<World> SeedAsync(string status = StudentStatuses.Active)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var inst = Guid.NewGuid();
        var group = Guid.NewGuid();
        var person = Guid.NewGuid();

        using var db = new AppDbContext(options, new PlatformTenant());

        db.Institutions.Add(new Institution { Id = inst, Name = "Colegio A", Slug = "demo-a" });
        db.Groups.Add(new Group { Id = group, InstitutionId = inst, Name = "10-1" });
        db.People.Add(new Person
        {
            Id = person,
            InstitutionId = inst,
            IdentificationNumber = "111111111",
            FirstName = "Ana",
            LastName = "Prueba Uno"
        });
        db.StudentProfiles.Add(new StudentProfile { PersonId = person, InstitutionId = inst, GroupId = group, Status = status });

        await db.SaveChangesAsync();

        return new World(options, new FakeClock(Start), inst, person, Guid.NewGuid());
    }

    private static CardTokenService Tokens(World w) => new(Secret, w.Clock);

    private static string Card(World w, Guid? institutionId = null) =>
        Tokens(w).Issue(w.PersonId, institutionId ?? w.InstitutionId).Token;

    private static CardScanService ServiceFor(World w)
    {
        var tenant = new FakeTenant(w.InstitutionId);

        return new CardScanService(
            new AppDbContext(w.Options, tenant),
            tenant,
            new FakeUser(w.StaffId),
            Tokens(w),
            new AuditSuppression(),
            w.Clock);
    }

    private static AppDbContext Reader(World w) => new(w.Options, new PlatformTenant());

    [Fact]
    public async Task A_valid_card_identifies_the_student_and_records_the_scan()
    {
        var w = await SeedAsync();

        var result = await ServiceFor(w).ScanAsync(Card(w));

        Assert.Null(result.Error);
        Assert.Equal("Ana", result.Value!.FirstName);
        Assert.Equal("Prueba Uno", result.Value.LastName);
        Assert.Equal("10-1", result.Value.Group);

        using var db = Reader(w);
        var scan = await db.CardScans.SingleAsync();

        Assert.Equal(w.PersonId, scan.PersonId);
        Assert.Equal(w.InstitutionId, scan.InstitutionId);
        Assert.Equal(w.StaffId, scan.ScannedByUserId);
        Assert.Equal(Start, scan.ScannedAt);
        Assert.False(string.IsNullOrEmpty(scan.Nonce));
    }

    [Fact]
    public async Task A_card_code_works_only_once_and_the_second_try_is_audited()
    {
        var w = await SeedAsync();
        var token = Card(w);

        Assert.Null((await ServiceFor(w).ScanAsync(token)).Error);
        Assert.Equal("card_already_used", (await ServiceFor(w).ScanAsync(token)).Error);

        using var db = Reader(w);
        Assert.Equal(1, await db.CardScans.CountAsync());

        var audit = Assert.Single(await db.AuditLogs.Where(a => a.Action == "card.scan.reused").ToListAsync());

        Assert.Equal(w.StaffId, audit.UserId);
        Assert.Equal(w.InstitutionId, audit.InstitutionId);
        Assert.Equal(w.PersonId.ToString(), audit.EntityId);
        Assert.DoesNotContain("111111111", audit.Metadata ?? string.Empty);
    }

    [Fact]
    public async Task A_new_card_code_of_the_same_student_works()
    {
        var w = await SeedAsync();

        Assert.Null((await ServiceFor(w).ScanAsync(Card(w))).Error);
        Assert.Null((await ServiceFor(w).ScanAsync(Card(w))).Error);

        using var db = Reader(w);
        Assert.Equal(2, await db.CardScans.CountAsync());
    }

    [Fact]
    public async Task An_expired_card_is_refused_and_nothing_is_recorded()
    {
        var w = await SeedAsync();
        var token = Card(w);

        w.Clock.Now = Start + CardTokenService.Lifetime + TimeSpan.FromSeconds(1);

        Assert.Equal("expired_card", (await ServiceFor(w).ScanAsync(token)).Error);

        using var db = Reader(w);
        Assert.Empty(await db.CardScans.ToListAsync());
    }

    [Fact]
    public async Task Garbage_and_cards_of_other_institutions_are_refused_the_same_way()
    {
        var w = await SeedAsync();
        var service = ServiceFor(w);

        Assert.Equal("invalid_card", (await service.ScanAsync(null)).Error);
        Assert.Equal("invalid_card", (await service.ScanAsync("not-a-card")).Error);
        Assert.Equal("invalid_card", (await service.ScanAsync(Card(w, institutionId: Guid.NewGuid()))).Error);

        using var db = Reader(w);
        Assert.Empty(await db.CardScans.ToListAsync());
    }

    [Fact]
    public async Task A_student_who_is_not_active_is_refused_and_the_token_is_not_used_up()
    {
        var w = await SeedAsync(status: "inactive");
        var token = Card(w);

        Assert.Equal("card_student_unavailable", (await ServiceFor(w).ScanAsync(token)).Error);

        using (var db = Reader(w))
        {
            Assert.Empty(await db.CardScans.ToListAsync());

            var profile = await db.StudentProfiles.SingleAsync();
            profile.Status = StudentStatuses.Active;
            await db.SaveChangesAsync();
        }

        // The same token still works: the refusal did not consume it.
        Assert.Null((await ServiceFor(w).ScanAsync(token)).Error);
    }

    [Fact]
    public async Task Platform_users_cannot_scan_cards()
    {
        var w = await SeedAsync();
        var tenant = new FakeTenant(null);

        var service = new CardScanService(
            new AppDbContext(w.Options, tenant),
            tenant,
            new FakeUser(w.StaffId),
            Tokens(w),
            new AuditSuppression(),
            w.Clock);

        await Assert.ThrowsAsync<TenantViolationException>(() => service.ScanAsync(Card(w)));
    }
}
