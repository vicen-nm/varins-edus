using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Auditing;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;

namespace VarinsEdu.Infrastructure.Access;

// Registers who was served: by QR from the student's phone, or manually by staff.
public class AccessRegistrationService(
    AppDbContext db,
    ICurrentTenant tenant,
    ICurrentUser currentUser,
    AccessPointTokenService tokens,
    AuditSuppression auditSuppression,
    TimeProvider time)
{
    private sealed record StudentRef(Guid PersonId, string FirstName, string LastName);

    // Checks run from cheapest to most specific. The location checks come before the "already
    // registered" check, so a far-away phone never learns whether the student was registered.
    public async Task<Outcome<RegistrationResult>> CheckInAsync(CheckInCommand command, CancellationToken ct = default)
    {
        var institutionId = Require();

        if (currentUser.UserId is not { } userId)
        {
            return Fail("student_unavailable");
        }

        if (!GeoDistance.IsValid(command.Latitude, command.Longitude)
            || !double.IsFinite(command.AccuracyMeters)
            || command.AccuracyMeters < 0)
        {
            return Fail("invalid_location");
        }

        var check = tokens.Verify(command.Token);

        if (check.Status == AccessTokenStatus.Expired)
        {
            return Fail("expired_qr");
        }

        if (check.Status != AccessTokenStatus.Valid || check.InstitutionId != institutionId)
        {
            return Fail("invalid_qr");
        }

        var personId = await db.Users
            .Where(u => u.Id == userId && u.IsActive)
            .Select(u => u.PersonId)
            .FirstOrDefaultAsync(ct);

        if (personId is not { } pid)
        {
            return Fail("student_unavailable");
        }

        var student = await FindStudentAsync(s => s.PersonId == pid, ct);

        if (student is null)
        {
            return Fail("student_unavailable");
        }

        var pointId = check.PointId!.Value;
        var point = await db.AccessPoints.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pointId && p.IsActive, ct);

        if (point is null)
        {
            return Fail("invalid_qr");
        }

        if (point.Latitude is not { } lat || point.Longitude is not { } lon)
        {
            return Fail("point_not_configured");
        }

        if (command.AccuracyMeters > point.MaxAccuracyMeters)
        {
            return Fail("location_inaccurate");
        }

        var distance = GeoDistance.Meters(command.Latitude, command.Longitude, lat, lon);

        if (distance > point.RadiusMeters)
        {
            return Fail("too_far");
        }

        return await RecordAsync(institutionId, point, student, AccessMethods.Qr, null, (int)Math.Round(distance), ct);
    }

    // The backup when the phone or the GPS fails: staff type the student's cédula.
    public async Task<Outcome<RegistrationResult>> RegisterManuallyAsync(
        Guid pointId,
        string? identificationNumber,
        CancellationToken ct = default)
    {
        var institutionId = Require();

        var staffId = currentUser.UserId
            ?? throw new InvalidOperationException("An authenticated user is required to register manually.");

        var point = await db.AccessPoints.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pointId && p.IsActive, ct);

        if (point is null)
        {
            return Fail("access_point_not_found");
        }

        // Cédulas are stored without hyphens and in upper case.
        var number = string.IsNullOrWhiteSpace(identificationNumber)
            ? string.Empty
            : new string(identificationNumber.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

        if (number.Length == 0)
        {
            return Fail("student_not_found");
        }

        var student = await FindStudentAsync(s => s.Person.IdentificationNumber == number, ct);

        if (student is null)
        {
            return Fail("student_not_found");
        }

        return await RecordAsync(institutionId, point, student, AccessMethods.Manual, staffId, null, ct);
    }

    // Defaults to today in the institution's time zone.
    public async Task<AccessEventPage> ListEventsAsync(
        Guid? pointId,
        DateOnly? date,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        Require();

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(skip, 0);

        var day = date ?? DateOnly.FromDateTime(await LocalNowAsync(ct));

        var query = db.AccessEvents.AsNoTracking().Where(e => e.ServiceDate == day);

        if (pointId is { } id)
        {
            query = query.Where(e => e.AccessPointId == id);
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(e => e.OccurredAt)
            .Skip(skip)
            .Take(take)
            .Select(e => new AccessEventItem(
                e.Id,
                e.AccessPoint.Name,
                e.AccessService.Name,
                e.Person.FirstName,
                e.Person.LastName,
                e.Method,
                e.OccurredAt,
                e.ServiceDate))
            .ToListAsync(ct);

        return new AccessEventPage(total, items);
    }

    private async Task<Outcome<RegistrationResult>> RecordAsync(
        Guid institutionId,
        AccessPoint point,
        StudentRef student,
        string method,
        Guid? recordedBy,
        int? distance,
        CancellationToken ct)
    {
        var local = await LocalNowAsync(ct);
        var localTime = TimeOnly.FromDateTime(local);
        var date = DateOnly.FromDateTime(local);

        var service = await db.AccessServices
            .AsNoTracking()
            .Where(s => s.AccessPointId == point.Id && s.IsActive && s.StartTime <= localTime && localTime < s.EndTime)
            .OrderBy(s => s.StartTime)
            .FirstOrDefaultAsync(ct);

        if (service is null)
        {
            return Fail("no_service_now");
        }

        if (await Exists(service.Id, student.PersonId, date, ct))
        {
            return Fail("already_registered");
        }

        var now = time.GetUtcNow();

        var registration = new AccessEvent
        {
            Id = Guid.NewGuid(),
            InstitutionId = institutionId,
            AccessPointId = point.Id,
            AccessServiceId = service.Id,
            PersonId = student.PersonId,
            ServiceDate = date,
            OccurredAt = now,
            Method = method,
            RecordedByUserId = recordedBy,
            DistanceMeters = distance
        };

        db.AccessEvents.Add(registration);

        try
        {
            // The table is already the history of these events: they are not audited one by one.
            using (auditSuppression.Begin())
            {
                await db.SaveChangesAsync(ct);
            }
        }
        catch (DbUpdateException)
        {
            db.Entry(registration).State = EntityState.Detached;

            // Two requests at once: the unique index let only one through.
            if (await Exists(service.Id, student.PersonId, date, ct))
            {
                return Fail("already_registered");
            }

            throw;
        }

        return Outcome<RegistrationResult>.Ok(
            new RegistrationResult(student.FirstName, student.LastName, point.Name, service.Name, now));
    }

    private Task<bool> Exists(Guid serviceId, Guid personId, DateOnly date, CancellationToken ct) =>
        db.AccessEvents.AnyAsync(e => e.AccessServiceId == serviceId && e.PersonId == personId && e.ServiceDate == date, ct);

    private async Task<StudentRef?> FindStudentAsync(Expression<Func<StudentProfile, bool>> filter, CancellationToken ct) =>
        await db.StudentProfiles
            .AsNoTracking()
            .Where(filter)
            .Where(s => s.Status == StudentStatuses.Active && s.Person.IsActive)
            .Select(s => new StudentRef(s.PersonId, s.Person.FirstName, s.Person.LastName))
            .FirstOrDefaultAsync(ct);

    // The clock on the wall at the institution (the filter limits this to the caller's own).
    private async Task<DateTime> LocalNowAsync(CancellationToken ct)
    {
        var zoneId = await db.Institutions.Select(i => i.TimeZone).FirstOrDefaultAsync(ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(zoneId) ? "UTC" : zoneId);

        return TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime;
    }

    private static Outcome<RegistrationResult> Fail(string code) => Outcome<RegistrationResult>.Fail(code);

    private Guid Require() =>
        tenant.InstitutionId ?? throw new TenantViolationException("Only institution users can register access.");
}
