using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Auditing;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Seeding;
using VarinsEdu.Infrastructure.Vehicles;
using Xunit;

namespace VarinsEdu.Tests;

public class VehicleServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
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
        Guid InstitutionId,
        Guid AnaUserId,
        Guid AnaPersonId,
        Guid BetoUserId,
        Guid BetoPersonId,
        Guid StaffId);

    private static DbContextOptions<AppDbContext> NewOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static User NewUser(Guid institutionId, string username, Guid? personId) => new()
    {
        Id = Guid.NewGuid(),
        InstitutionId = institutionId,
        PersonId = personId,
        Username = username,
        PasswordHash = "x",
        CreatedAt = Start
    };

    // One institution with Ana (cédula 111111111, group 10-1) and Beto (222222222, no group), both students with
    // an account, and a staff member without a student profile.
    private static async Task<World> SeedAsync(
        DbContextOptions<AppDbContext>? options = null,
        string slug = "demo-a",
        Guid? institutionId = null)
    {
        options ??= NewOptions();

        var inst = institutionId ?? Guid.NewGuid();
        var group = Guid.NewGuid();
        var ana = Guid.NewGuid();
        var beto = Guid.NewGuid();

        var anaUser = NewUser(inst, $"ana@{slug}", ana);
        var betoUser = NewUser(inst, $"beto@{slug}", beto);
        var staff = NewUser(inst, $"staff@{slug}", null);

        using var db = new AppDbContext(options, new PlatformTenant());

        db.Institutions.Add(new Institution { Id = inst, Name = "Colegio " + slug, Slug = slug });
        db.Groups.Add(new Group { Id = group, InstitutionId = inst, Name = "10-1" });
        db.People.AddRange(
            new Person
            {
                Id = ana,
                InstitutionId = inst,
                IdentificationNumber = "111111111",
                FirstName = "Ana",
                LastName = "Prueba Uno"
            },
            new Person
            {
                Id = beto,
                InstitutionId = inst,
                IdentificationNumber = "222222222",
                FirstName = "Beto",
                LastName = "Prueba Dos"
            });
        db.StudentProfiles.AddRange(
            new StudentProfile { PersonId = ana, InstitutionId = inst, GroupId = group },
            new StudentProfile { PersonId = beto, InstitutionId = inst });
        db.Users.AddRange(anaUser, betoUser, staff);

        await db.SaveChangesAsync();

        return new World(options, inst, anaUser.Id, ana, betoUser.Id, beto, staff.Id);
    }

    private static VehicleService ServiceFor(DbContextOptions<AppDbContext> options, Guid? institutionId, Guid? userId)
    {
        var tenant = new FakeTenant(institutionId);

        return new VehicleService(new AppDbContext(options, tenant), tenant, new FakeUser(userId), new FakeClock(Start));
    }

    private static VehicleService AnaOf(World w) => ServiceFor(w.Options, w.InstitutionId, w.AnaUserId);

    private static VehicleService BetoOf(World w) => ServiceFor(w.Options, w.InstitutionId, w.BetoUserId);

    private static VehicleService StaffOf(World w) => ServiceFor(w.Options, w.InstitutionId, w.StaffId);

    private static AppDbContext Reader(DbContextOptions<AppDbContext> options) =>
        new(options, new PlatformTenant());

    private static VehicleInput Moto(string plate = "AAA111") => new(plate, "Honda", "XR 150", "Rojo");

    private static async Task<Guid> IdOfAsync(VehicleService service, string plate) =>
        (await service.ListMineAsync()).Value!.Single(v => v.Plate == plate).Id;

    [Fact]
    public async Task A_student_requests_a_vehicle_that_waits_for_approval()
    {
        var w = await SeedAsync();

        var result = await AnaOf(w).RequestAsync(new VehicleInput(" mot-123 456 ", " Honda ", "XR 150", "Rojo"));

        Assert.Null(result.Error);
        Assert.Equal("MOT123456", result.Value!.Plate);
        Assert.Equal("Honda", result.Value.Make);
        Assert.Equal(VehicleStatuses.Pending, result.Value.Status);

        Assert.Equal("MOT123456", Assert.Single((await AnaOf(w).ListMineAsync()).Value!).Plate);
        Assert.Empty((await BetoOf(w).ListMineAsync()).Value!);

        using var db = Reader(w.Options);
        var stored = await db.Vehicles.SingleAsync();

        Assert.Equal(w.AnaPersonId, stored.PersonId);
        Assert.Equal(w.InstitutionId, stored.InstitutionId);
        Assert.Equal(w.AnaUserId, stored.CreatedByUserId);
        Assert.Null(stored.ReviewedByUserId);
        Assert.Null(stored.ReviewedAt);
    }

    [Fact]
    public async Task Invalid_vehicle_data_is_rejected()
    {
        var w = await SeedAsync();
        var ana = AnaOf(w);

        Assert.Equal("invalid_plate", (await ana.RequestAsync(new("--", "Honda", "XR", "Rojo"))).Error);
        Assert.Equal("invalid_plate", (await ana.RequestAsync(new("AB", "Honda", "XR", "Rojo"))).Error);
        Assert.Equal("invalid_plate", (await ana.RequestAsync(new("ABCDEFGHIJK", "Honda", "XR", "Rojo"))).Error);
        Assert.Equal("invalid_plate", (await ana.RequestAsync(new(null, "Honda", "XR", "Rojo"))).Error);

        Assert.Equal("invalid_vehicle_data", (await ana.RequestAsync(new("AAA111", "", "XR", "Rojo"))).Error);
        Assert.Equal("invalid_vehicle_data", (await ana.RequestAsync(new("AAA111", new string('x', 61), "XR", "Rojo"))).Error);
        Assert.Equal("invalid_vehicle_data", (await ana.RequestAsync(new("AAA111", "Honda", "XR", null))).Error);

        using var db = Reader(w.Options);
        Assert.Empty(await db.Vehicles.ToListAsync());
    }

    [Fact]
    public async Task A_student_can_have_two_live_vehicles_at_most()
    {
        var w = await SeedAsync();
        var ana = AnaOf(w);

        Assert.Null((await ana.RequestAsync(Moto("AAA111"))).Error);
        Assert.Null((await ana.RequestAsync(Moto("BBB222"))).Error);
        Assert.Equal("vehicle_limit_reached", (await ana.RequestAsync(Moto("CCC333"))).Error);

        // Withdrawing one frees a place.
        Assert.Null((await ana.WithdrawAsync(await IdOfAsync(ana, "AAA111"))).Error);
        Assert.Null((await ana.RequestAsync(Moto("CCC333"))).Error);
        Assert.Equal("vehicle_limit_reached", (await ana.RequestAsync(Moto("DDD444"))).Error);

        // So does a rejection.
        var rejected = await IdOfAsync(ana, "BBB222");
        Assert.Null((await StaffOf(w).SetStatusAsync(rejected, new(VehicleStatuses.Rejected, "Falta un documento"))).Error);
        Assert.Null((await ana.RequestAsync(Moto("DDD444"))).Error);

        // The limit is per student.
        Assert.Null((await BetoOf(w).RequestAsync(Moto("EEE555"))).Error);
    }

    [Fact]
    public async Task A_plate_belongs_to_one_live_vehicle_per_institution()
    {
        var options = NewOptions();
        var w = await SeedAsync(options);
        var other = await SeedAsync(options, "demo-b");

        Assert.Null((await AnaOf(w).RequestAsync(Moto("MOT123"))).Error);
        Assert.Equal("plate_taken", (await BetoOf(w).RequestAsync(Moto("mot-123"))).Error);

        // Withdrawing frees the plate.
        Assert.Null((await AnaOf(w).WithdrawAsync(await IdOfAsync(AnaOf(w), "MOT123"))).Error);
        Assert.Null((await BetoOf(w).RequestAsync(Moto("MOT123"))).Error);

        // Another institution is another world.
        var otherAna = ServiceFor(options, other.InstitutionId, other.AnaUserId);
        Assert.Null((await otherAna.RequestAsync(Moto("MOT123"))).Error);
    }

    [Fact]
    public async Task Only_active_students_have_vehicles()
    {
        var w = await SeedAsync();
        var staff = StaffOf(w);

        Assert.Equal("vehicle_student_unavailable", (await staff.RequestAsync(Moto())).Error);
        Assert.Equal("vehicle_student_unavailable", (await staff.ListMineAsync()).Error);
        Assert.Equal("vehicle_student_unavailable", (await staff.WithdrawAsync(Guid.NewGuid())).Error);

        using (var db = Reader(w.Options))
        {
            var profile = await db.StudentProfiles.SingleAsync(s => s.PersonId == w.AnaPersonId);
            profile.Status = "inactive";
            await db.SaveChangesAsync();
        }

        Assert.Equal("vehicle_student_unavailable", (await AnaOf(w).RequestAsync(Moto())).Error);
    }

    [Fact]
    public async Task A_student_withdraws_only_her_own_vehicles()
    {
        var w = await SeedAsync();
        var ana = AnaOf(w);

        await ana.RequestAsync(Moto("AAA111"));
        var id = await IdOfAsync(ana, "AAA111");

        Assert.Equal("vehicle_not_found", (await BetoOf(w).WithdrawAsync(id)).Error);
        Assert.Equal("vehicle_not_found", (await ana.WithdrawAsync(Guid.NewGuid())).Error);

        Assert.Null((await ana.WithdrawAsync(id)).Error);
        Assert.Equal("invalid_status_change", (await ana.WithdrawAsync(id)).Error);

        // The history stays: the withdrawn vehicle is still listed.
        Assert.Equal(VehicleStatuses.Withdrawn, Assert.Single((await ana.ListMineAsync()).Value!).Status);
    }

    [Fact]
    public async Task Staff_review_follows_the_allowed_transitions()
    {
        var w = await SeedAsync();
        var ana = AnaOf(w);
        var staff = StaffOf(w);

        await ana.RequestAsync(Moto("AAA111"));
        var id = await IdOfAsync(ana, "AAA111");

        Assert.Equal("invalid_status", (await staff.SetStatusAsync(id, new("withdrawn", null))).Error);
        Assert.Equal("invalid_status", (await staff.SetStatusAsync(id, new(null, null))).Error);
        Assert.Equal("note_required", (await staff.SetStatusAsync(id, new(VehicleStatuses.Rejected, null))).Error);
        Assert.Equal("note_too_long", (await staff.SetStatusAsync(id, new(VehicleStatuses.Rejected, new string('x', 201)))).Error);
        Assert.Equal("vehicle_not_found", (await staff.SetStatusAsync(Guid.NewGuid(), new(VehicleStatuses.Approved, null))).Error);

        // A pending vehicle cannot be suspended yet.
        Assert.Equal("invalid_status_change", (await staff.SetStatusAsync(id, new(VehicleStatuses.Suspended, "x"))).Error);

        Assert.Null((await staff.SetStatusAsync(id, new(VehicleStatuses.Approved, null))).Error);

        using (var db = Reader(w.Options))
        {
            var approved = await db.Vehicles.SingleAsync();

            Assert.Equal(VehicleStatuses.Approved, approved.Status);
            Assert.Equal(w.StaffId, approved.ReviewedByUserId);
            Assert.Equal(Start, approved.ReviewedAt);
        }

        // An approved vehicle is not rejected: it is suspended, and that needs a note.
        Assert.Equal("invalid_status_change", (await staff.SetStatusAsync(id, new(VehicleStatuses.Rejected, "x"))).Error);
        Assert.Equal("note_required", (await staff.SetStatusAsync(id, new(VehicleStatuses.Suspended, " "))).Error);
        Assert.Null((await staff.SetStatusAsync(id, new(VehicleStatuses.Suspended, "Documentos vencidos"))).Error);

        // The student sees why.
        var shown = Assert.Single((await ana.ListMineAsync()).Value!);
        Assert.Equal(VehicleStatuses.Suspended, shown.Status);
        Assert.Equal("Documentos vencidos", shown.StatusNote);

        // Reinstating clears the note.
        Assert.Null((await staff.SetStatusAsync(id, new(VehicleStatuses.Approved, null))).Error);
        Assert.Null(Assert.Single((await ana.ListMineAsync()).Value!).StatusNote);

        // A rejected vehicle stays rejected.
        await ana.RequestAsync(Moto("BBB222"));
        var second = await IdOfAsync(ana, "BBB222");

        Assert.Null((await staff.SetStatusAsync(second, new(VehicleStatuses.Rejected, "Falta la póliza"))).Error);
        Assert.Equal("invalid_status_change", (await staff.SetStatusAsync(second, new(VehicleStatuses.Approved, null))).Error);
    }

    [Fact]
    public async Task Staff_register_a_vehicle_for_a_student_by_cedula()
    {
        var options = NewOptions();
        var w = await SeedAsync(options);
        var other = await SeedAsync(options, "demo-b");
        var staff = StaffOf(w);

        // The same cédula exists in the other institution: the vehicle goes to this institution's student.
        var approved = await staff.RegisterForStudentAsync(new("1-1111-1111", "xyz 987", "Yamaha", "FZ", "Negro", true));

        Assert.Null(approved.Error);
        Assert.Equal("XYZ987", approved.Value!.Plate);
        Assert.Equal(VehicleStatuses.Approved, approved.Value.Status);

        using (var db = Reader(options))
        {
            var stored = await db.Vehicles.SingleAsync();

            Assert.Equal(w.AnaPersonId, stored.PersonId);
            Assert.NotEqual(other.AnaPersonId, stored.PersonId);
            Assert.Equal(w.StaffId, stored.CreatedByUserId);
            Assert.Equal(w.StaffId, stored.ReviewedByUserId);
            Assert.Equal(Start, stored.ReviewedAt);
        }

        var pending = await staff.RegisterForStudentAsync(new("111111111", "AAA111", "Honda", "XR", "Rojo"));

        Assert.Null(pending.Error);
        Assert.Equal(VehicleStatuses.Pending, pending.Value!.Status);
        Assert.Null(pending.Value.ReviewedAt);

        Assert.Equal("vehicle_limit_reached", (await staff.RegisterForStudentAsync(new("111111111", "BBB222", "Honda", "XR", "Rojo"))).Error);
        Assert.Equal("plate_taken", (await staff.RegisterForStudentAsync(new("222222222", "xyz-987", "Honda", "XR", "Rojo"))).Error);
        Assert.Equal("student_not_found", (await staff.RegisterForStudentAsync(new("0-0000-0000", "CCC333", "Honda", "XR", "Rojo"))).Error);
        Assert.Equal("student_not_found", (await staff.RegisterForStudentAsync(new(" ", "CCC333", "Honda", "XR", "Rojo"))).Error);
        Assert.Equal("invalid_plate", (await staff.RegisterForStudentAsync(new("222222222", "--", "Honda", "XR", "Rojo"))).Error);
    }

    [Fact]
    public async Task The_list_filters_by_status_and_search_and_shows_only_this_institution()
    {
        var options = NewOptions();
        var w = await SeedAsync(options);
        var other = await SeedAsync(options, "demo-b");
        var staff = StaffOf(w);

        await staff.RegisterForStudentAsync(new("111111111", "AAA111", "Honda", "XR", "Rojo", true));
        await staff.RegisterForStudentAsync(new("222222222", "BBB222", "Yamaha", "FZ", "Negro"));
        await staff.RegisterForStudentAsync(new("111111111", "CCC333", "Suzuki", "GN", "Azul"));
        await ServiceFor(options, other.InstitutionId, other.StaffId)
            .RegisterForStudentAsync(new("111111111", "AAA111", "Honda", "XR", "Rojo"));

        Assert.Equal(3, (await staff.ListAsync(null, null, 0, 50)).Total);
        Assert.Equal(2, (await staff.ListAsync("pending", null, 0, 50)).Total);

        var approved = Assert.Single((await staff.ListAsync("approved", null, 0, 50)).Items);

        Assert.Equal("AAA111", approved.Plate);
        Assert.Equal("Ana", approved.FirstName);
        Assert.Equal("10-1", approved.Group);
        Assert.Equal("staff@demo-a", approved.ReviewedBy);

        var byPlate = Assert.Single((await staff.ListAsync(null, "bbb-222", 0, 50)).Items);

        Assert.Equal("Beto", byPlate.FirstName);
        Assert.Null(byPlate.Group);
        Assert.Null(byPlate.ReviewedBy);

        Assert.Equal(2, (await staff.ListAsync(null, "ana", 0, 50)).Total);
    }

    [Fact]
    public async Task Platform_users_cannot_manage_vehicles()
    {
        var w = await SeedAsync();
        var service = ServiceFor(w.Options, null, Guid.NewGuid());

        await Assert.ThrowsAsync<TenantViolationException>(() => service.ListAsync(null, null, 0, 50));
        await Assert.ThrowsAsync<TenantViolationException>(() => service.ListMineAsync());
        await Assert.ThrowsAsync<TenantViolationException>(() => service.RequestAsync(Moto()));
        await Assert.ThrowsAsync<TenantViolationException>(() => service.WithdrawAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<TenantViolationException>(() => service.RegisterForStudentAsync(new("111111111", "AAA111", "Honda", "XR", "Rojo")));
        await Assert.ThrowsAsync<TenantViolationException>(() => service.SetStatusAsync(Guid.NewGuid(), new(VehicleStatuses.Approved, null)));
    }

    [Fact]
    public async Task Vehicle_changes_are_audited_without_the_plate_or_the_note()
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

        var w = await SeedAsync(options, "demo-a", institutionId);
        var ana = AnaOf(w);

        await ana.RequestAsync(Moto("MOT123456"));
        await StaffOf(w).SetStatusAsync(await IdOfAsync(ana, "MOT123456"), new(VehicleStatuses.Rejected, "Nota secreta"));

        using var db = Reader(options);
        var logs = await db.AuditLogs.Where(a => a.EntityType == "Vehicle").ToListAsync();

        Assert.Equal(2, logs.Count);
        Assert.All(logs, a => Assert.DoesNotContain("MOT123456", a.Metadata));
        Assert.All(logs, a => Assert.DoesNotContain("Nota secreta", a.Metadata));
        Assert.Contains(logs, a => a.Action == "Vehicle.updated" && a.Metadata!.Contains("rejected"));
        Assert.Contains(logs, a => a.Metadata!.Contains("[redacted]"));
    }
}
