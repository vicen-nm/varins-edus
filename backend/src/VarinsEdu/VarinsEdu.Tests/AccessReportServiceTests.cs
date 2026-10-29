using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Access;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Seeding;
using Xunit;

namespace VarinsEdu.Tests;

public class AccessReportServiceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

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

    private static DbContextOptions<AppDbContext> NewOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static AccessReportService ServiceFor(
        DbContextOptions<AppDbContext> options,
        Guid? institutionId,
        Guid? userId = null)
    {
        var tenant = new FakeTenant(institutionId);

        return new AccessReportService(
            new AppDbContext(options, tenant),
            tenant,
            new FakeUser(userId ?? Guid.NewGuid()),
            new FakeClock(Noon));
    }

    // One institution (UTC) with a cafeteria, lunch and breakfast, Ana (group 10-1) and Beto (no group),
    // and four events: 10-27 lunch (Ana QR, Beto manual), 10-26 breakfast (Ana QR) and 10-01 lunch (Ana QR).
    private static async Task AddInstitutionAsync(DbContextOptions<AppDbContext> options, Guid inst, string slug)
    {
        using var db = new AppDbContext(options, new PlatformTenant());

        var point = Guid.NewGuid();
        var lunch = Guid.NewGuid();
        var breakfast = Guid.NewGuid();
        var group = Guid.NewGuid();
        var ana = Guid.NewGuid();
        var beto = Guid.NewGuid();

        db.Institutions.Add(new Institution { Id = inst, Name = "Colegio " + slug, Slug = slug, TimeZone = "UTC" });
        db.AccessPoints.Add(new AccessPoint
        {
            Id = point,
            InstitutionId = inst,
            Name = "Comedor",
            Kind = AccessPointKinds.Cafeteria,
            CreatedAt = Noon
        });
        db.AccessServices.AddRange(
            new AccessService
            {
                Id = lunch,
                InstitutionId = inst,
                AccessPointId = point,
                Name = "Almuerzo",
                StartTime = new TimeOnly(11, 0),
                EndTime = new TimeOnly(13, 0)
            },
            new AccessService
            {
                Id = breakfast,
                InstitutionId = inst,
                AccessPointId = point,
                Name = "Desayuno",
                StartTime = new TimeOnly(6, 0),
                EndTime = new TimeOnly(8, 0)
            });
        db.Groups.Add(new Group { Id = group, InstitutionId = inst, Name = "10-1" });
        db.People.AddRange(
            new Person
            {
                Id = ana,
                InstitutionId = inst,
                IdentificationNumber = "111111111",
                FirstName = "Ana",
                LastName = "Prueba Uno",
                CreatedAt = Noon
            },
            new Person
            {
                Id = beto,
                InstitutionId = inst,
                IdentificationNumber = "222222222",
                FirstName = "Beto",
                LastName = "Prueba Dos",
                CreatedAt = Noon
            });
        db.StudentProfiles.AddRange(
            new StudentProfile { PersonId = ana, InstitutionId = inst, GroupId = group },
            new StudentProfile { PersonId = beto, InstitutionId = inst });

        AccessEvent Event(DateOnly date, Guid service, Guid person, string method) => new()
        {
            Id = Guid.NewGuid(),
            InstitutionId = inst,
            AccessPointId = point,
            AccessServiceId = service,
            PersonId = person,
            ServiceDate = date,
            OccurredAt = Noon,
            Method = method
        };

        db.AccessEvents.AddRange(
            Event(new DateOnly(2026, 10, 27), lunch, ana, AccessMethods.Qr),
            Event(new DateOnly(2026, 10, 27), lunch, beto, AccessMethods.Manual),
            Event(new DateOnly(2026, 10, 26), breakfast, ana, AccessMethods.Qr),
            Event(new DateOnly(2026, 10, 1), lunch, ana, AccessMethods.Qr));

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_report_counts_by_day_service_method_and_group()
    {
        var options = NewOptions();
        var inst = Guid.NewGuid();
        await AddInstitutionAsync(options, inst, "demo-a");

        var report = (await ServiceFor(options, inst).BuildAsync(new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 27))).Value!;

        Assert.Equal(new ReportTotals(3, 2, 1), report.Totals);

        Assert.Collection(
            report.ByDay,
            d =>
            {
                Assert.Equal(new DateOnly(2026, 10, 26), d.Date);
                Assert.Equal("Desayuno", d.Service);
                Assert.Equal(1, d.Total);
            },
            d =>
            {
                Assert.Equal(new DateOnly(2026, 10, 27), d.Date);
                Assert.Equal("Almuerzo", d.Service);
                Assert.Equal(2, d.Total);
                Assert.Equal(1, d.Qr);
                Assert.Equal(1, d.Manual);
            });

        Assert.Equal("10-1", report.ByGroup[0].Group);
        Assert.Equal(2, report.ByGroup[0].Total);
        Assert.Null(report.ByGroup[1].Group);
        Assert.Equal(1, report.ByGroup[1].Total);

        Assert.Equal(3, report.Details.Count);
    }

    [Fact]
    public async Task Without_dates_the_report_covers_the_last_seven_days()
    {
        var options = NewOptions();
        var inst = Guid.NewGuid();
        await AddInstitutionAsync(options, inst, "demo-a");

        var report = (await ServiceFor(options, inst).BuildAsync(null, null)).Value!;

        Assert.Equal(new DateOnly(2026, 10, 21), report.From);
        Assert.Equal(new DateOnly(2026, 10, 27), report.To);

        // The event of October 1st is outside the range.
        Assert.Equal(3, report.Totals.Total);
    }

    [Fact]
    public async Task Invalid_ranges_are_rejected_and_the_limit_is_92_days()
    {
        var service = ServiceFor(NewOptions(), Guid.NewGuid());
        var to = new DateOnly(2026, 10, 27);

        Assert.Equal("invalid_date_range", (await service.BuildAsync(to, to.AddDays(-1))).Error);
        Assert.Equal("range_too_long", (await service.BuildAsync(to.AddDays(-92), to)).Error);
        Assert.Null((await service.BuildAsync(to.AddDays(-91), to)).Error);
    }

    [Fact]
    public async Task The_report_only_counts_the_callers_institution()
    {
        var options = NewOptions();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await AddInstitutionAsync(options, a, "demo-a");
        await AddInstitutionAsync(options, b, "demo-b");

        var report = (await ServiceFor(options, a).BuildAsync(null, null)).Value!;

        Assert.Equal(3, report.Totals.Total);
        Assert.Equal(3, report.Details.Count);
    }

    [Fact]
    public async Task Platform_users_cannot_get_reports()
    {
        var service = ServiceFor(NewOptions(), null);

        await Assert.ThrowsAsync<TenantViolationException>(() => service.BuildAsync(null, null));
        await Assert.ThrowsAsync<TenantViolationException>(() => service.ExportAsync(null, null));
    }

    [Fact]
    public async Task The_export_has_a_summary_and_a_detail_with_the_cedula_as_text()
    {
        var options = NewOptions();
        var inst = Guid.NewGuid();
        await AddInstitutionAsync(options, inst, "demo-a");

        var file = (await ServiceFor(options, inst).ExportAsync(null, null)).Value!;

        Assert.Equal("comedor-2026-10-21-2026-10-27.xlsx", file.FileName);

        using var workbook = new XLWorkbook(new MemoryStream(file.Bytes));

        Assert.Equal(new[] { "Resumen", "Detalle" }, workbook.Worksheets.Select(w => w.Name).ToArray());

        var detail = workbook.Worksheet("Detalle");

        Assert.Equal("Cédula", detail.Cell(1, 3).GetString());
        Assert.Equal(4, detail.LastRowUsed()!.RowNumber());
        Assert.Equal(XLDataType.Text, detail.Cell(2, 3).DataType);

        var cedulas = Enumerable.Range(2, 3).Select(r => detail.Cell(r, 3).GetString()).ToList();

        Assert.Contains("111111111", cedulas);
        Assert.Contains("222222222", cedulas);

        Assert.Equal("Reporte del comedor", workbook.Worksheet("Resumen").Cell(1, 1).GetString());
    }

    [Fact]
    public async Task Every_export_is_audited_without_personal_data()
    {
        var options = NewOptions();
        var inst = Guid.NewGuid();
        var user = Guid.NewGuid();
        await AddInstitutionAsync(options, inst, "demo-a");

        await ServiceFor(options, inst, user).ExportAsync(null, null);

        using var db = new AppDbContext(options, new PlatformTenant());
        var audit = await db.AuditLogs.SingleAsync(a => a.Action == "AccessReport.exported");

        Assert.Equal(user, audit.UserId);
        Assert.Equal(inst, audit.InstitutionId);
        Assert.Contains("2026-10-21", audit.Metadata);
        Assert.DoesNotContain("111111111", audit.Metadata);
        Assert.DoesNotContain("Ana", audit.Metadata);
    }
}
