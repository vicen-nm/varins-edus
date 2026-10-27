using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Access;
using VarinsEdu.Infrastructure.Auditing;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;
using VarinsEdu.Infrastructure.Seeding;
using Xunit;

namespace VarinsEdu.Tests;

public class AccessRegistrationServiceTests
{
    private static readonly string Secret = new('k', 48);
    private static readonly DateTimeOffset Noon = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private const double Lat = 9.9333;
    private const double Lon = -84.0833;

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

    // One institution (time zone UTC) with a cafeteria at (Lat, Lon), a lunch service 11:00-13:00,
    // and Ana, a student with an account.
    private sealed class World
    {
        public required DbContextOptions<AppDbContext> Options { get; init; }
        public required FakeClock Clock { get; init; }
        public Guid InstitutionId { get; init; } = Guid.NewGuid();
        public Guid PointId { get; init; } = Guid.NewGuid();
        public Guid ServiceId { get; init; } = Guid.NewGuid();
        public Guid UserId { get; init; } = Guid.NewGuid();
        public Guid PersonId { get; init; } = Guid.NewGuid();
    }

    private static async Task<World> SeedAsync(double? latitude = Lat, bool pointActive = true, bool withProfile = true)
    {
        var world = new World
        {
            Options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            Clock = new FakeClock(Noon)
        };

        using var db = Reader(world);

        db.Institutions.Add(new Institution { Id = world.InstitutionId, Name = "Colegio Demo", Slug = "demo-a", TimeZone = "UTC" });

        db.AccessPoints.Add(new AccessPoint
        {
            Id = world.PointId,
            InstitutionId = world.InstitutionId,
            Name = "Comedor",
            Kind = AccessPointKinds.Cafeteria,
            Latitude = latitude,
            Longitude = latitude is null ? null : Lon,
            IsActive = pointActive,
            CreatedAt = Noon
        });

        db.AccessServices.Add(new AccessService
        {
            Id = world.ServiceId,
            InstitutionId = world.InstitutionId,
            AccessPointId = world.PointId,
            Name = "Almuerzo",
            StartTime = new TimeOnly(11, 0),
            EndTime = new TimeOnly(13, 0)
        });

        db.People.Add(new Person
        {
            Id = world.PersonId,
            InstitutionId = world.InstitutionId,
            IdentificationNumber = "111111111",
            FirstName = "Ana",
            LastName = "Prueba Uno",
            CreatedAt = Noon
        });

        if (withProfile)
        {
            db.StudentProfiles.Add(new StudentProfile { PersonId = world.PersonId, InstitutionId = world.InstitutionId });
        }

        db.Users.Add(new User
        {
            Id = world.UserId,
            InstitutionId = world.InstitutionId,
            PersonId = world.PersonId,
            Username = "111111111@demo-a",
            PasswordHash = "x"
        });

        await db.SaveChangesAsync();
        return world;
    }

    private static AppDbContext Reader(World w) => new(w.Options, new PlatformTenant());

    private static AccessRegistrationService ServiceFor(World w, Guid? institutionId, Guid? userId)
    {
        var tenant = new FakeTenant(institutionId);

        return new AccessRegistrationService(
            new AppDbContext(w.Options, tenant),
            tenant,
            new FakeUser(userId),
            new AccessPointTokenService(Secret, w.Clock),
            new AuditSuppression(),
            w.Clock);
    }

    private static string Qr(World w, Guid? institutionId = null) =>
        new AccessPointTokenService(Secret, w.Clock).Issue(w.PointId, institutionId ?? w.InstitutionId).Token;

    private static Task<Outcome<RegistrationResult>> CheckIn(
        World w,
        string? token = null,
        double lat = Lat,
        double lon = Lon,
        double accuracy = 20) =>
        ServiceFor(w, w.InstitutionId, w.UserId).CheckInAsync(new CheckInCommand(token ?? Qr(w), lat, lon, accuracy));

    private static Task<Outcome<RegistrationResult>> Manual(World w, string? cedula) =>
        ServiceFor(w, w.InstitutionId, Guid.NewGuid()).RegisterManuallyAsync(w.PointId, cedula);

    private static async Task<int> EventCount(World w)
    {
        using var db = Reader(w);
        return await db.AccessEvents.CountAsync();
    }

    [Fact]
    public async Task A_check_in_records_the_event()
    {
        var w = await SeedAsync();

        var result = await CheckIn(w);

        Assert.Null(result.Error);
        Assert.Equal("Almuerzo", result.Value!.ServiceName);
        Assert.Equal("Comedor", result.Value.PointName);

        using var db = Reader(w);
        var stored = await db.AccessEvents.SingleAsync();

        Assert.Equal(w.PersonId, stored.PersonId);
        Assert.Equal(AccessMethods.Qr, stored.Method);
        Assert.Equal(new DateOnly(2026, 10, 27), stored.ServiceDate);
        Assert.Equal(0, stored.DistanceMeters);
        Assert.Null(stored.RecordedByUserId);
    }

    [Fact]
    public async Task The_same_service_cannot_be_registered_twice_in_a_day()
    {
        var w = await SeedAsync();

        Assert.Null((await CheckIn(w)).Error);
        Assert.Equal("already_registered", (await CheckIn(w)).Error);
        Assert.Equal(1, await EventCount(w));
    }

    [Fact]
    public async Task Another_service_on_the_same_day_is_allowed()
    {
        var w = await SeedAsync();

        using (var db = Reader(w))
        {
            db.AccessServices.Add(new AccessService
            {
                Id = Guid.NewGuid(),
                InstitutionId = w.InstitutionId,
                AccessPointId = w.PointId,
                Name = "Merienda",
                StartTime = new TimeOnly(13, 0),
                EndTime = new TimeOnly(15, 0)
            });
            await db.SaveChangesAsync();
        }

        Assert.Null((await CheckIn(w)).Error);

        w.Clock.Now = Noon.AddMinutes(90);
        var second = await CheckIn(w);

        Assert.Null(second.Error);
        Assert.Equal("Merienda", second.Value!.ServiceName);
        Assert.Equal(2, await EventCount(w));
    }

    [Fact]
    public async Task A_phone_too_far_away_is_refused()
    {
        var w = await SeedAsync();

        // 0.01 degrees of latitude are about 1.1 km.
        var result = await CheckIn(w, lat: Lat + 0.01);

        Assert.Equal("too_far", result.Error);
        Assert.Equal(0, await EventCount(w));
    }

    [Fact]
    public async Task An_inaccurate_location_is_refused()
    {
        var w = await SeedAsync();

        Assert.Equal("location_inaccurate", (await CheckIn(w, accuracy: 500)).Error);
    }

    [Fact]
    public async Task Outside_the_service_hours_nothing_is_registered()
    {
        var w = await SeedAsync();
        w.Clock.Now = Noon.AddHours(4);

        Assert.Equal("no_service_now", (await CheckIn(w)).Error);
        Assert.Equal(0, await EventCount(w));
    }

    [Fact]
    public async Task An_expired_qr_is_refused()
    {
        var w = await SeedAsync();
        var token = Qr(w);

        w.Clock.Now = Noon.AddMinutes(1);

        Assert.Equal("expired_qr", (await CheckIn(w, token)).Error);
    }

    [Fact]
    public async Task A_garbage_qr_is_refused()
    {
        var w = await SeedAsync();

        Assert.Equal("invalid_qr", (await CheckIn(w, "esto-no-es-un-qr")).Error);
        Assert.Equal("invalid_qr", (await CheckIn(w, "")).Error);
    }

    [Fact]
    public async Task A_qr_of_another_institution_is_refused()
    {
        var w = await SeedAsync();

        Assert.Equal("invalid_qr", (await CheckIn(w, Qr(w, institutionId: Guid.NewGuid()))).Error);
    }

    [Fact]
    public async Task Impossible_coordinates_are_refused()
    {
        var w = await SeedAsync();

        Assert.Equal("invalid_location", (await CheckIn(w, lat: 200)).Error);
        Assert.Equal("invalid_location", (await CheckIn(w, accuracy: -1)).Error);
    }

    [Fact]
    public async Task A_user_who_is_not_a_student_cannot_check_in()
    {
        var w = await SeedAsync(withProfile: false);

        Assert.Equal("student_unavailable", (await CheckIn(w)).Error);
    }

    [Fact]
    public async Task A_point_without_a_location_refuses_check_ins()
    {
        var w = await SeedAsync(latitude: null);

        Assert.Equal("point_not_configured", (await CheckIn(w)).Error);
    }

    [Fact]
    public async Task An_inactive_point_refuses_check_ins()
    {
        var w = await SeedAsync(pointActive: false);

        Assert.Equal("invalid_qr", (await CheckIn(w)).Error);
    }

    [Fact]
    public async Task Platform_users_cannot_register_access()
    {
        var w = await SeedAsync();
        var service = ServiceFor(w, null, w.UserId);

        await Assert.ThrowsAsync<TenantViolationException>(() =>
            service.CheckInAsync(new CheckInCommand(Qr(w), Lat, Lon, 20)));
    }

    [Fact]
    public async Task A_manual_registration_records_who_made_it()
    {
        var w = await SeedAsync();

        var result = await Manual(w, "1-1111-1111");

        Assert.Null(result.Error);
        Assert.Equal("Ana", result.Value!.FirstName);

        using var db = Reader(w);
        var stored = await db.AccessEvents.SingleAsync();

        Assert.Equal(AccessMethods.Manual, stored.Method);
        Assert.NotNull(stored.RecordedByUserId);
        Assert.Null(stored.DistanceMeters);
    }

    [Fact]
    public async Task A_manual_registration_cannot_duplicate_a_check_in()
    {
        var w = await SeedAsync();

        Assert.Null((await CheckIn(w)).Error);
        Assert.Equal("already_registered", (await Manual(w, "111111111")).Error);
        Assert.Equal(1, await EventCount(w));
    }

    [Fact]
    public async Task A_manual_registration_for_an_unknown_cedula_is_refused()
    {
        var w = await SeedAsync();

        Assert.Equal("student_not_found", (await Manual(w, "0-0000-0000")).Error);
        Assert.Equal("student_not_found", (await Manual(w, "  ")).Error);
    }

    [Fact]
    public async Task A_manual_registration_cannot_reach_students_of_other_institutions()
    {
        var w = await SeedAsync();
        var other = Guid.NewGuid();
        var person = Guid.NewGuid();

        using (var db = Reader(w))
        {
            db.Institutions.Add(new Institution { Id = other, Name = "Colegio B", Slug = "demo-b", TimeZone = "UTC" });
            db.People.Add(new Person
            {
                Id = person,
                InstitutionId = other,
                IdentificationNumber = "999999999",
                FirstName = "Zoe",
                LastName = "Otra",
                CreatedAt = Noon
            });
            db.StudentProfiles.Add(new StudentProfile { PersonId = person, InstitutionId = other });
            await db.SaveChangesAsync();
        }

        Assert.Equal("student_not_found", (await Manual(w, "9-9999-9999")).Error);
    }

    [Fact]
    public async Task A_manual_registration_outside_the_hours_is_refused()
    {
        var w = await SeedAsync();
        w.Clock.Now = Noon.AddHours(4);

        Assert.Equal("no_service_now", (await Manual(w, "111111111")).Error);
    }

    [Fact]
    public async Task The_event_list_shows_only_this_institution_and_day()
    {
        var w = await SeedAsync();
        Assert.Null((await CheckIn(w)).Error);

        var other = Guid.NewGuid();
        var otherPoint = Guid.NewGuid();
        var otherService = Guid.NewGuid();
        var otherPerson = Guid.NewGuid();

        using (var db = Reader(w))
        {
            db.Institutions.Add(new Institution { Id = other, Name = "Colegio B", Slug = "demo-b", TimeZone = "UTC" });
            db.AccessPoints.Add(new AccessPoint
            {
                Id = otherPoint,
                InstitutionId = other,
                Name = "Comedor",
                Kind = AccessPointKinds.Cafeteria,
                CreatedAt = Noon
            });
            db.AccessServices.Add(new AccessService
            {
                Id = otherService,
                InstitutionId = other,
                AccessPointId = otherPoint,
                Name = "Almuerzo",
                StartTime = new TimeOnly(11, 0),
                EndTime = new TimeOnly(13, 0)
            });
            db.People.Add(new Person
            {
                Id = otherPerson,
                InstitutionId = other,
                IdentificationNumber = "999999999",
                FirstName = "Zoe",
                LastName = "Otra",
                CreatedAt = Noon
            });
            db.AccessEvents.Add(new AccessEvent
            {
                Id = Guid.NewGuid(),
                InstitutionId = other,
                AccessPointId = otherPoint,
                AccessServiceId = otherService,
                PersonId = otherPerson,
                ServiceDate = new DateOnly(2026, 10, 27),
                OccurredAt = Noon,
                Method = AccessMethods.Qr
            });
            await db.SaveChangesAsync();
        }

        var service = ServiceFor(w, w.InstitutionId, Guid.NewGuid());

        var today = await service.ListEventsAsync(null, null, 0, 50);
        var item = Assert.Single(today.Items);

        Assert.Equal(1, today.Total);
        Assert.Equal("Ana", item.FirstName);
        Assert.Equal("Almuerzo", item.ServiceName);

        var yesterday = await service.ListEventsAsync(null, new DateOnly(2026, 10, 26), 0, 50);
        Assert.Equal(0, yesterday.Total);
    }
}
