using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Seeding;
using VarinsEdu.Infrastructure.Summary;
using Xunit;

namespace VarinsEdu.Tests;

public class SummaryServiceTests
{
    // 06:00 in Costa Rica (UTC-6): local midnight was 06:00 UTC.
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 19);
    private static readonly DateOnly Yesterday = new(2026, 11, 18);

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeTenant(Guid? institutionId) : ICurrentTenant
    {
        public Guid? InstitutionId { get; } = institutionId;
        public bool IsPlatformScope => false;
    }

    private static readonly SummaryScope Everything = new(true, true, true, true, true, true, true);

    private static SummaryService ServiceFor(DbContextOptions<AppDbContext> options, Guid? institutionId)
    {
        var tenant = new FakeTenant(institutionId);

        return new SummaryService(new AppDbContext(options, tenant), tenant, new FakeClock(Now));
    }

    // Institution A (Costa Rica time) with data of today, of yesterday and of long ago, and institution B with noise.
    private static async Task<(DbContextOptions<AppDbContext> Options, Guid Institution)> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var inst = Guid.NewGuid();
        var other = Guid.NewGuid();

        using var db = new AppDbContext(options, new PlatformTenant());

        db.Institutions.AddRange(
            new Institution { Id = inst, Name = "A", Slug = "demo-a", TimeZone = "America/Costa_Rica" },
            new Institution { Id = other, Name = "B", Slug = "demo-b", TimeZone = "America/Costa_Rica" });

        // Students: 3 active (2 with an account), 1 inactive, and one of the other institution.
        Guid Student(Guid institution, string cedula, string status, bool account)
        {
            var id = Guid.NewGuid();

            db.People.Add(new Person { Id = id, InstitutionId = institution, IdentificationNumber = cedula, FirstName = cedula, LastName = "P" });
            db.StudentProfiles.Add(new StudentProfile { PersonId = id, InstitutionId = institution, Status = status });

            if (account)
            {
                db.Users.Add(new User { Id = Guid.NewGuid(), InstitutionId = institution, PersonId = id, Username = cedula + "@x", PasswordHash = "x" });
            }

            return id;
        }

        var ana = Student(inst, "111", StudentStatuses.Active, true);
        var beto = Student(inst, "222", StudentStatuses.Active, true);
        Student(inst, "333", StudentStatuses.Active, false);
        Student(inst, "444", "inactive", true);
        Student(other, "555", StudentStatuses.Active, true);

        // Cafeteria: 3 registrations today (2 by QR), 1 yesterday.
        var point = Guid.NewGuid();
        var lunch = Guid.NewGuid();
        var breakfast = Guid.NewGuid();

        db.AccessPoints.Add(new AccessPoint { Id = point, InstitutionId = inst, Name = "Comedor", Kind = AccessPointKinds.Cafeteria });
        db.AccessServices.AddRange(
            new AccessService { Id = lunch, InstitutionId = inst, AccessPointId = point, Name = "Almuerzo" },
            new AccessService { Id = breakfast, InstitutionId = inst, AccessPointId = point, Name = "Desayuno" });

        AccessEvent Meal(Guid service, Guid person, DateOnly date, string method) => new()
        {
            Id = Guid.NewGuid(),
            InstitutionId = inst,
            AccessPointId = point,
            AccessServiceId = service,
            PersonId = person,
            ServiceDate = date,
            OccurredAt = Now,
            Method = method
        };

        db.AccessEvents.AddRange(
            Meal(breakfast, ana, Today, AccessMethods.Qr),
            Meal(lunch, ana, Today, AccessMethods.Qr),
            Meal(lunch, beto, Today, AccessMethods.Manual),
            Meal(lunch, ana, Yesterday, AccessMethods.Qr));

        // Parking: today one inside and one that left; yesterday one never closed; 40 days ago one never closed.
        ParkingEvent Park(DateOnly date, DateTimeOffset? exited, bool auto = false) => new()
        {
            Id = Guid.NewGuid(),
            InstitutionId = inst,
            VehicleId = Guid.NewGuid(),
            PersonId = ana,
            ServiceDate = date,
            EnteredAt = Now,
            EnteredByUserId = Guid.NewGuid(),
            ExitedAt = exited,
            AutoClosed = auto
        };

        db.ParkingEvents.AddRange(
            Park(Today, null),
            Park(Today, Now),
            Park(Yesterday, null, auto: true),
            Park(Today.AddDays(-40), null, auto: true));

        // Visits: today one pending, one inside and one that left; yesterday one pending.
        Visit Guest(string id, string status, DateOnly date) => new()
        {
            Id = Guid.NewGuid(),
            InstitutionId = inst,
            ServiceDate = date,
            FullName = "V",
            IdentificationNumber = id,
            Host = "H",
            Reason = "R",
            SignaturePng = [1],
            Status = status,
            CreatedAt = Now
        };

        db.Visits.AddRange(
            Guest("A1", VisitStatuses.Pending, Today),
            Guest("A2", VisitStatuses.Inside, Today),
            Guest("A3", VisitStatuses.Exited, Today),
            Guest("A4", VisitStatuses.Pending, Yesterday));

        // Exit permits: 4 today (one still valid at 06:00, one already over, one used, one cancelled) and 1 yesterday.
        ExitPermit Permit(DateOnly date, string status, int fromHour, int toHour) => new()
        {
            Id = Guid.NewGuid(),
            InstitutionId = inst,
            PersonId = ana,
            ServiceDate = date,
            FromTime = new TimeOnly(fromHour, 0),
            ToTime = new TimeOnly(toHour, 0),
            Reason = "r",
            Status = status,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = Now
        };

        db.ExitPermits.AddRange(
            Permit(Today, ExitPermitStatuses.Active, 8, 10),
            Permit(Today, ExitPermitStatuses.Active, 4, 5),
            Permit(Today, ExitPermitStatuses.Used, 8, 10),
            Permit(Today, ExitPermitStatuses.Cancelled, 8, 10),
            Permit(Yesterday, ExitPermitStatuses.Active, 8, 10));

        // Vehicles: 1 pending, 2 approved, 1 suspended, 1 rejected.
        Vehicle Moto(string plate, string status) => new()
        {
            Id = Guid.NewGuid(),
            InstitutionId = inst,
            PersonId = ana,
            Plate = plate,
            Make = "H",
            Model = "X",
            Color = "R",
            Status = status,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = Now
        };

        db.Vehicles.AddRange(
            Moto("AAA111", VehicleStatuses.Pending),
            Moto("BBB222", VehicleStatuses.Approved),
            Moto("CCC333", VehicleStatuses.Approved),
            Moto("DDD444", VehicleStatuses.Suspended),
            Moto("EEE555", VehicleStatuses.Rejected));

        // Security: 00:30 local is today, 23:30 local is yesterday. Other institutions do not count.
        var included = new DateTimeOffset(2026, 11, 19, 6, 30, 0, TimeSpan.Zero);
        var excluded = new DateTimeOffset(2026, 11, 19, 5, 30, 0, TimeSpan.Zero);

        AuditLog Log(Guid institution, string action, DateTimeOffset at) =>
            new() { InstitutionId = institution, Action = action, EntityType = "User", OccurredAt = at };

        db.AuditLogs.AddRange(
            Log(inst, "auth.login.failed", included),
            Log(inst, "auth.login.failed", included),
            Log(inst, "auth.account.locked", included),
            Log(inst, "card.scan.reused", Now),
            Log(inst, "auth.login.failed", excluded),
            Log(inst, "auth.login.succeeded", Now),
            Log(other, "auth.login.failed", Now),
            Log(other, "auth.login.failed", Now),
            Log(other, "auth.refresh.reuse", Now));

        await db.SaveChangesAsync();

        return (options, inst);
    }

    [Fact]
    public async Task The_summary_counts_today_for_every_module_and_only_this_institution()
    {
        var (options, inst) = await SeedAsync();

        var summary = await ServiceFor(options, inst).GetAsync(Everything);

        Assert.Equal(Today, summary.Date);

        Assert.Equal(3, summary.Access!.Total);
        Assert.Equal(2, summary.Access.Qr);
        Assert.Equal(1, summary.Access.Manual);
        Assert.Equal(new[] { "Almuerzo", "Desayuno" }, summary.Access.ByService.Select(s => s.Service).ToArray());
        Assert.Equal(new[] { 2, 1 }, summary.Access.ByService.Select(s => s.Total).ToArray());

        Assert.Equal(1, summary.Parking!.Inside);
        Assert.Equal(2, summary.Parking.EntriesToday);
        Assert.Equal(1, summary.Parking.ExitsToday);
        Assert.Equal(1, summary.Parking.WithoutExit);

        Assert.Equal(3, summary.Visits!.Total);
        Assert.Equal(1, summary.Visits.Pending);
        Assert.Equal(1, summary.Visits.Inside);

        Assert.Equal(4, summary.Exits!.Total);
        Assert.Equal(1, summary.Exits.Valid);
        Assert.Equal(1, summary.Exits.Used);

        Assert.Equal(1, summary.Vehicles!.Pending);
        Assert.Equal(2, summary.Vehicles.Approved);
        Assert.Equal(1, summary.Vehicles.Suspended);

        Assert.Equal(3, summary.Students!.Active);
        Assert.Equal(2, summary.Students.WithAccount);
        Assert.Equal(1, summary.Students.WithoutAccount);

        // 23:30 of yesterday (local) is not today; the other institution's attempts are not counted.
        Assert.Equal(2, summary.Security!.FailedLogins);
        Assert.Equal(1, summary.Security.LockedAccounts);
        Assert.Equal(1, summary.Security.CardReuse);
        Assert.Equal(0, summary.Security.SessionReuse);
    }

    [Fact]
    public async Task Only_the_requested_sections_are_computed()
    {
        var (options, inst) = await SeedAsync();

        var none = await ServiceFor(options, inst).GetAsync(new SummaryScope(false, false, false, false, false, false, false));

        Assert.Null(none.Access);
        Assert.Null(none.Parking);
        Assert.Null(none.Visits);
        Assert.Null(none.Exits);
        Assert.Null(none.Vehicles);
        Assert.Null(none.Students);
        Assert.Null(none.Security);

        var some = await ServiceFor(options, inst).GetAsync(new SummaryScope(false, true, false, false, true, false, false));

        Assert.NotNull(some.Parking);
        Assert.NotNull(some.Vehicles);
        Assert.Null(some.Access);
        Assert.Null(some.Students);
        Assert.Null(some.Security);
    }

    [Fact]
    public async Task Platform_users_have_no_summary()
    {
        var (options, _) = await SeedAsync();

        await Assert.ThrowsAsync<TenantViolationException>(() => ServiceFor(options, null).GetAsync(Everything));
    }
}
