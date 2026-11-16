using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Auditing;
using VarinsEdu.Infrastructure.Parking;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;
using VarinsEdu.Infrastructure.Seeding;
using VarinsEdu.Infrastructure.Students;
using Xunit;

namespace VarinsEdu.Tests;

public class ParkingServiceTests
{
    private static readonly string Secret = new('k', 48);
    private static readonly DateTimeOffset Start = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

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
        Guid ApprovedId,
        Guid PendingId,
        Guid StaffId);

    // One institution (time zone UTC) with Ana (group 10-1, an approved vehicle AAA111 and a pending BBB222)
    // and Beto (no vehicles), and a gate guard.
    private static async Task<World> SeedAsync(
        DbContextOptions<AppDbContext>? options = null,
        FakeClock? clock = null,
        string slug = "demo-a")
    {
        options ??= new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        clock ??= new FakeClock(Start);

        var inst = Guid.NewGuid();
        var group = Guid.NewGuid();
        var ana = Guid.NewGuid();
        var beto = Guid.NewGuid();
        var approved = Guid.NewGuid();
        var pending = Guid.NewGuid();
        var staff = Guid.NewGuid();

        Vehicle NewVehicle(Guid id, string plate, string status) => new()
        {
            Id = id,
            InstitutionId = inst,
            PersonId = ana,
            Plate = plate,
            Make = "Honda",
            Model = "XR",
            Color = "Rojo",
            Status = status,
            CreatedByUserId = staff,
            CreatedAt = Start
        };

        using var db = new AppDbContext(options, new PlatformTenant());

        db.Institutions.Add(new Institution { Id = inst, Name = "Colegio " + slug, Slug = slug, TimeZone = "UTC" });
        db.Groups.Add(new Group { Id = group, InstitutionId = inst, Name = "10-1" });
        db.People.AddRange(
            new Person { Id = ana, InstitutionId = inst, IdentificationNumber = "111111111", FirstName = "Ana", LastName = "Prueba Uno" },
            new Person { Id = beto, InstitutionId = inst, IdentificationNumber = "222222222", FirstName = "Beto", LastName = "Prueba Dos" });
        db.StudentProfiles.AddRange(
            new StudentProfile { PersonId = ana, InstitutionId = inst, GroupId = group },
            new StudentProfile { PersonId = beto, InstitutionId = inst });
        db.Vehicles.AddRange(
            NewVehicle(approved, "AAA111", VehicleStatuses.Approved),
            NewVehicle(pending, "BBB222", VehicleStatuses.Pending));

        await db.SaveChangesAsync();

        return new World(options, clock, inst, ana, beto, approved, pending, staff);
    }

    // A new card code every time, like the student's phone does.
    private static string Card(World w, Guid personId, Guid? institutionId = null) =>
        new CardTokenService(Secret, w.Clock).Issue(personId, institutionId ?? w.InstitutionId).Token;

    private static ParkingService ServiceFor(World w, Guid? institutionId, Guid? userId)
    {
        var tenant = new FakeTenant(institutionId);
        var db = new AppDbContext(w.Options, tenant);
        var user = new FakeUser(userId);
        var suppression = new AuditSuppression();

        var cards = new CardScanService(db, tenant, user, new CardTokenService(Secret, w.Clock), suppression, w.Clock);

        return new ParkingService(db, tenant, user, cards, suppression, w.Clock);
    }

    private static ParkingService GateOf(World w) => ServiceFor(w, w.InstitutionId, w.StaffId);

    private static AppDbContext Reader(World w) => new(w.Options, new PlatformTenant());

    [Fact]
    public async Task The_preview_shows_the_approved_vehicles_and_does_not_use_the_card_up()
    {
        var w = await SeedAsync();
        var token = Card(w, w.AnaId);

        var preview = await GateOf(w).PreviewAsync(token);

        Assert.Null(preview.Error);
        Assert.Equal("Ana", preview.Value!.Student.FirstName);
        Assert.Equal("10-1", preview.Value.Student.Group);

        var vehicle = Assert.Single(preview.Value.Vehicles);

        Assert.Equal("AAA111", vehicle.Plate);
        Assert.False(vehicle.Inside);

        // The same code can be previewed again: it is only used up when a movement is registered.
        Assert.Null((await GateOf(w).PreviewAsync(token)).Error);

        using var db = Reader(w);
        Assert.Empty(await db.CardScans.ToListAsync());
        Assert.Empty(await db.ParkingEvents.ToListAsync());
    }

    [Fact]
    public async Task Students_without_an_approved_vehicle_and_foreign_vehicles_are_refused()
    {
        var w = await SeedAsync();

        Assert.Equal("no_approved_vehicle", (await GateOf(w).PreviewAsync(Card(w, w.BetoId))).Error);

        // A vehicle of another student looks just like one that does not exist.
        Assert.Equal("vehicle_not_found", (await GateOf(w).RegisterAsync(Card(w, w.BetoId), w.ApprovedId)).Error);
        Assert.Equal("vehicle_not_found", (await GateOf(w).RegisterAsync(Card(w, w.AnaId), Guid.NewGuid())).Error);
        Assert.Equal("vehicle_not_approved", (await GateOf(w).RegisterAsync(Card(w, w.AnaId), w.PendingId)).Error);

        using var db = Reader(w);
        Assert.Empty(await db.ParkingEvents.ToListAsync());
        Assert.Empty(await db.CardScans.ToListAsync());
    }

    [Fact]
    public async Task Registering_enters_then_exits_then_enters_again()
    {
        var w = await SeedAsync();

        var entry = await GateOf(w).RegisterAsync(Card(w, w.AnaId), w.ApprovedId);

        Assert.Null(entry.Error);
        Assert.Equal("entry", entry.Value!.Action);
        Assert.Equal("AAA111", entry.Value.Plate);
        Assert.Equal("Ana", entry.Value.Student.FirstName);

        using (var db = Reader(w))
        {
            var visit = await db.ParkingEvents.SingleAsync();

            Assert.Equal(new DateOnly(2026, 11, 16), visit.ServiceDate);
            Assert.Equal(Start, visit.EnteredAt);
            Assert.Equal(w.StaffId, visit.EnteredByUserId);
            Assert.Null(visit.ExitedAt);
            Assert.Equal(1, await db.CardScans.CountAsync());
        }

        var inside = await GateOf(w).PreviewAsync(Card(w, w.AnaId));
        Assert.True(Assert.Single(inside.Value!.Vehicles).Inside);

        w.Clock.Now = Start.AddHours(5);

        var exit = await GateOf(w).RegisterAsync(Card(w, w.AnaId), w.ApprovedId);

        Assert.Equal("exit", exit.Value!.Action);

        using (var db = Reader(w))
        {
            var visit = await db.ParkingEvents.SingleAsync();

            Assert.Equal(Start.AddHours(5), visit.ExitedAt);
            Assert.Equal(w.StaffId, visit.ExitedByUserId);
            Assert.False(visit.AutoClosed);
        }

        Assert.Equal("entry", (await GateOf(w).RegisterAsync(Card(w, w.AnaId), w.ApprovedId)).Value!.Action);

        using var check = Reader(w);
        Assert.Equal(2, await check.ParkingEvents.CountAsync());
        Assert.Equal(3, await check.CardScans.CountAsync());
    }

    [Fact]
    public async Task A_card_code_works_only_once_for_the_parking_too()
    {
        var w = await SeedAsync();
        var token = Card(w, w.AnaId);

        Assert.Null((await GateOf(w).RegisterAsync(token, w.ApprovedId)).Error);
        Assert.Equal("card_already_used", (await GateOf(w).RegisterAsync(token, w.ApprovedId)).Error);
        Assert.Equal("card_already_used", (await GateOf(w).PreviewAsync(token)).Error);

        using var db = Reader(w);
        Assert.Equal(1, await db.ParkingEvents.CountAsync());
        Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.Action == "card.scan.reused"));
    }

    [Fact]
    public async Task A_refusal_does_not_use_the_card_up()
    {
        var w = await SeedAsync();
        var token = Card(w, w.AnaId);

        Assert.Equal("vehicle_not_approved", (await GateOf(w).RegisterAsync(token, w.PendingId)).Error);

        using (var db = Reader(w))
        {
            Assert.Empty(await db.CardScans.ToListAsync());
        }

        // The same code still works with the right vehicle.
        Assert.Null((await GateOf(w).RegisterAsync(token, w.ApprovedId)).Error);
    }

    [Fact]
    public async Task A_visit_left_open_on_a_previous_day_is_closed_and_a_new_one_starts()
    {
        var w = await SeedAsync();

        Assert.Equal("entry", (await GateOf(w).RegisterAsync(Card(w, w.AnaId), w.ApprovedId)).Value!.Action);

        w.Clock.Now = Start.AddDays(1);

        // The vehicle looks outside, because yesterday's visit does not count.
        var preview = await GateOf(w).PreviewAsync(Card(w, w.AnaId));
        Assert.False(Assert.Single(preview.Value!.Vehicles).Inside);

        // So scanning registers an entry, not an exit.
        Assert.Equal("entry", (await GateOf(w).RegisterAsync(Card(w, w.AnaId), w.ApprovedId)).Value!.Action);

        using (var db = Reader(w))
        {
            var visits = await db.ParkingEvents.OrderBy(e => e.EnteredAt).ToListAsync();

            Assert.Equal(2, visits.Count);
            Assert.True(visits[0].AutoClosed);
            Assert.Null(visits[0].ExitedAt);
            Assert.False(visits[1].AutoClosed);
            Assert.Null(visits[1].ExitedAt);
        }

        var yesterday = await GateOf(w).ListEventsAsync(new DateOnly(2026, 11, 16), 0, 50);
        Assert.Equal("no_exit", Assert.Single(yesterday.Items).Status);

        var today = await GateOf(w).ListEventsAsync(null, 0, 50);
        Assert.Equal("inside", Assert.Single(today.Items).Status);
        Assert.Equal(1, today.Inside);
    }

    [Fact]
    public async Task Expired_forged_and_foreign_cards_are_refused()
    {
        var w = await SeedAsync();
        var expired = Card(w, w.AnaId);

        w.Clock.Now = Start + CardTokenService.Lifetime + TimeSpan.FromSeconds(1);

        Assert.Equal("expired_card", (await GateOf(w).PreviewAsync(expired)).Error);
        Assert.Equal("expired_card", (await GateOf(w).RegisterAsync(expired, w.ApprovedId)).Error);
        Assert.Equal("invalid_card", (await GateOf(w).PreviewAsync("abc")).Error);
        Assert.Equal("invalid_card", (await GateOf(w).PreviewAsync(Card(w, w.AnaId, Guid.NewGuid()))).Error);

        using var db = Reader(w);
        Assert.Empty(await db.ParkingEvents.ToListAsync());
        Assert.Empty(await db.CardScans.ToListAsync());
    }

    [Fact]
    public async Task The_list_shows_one_day_with_its_status_and_only_this_institution()
    {
        var w = await SeedAsync();
        var other = await SeedAsync(w.Options, w.Clock, "demo-b");

        // A second approved vehicle, for Beto.
        var betoVehicle = Guid.NewGuid();

        using (var db = Reader(w))
        {
            db.Vehicles.Add(new Vehicle
            {
                Id = betoVehicle,
                InstitutionId = w.InstitutionId,
                PersonId = w.BetoId,
                Plate = "CCC333",
                Make = "Yamaha",
                Model = "FZ",
                Color = "Negro",
                Status = VehicleStatuses.Approved,
                CreatedByUserId = w.StaffId,
                CreatedAt = Start
            });

            // An old visit that was never closed.
            db.ParkingEvents.Add(new ParkingEvent
            {
                Id = Guid.NewGuid(),
                InstitutionId = w.InstitutionId,
                VehicleId = w.ApprovedId,
                PersonId = w.AnaId,
                ServiceDate = new DateOnly(2026, 11, 15),
                EnteredAt = Start.AddDays(-1),
                EnteredByUserId = w.StaffId
            });

            await db.SaveChangesAsync();
        }

        // The old visit is still open, so a visit that started today is a new one: close it as the service would.
        using (var db = Reader(w))
        {
            var isOld = await db.ParkingEvents.SingleAsync();
            isOld.AutoClosed = true;
            await db.SaveChangesAsync();
        }

        await GateOf(w).RegisterAsync(Card(w, w.AnaId), w.ApprovedId);
        w.Clock.Now = Start.AddHours(1);
        await GateOf(w).RegisterAsync(Card(w, w.AnaId), w.ApprovedId);
        w.Clock.Now = Start.AddHours(2);
        await GateOf(w).RegisterAsync(Card(w, w.BetoId), betoVehicle);

        // Another institution registers too: it must not show up here.
        await GateOf(other).RegisterAsync(Card(other, other.AnaId), other.ApprovedId);

        var today = await GateOf(w).ListEventsAsync(null, 0, 50);

        Assert.Equal(2, today.Total);
        Assert.Equal(1, today.Inside);
        Assert.Equal(new[] { "CCC333", "AAA111" }, today.Items.Select(i => i.Plate).ToArray());
        Assert.Equal(new[] { "inside", "exited" }, today.Items.Select(i => i.Status).ToArray());
        Assert.Equal("Beto", today.Items[0].FirstName);
        Assert.Null(today.Items[0].Group);
        Assert.Equal("10-1", today.Items[1].Group);

        var old = await GateOf(w).ListEventsAsync(new DateOnly(2026, 11, 15), 0, 50);

        Assert.Equal("no_exit", Assert.Single(old.Items).Status);
        Assert.Equal(0, old.Inside);
    }

    [Fact]
    public async Task Platform_users_cannot_use_the_parking()
    {
        var w = await SeedAsync();
        var service = ServiceFor(w, null, w.StaffId);

        await Assert.ThrowsAsync<TenantViolationException>(() => service.PreviewAsync(Card(w, w.AnaId)));
        await Assert.ThrowsAsync<TenantViolationException>(() => service.RegisterAsync(Card(w, w.AnaId), w.ApprovedId));
        await Assert.ThrowsAsync<TenantViolationException>(() => service.ListEventsAsync(null, 0, 50));
    }
}
