using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Auditing;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;
using VarinsEdu.Infrastructure.Seeding;
using VarinsEdu.Infrastructure.Summary;
using Xunit;

namespace VarinsEdu.Tests;

public class DemoDataSeederTests
{
    // Monday 10:00 in Costa Rica.
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 23);
    private static readonly PasswordHasherService Hasher = new();
    private static readonly ActivationCodeHasher CodeHasher = new(new string('k', 48));

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static DbContextOptions<AppDbContext> NewOptions() =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static DemoDataSeeder SeederFor(DbContextOptions<AppDbContext> options) =>
        new(
            options,
            new DatabaseSeeder(options, Hasher, new SeedOptions(), NullLogger<DatabaseSeeder>.Instance),
            Hasher,
            CodeHasher,
            new AuditSuppression(),
            new FakeClock(Now));

    [Fact]
    public async Task The_demo_school_is_complete_and_its_credentials_work()
    {
        var options = NewOptions();

        var report = await SeederFor(options).RunAsync();

        Assert.NotNull(report);
        Assert.Equal("liceo-demo", report!.Slug);
        Assert.Equal(13, report.Accounts.Count);
        Assert.Equal(4, report.Activations.Count);

        using var db = new AppDbContext(options, new PlatformTenant());
        var institution = await db.Institutions.SingleAsync(i => i.Slug == "liceo-demo");

        Assert.Equal("America/Costa_Rica", institution.TimeZone);
        Assert.Equal(12, await db.People.CountAsync());
        Assert.Equal(3, await db.Groups.CountAsync());
        Assert.Equal(3, await db.AccessServices.CountAsync());
        Assert.Equal(7, await db.Vehicles.CountAsync());
        Assert.All(await db.InstitutionModules.ToListAsync(), m => Assert.True(m.IsEnabled));

        // Staff and students sign in with the shared demo password.
        var director = await db.Users.SingleAsync(u => u.Username == "director@liceo-demo");
        var student = await db.Users.SingleAsync(u => u.Username == "900000001@liceo-demo");

        Assert.True(Hasher.Verify(director.PasswordHash, report.Password));
        Assert.True(Hasher.Verify(student.PasswordHash, report.Password));

        // The students without an account have a working code and no user.
        foreach (var activation in report.Activations)
        {
            var person = await db.People.SingleAsync(p => p.IdentificationNumber == activation.Identification);
            var stored = await db.ActivationCodes.SingleAsync(c => c.PersonId == person.Id);

            Assert.True(CodeHasher.Verify(stored.CodeHash, activation.Code));
            Assert.False(await db.Users.AnyAsync(u => u.PersonId == person.Id));
        }

        // The drawn signatures are real PNG images of a sensible size.
        foreach (var visit in await db.Visits.ToListAsync())
        {
            Assert.InRange(visit.SignaturePng.Length, 100, 60 * 1024);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, visit.SignaturePng.Take(4).ToArray());
        }

        // What the summary will show while recording.
        var tenant = new InstitutionTenant(institution.Id);
        var summary = await new SummaryService(new AppDbContext(options, tenant), tenant, new FakeClock(Now))
            .GetAsync(new SummaryScope(true, true, true, true, true, true, true));

        Assert.Equal(0, summary.Access!.Total);
        Assert.Equal(new ParkingSummary(1, 2, 1, 1), summary.Parking);
        Assert.Equal(new VisitsSummary(3, 1, 1), summary.Visits);
        Assert.Equal(new ExitsSummary(2, 1, 1), summary.Exits);
        Assert.Equal(new VehiclesSummary(2, 3, 1), summary.Vehicles);
        Assert.Equal(new StudentsSummary(12, 8, 4), summary.Students);
        Assert.Equal(new SecuritySummary(4, 1, 1, 0), summary.Security);

        // Only the entries written by hand: the demo rows themselves are not audited.
        Assert.Equal(14, await db.AuditLogs.CountAsync(a => a.InstitutionId == institution.Id));
    }

    [Fact]
    public async Task The_cafeteria_history_is_on_school_days_never_today_and_never_repeats()
    {
        var options = NewOptions();
        await SeederFor(options).RunAsync();

        using var db = new AppDbContext(options, new PlatformTenant());
        var events = await db.AccessEvents.ToListAsync();

        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.True(e.ServiceDate < Today));
        Assert.All(events, e => Assert.NotEqual(DayOfWeek.Saturday, e.ServiceDate.DayOfWeek));
        Assert.All(events, e => Assert.NotEqual(DayOfWeek.Sunday, e.ServiceDate.DayOfWeek));
        Assert.Equal(5, events.Select(e => e.ServiceDate).Distinct().Count());

        // One registration per student, service and day.
        Assert.Equal(events.Count, events.Select(e => (e.AccessServiceId, e.PersonId, e.ServiceDate)).Distinct().Count());

        var staffId = (await db.Users.SingleAsync(u => u.Username == "comedor.1@liceo-demo")).Id;

        Assert.All(events.Where(e => e.Method == AccessMethods.Manual), e => Assert.Equal(staffId, e.RecordedByUserId));
        Assert.All(events.Where(e => e.Method == AccessMethods.Qr), e => Assert.NotNull(e.DistanceMeters));
    }

    [Fact]
    public async Task Running_it_again_changes_nothing()
    {
        var options = NewOptions();

        Assert.NotNull(await SeederFor(options).RunAsync());
        Assert.Null(await SeederFor(options).RunAsync());

        using var db = new AppDbContext(options, new PlatformTenant());

        Assert.Equal(1, await db.Institutions.CountAsync(i => i.Slug == "liceo-demo"));
        Assert.Equal(12, await db.People.CountAsync());
        Assert.Equal(7, await db.Vehicles.CountAsync());
    }
}
