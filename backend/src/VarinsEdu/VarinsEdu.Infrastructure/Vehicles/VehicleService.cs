using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Access;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Infrastructure.Vehicles;

// Students' vehicles: a student asks, the staff decide. The tenant filter limits every query to the caller's institution.
public class VehicleService(AppDbContext db, ICurrentTenant tenant, ICurrentUser currentUser, TimeProvider time)
{
    public const int MaxActivePerStudent = 2;
    public const int MaxNoteLength = 200;

    private sealed record Cleaned(string Plate, string Make, string Model, string Color);

    // ---- The student's own vehicles ----

    public async Task<Outcome<IReadOnlyList<VehicleDto>>> ListMineAsync(CancellationToken ct = default)
    {
        Require();

        if (await OwnStudentAsync(ct) is not { } personId)
        {
            return Outcome<IReadOnlyList<VehicleDto>>.Fail("vehicle_student_unavailable");
        }

        var rows = await db.Vehicles
            .AsNoTracking()
            .Where(v => v.PersonId == personId)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(ct);

        return Outcome<IReadOnlyList<VehicleDto>>.Ok(rows.Select(ToDto).ToList());
    }

    public async Task<Outcome<VehicleDto>> RequestAsync(VehicleInput input, CancellationToken ct = default)
    {
        Require();

        var userId = RequireUser();

        if (await OwnStudentAsync(ct) is not { } personId)
        {
            return Outcome<VehicleDto>.Fail("vehicle_student_unavailable");
        }

        if (Clean(input.Plate, input.Make, input.Model, input.Color) is not { } cleaned)
        {
            return Outcome<VehicleDto>.Fail(InvalidCode(input.Plate));
        }

        return await CreateAsync(personId, cleaned, userId, approveNow: false, ct);
    }

    // The student withdraws a vehicle of their own: its plate is free again.
    public async Task<Outcome<bool>> WithdrawAsync(Guid id, CancellationToken ct = default)
    {
        Require();

        if (await OwnStudentAsync(ct) is not { } personId)
        {
            return Outcome<bool>.Fail("vehicle_student_unavailable");
        }

        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id && v.PersonId == personId, ct);

        if (vehicle is null)
        {
            return Outcome<bool>.Fail("vehicle_not_found");
        }

        if (!VehicleStatuses.Active.Contains(vehicle.Status))
        {
            return Outcome<bool>.Fail("invalid_status_change");
        }

        vehicle.Status = VehicleStatuses.Withdrawn;
        vehicle.StatusNote = null;

        await db.SaveChangesAsync(ct);

        return Outcome<bool>.Ok(true);
    }

    // ---- The staff ----

    public async Task<VehiclePage> ListAsync(string? status, string? search, int skip, int take, CancellationToken ct = default)
    {
        Require();

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(skip, 0);

        var query = db.Vehicles.AsNoTracking().AsQueryable();

        if (status is not null && VehicleStatuses.All.Contains(status))
        {
            query = query.Where(v => v.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            var plateTerm = NormalizePlate(search);

            query = query.Where(v =>
                (v.Person.FirstName + " " + v.Person.LastName).ToLower().Contains(term) ||
                (plateTerm.Length > 0 && v.Plate.Contains(plateTerm)));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(v => v.CreatedAt)
            .ThenBy(v => v.Id)
            .Skip(skip)
            .Take(take)
            .Select(v => new VehicleListItem(
                v.Id,
                v.Plate,
                v.Make,
                v.Model,
                v.Color,
                v.Status,
                v.StatusNote,
                v.Person.FirstName,
                v.Person.LastName,
                v.Person.StudentProfile != null && v.Person.StudentProfile.Group != null
                    ? v.Person.StudentProfile.Group.Name
                    : null,
                v.CreatedAt,
                v.ReviewedAt,
                db.Users.Where(u => u.Id == v.ReviewedByUserId).Select(u => u.Username).FirstOrDefault()))
            .ToListAsync(ct);

        return new VehiclePage(total, items);
    }

    // For a student without a phone, or when the documents are in hand at the desk.
    public async Task<Outcome<VehicleDto>> RegisterForStudentAsync(StaffVehicleInput input, CancellationToken ct = default)
    {
        Require();

        var staffId = RequireUser();

        if (Clean(input.Plate, input.Make, input.Model, input.Color) is not { } cleaned)
        {
            return Outcome<VehicleDto>.Fail(InvalidCode(input.Plate));
        }

        var number = IdentificationNumbers.Normalize(input.IdentificationNumber);

        if (number.Length == 0)
        {
            return Outcome<VehicleDto>.Fail("student_not_found");
        }

        var personId = await db.StudentProfiles
            .Where(s => s.Person.IdentificationNumber == number
                        && s.Status == StudentStatuses.Active
                        && s.Person.IsActive)
            .Select(s => (Guid?)s.PersonId)
            .FirstOrDefaultAsync(ct);

        if (personId is not { } pid)
        {
            return Outcome<VehicleDto>.Fail("student_not_found");
        }

        return await CreateAsync(pid, cleaned, staffId, input.ApproveNow, ct);
    }

    // Allowed changes: pending to approved or rejected, approved to suspended, suspended back to approved.
    // Rejecting and suspending need a note. A student withdraws their own vehicle; the staff cannot.
    public async Task<Outcome<bool>> SetStatusAsync(Guid id, StatusInput input, CancellationToken ct = default)
    {
        Require();

        var staffId = RequireUser();
        var target = input.Status?.Trim();

        if (target is not (VehicleStatuses.Approved or VehicleStatuses.Rejected or VehicleStatuses.Suspended))
        {
            return Outcome<bool>.Fail("invalid_status");
        }

        var note = input.Note?.Trim();

        if (note is { Length: > MaxNoteLength })
        {
            return Outcome<bool>.Fail("note_too_long");
        }

        if (target != VehicleStatuses.Approved && string.IsNullOrEmpty(note))
        {
            return Outcome<bool>.Fail("note_required");
        }

        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id, ct);

        if (vehicle is null)
        {
            return Outcome<bool>.Fail("vehicle_not_found");
        }

        var allowed = target switch
        {
            VehicleStatuses.Approved => vehicle.Status is VehicleStatuses.Pending or VehicleStatuses.Suspended,
            VehicleStatuses.Rejected => vehicle.Status is VehicleStatuses.Pending,
            _ => vehicle.Status is VehicleStatuses.Approved
        };

        if (!allowed)
        {
            return Outcome<bool>.Fail("invalid_status_change");
        }

        vehicle.Status = target;
        vehicle.StatusNote = string.IsNullOrEmpty(note) ? null : note;
        vehicle.ReviewedByUserId = staffId;
        vehicle.ReviewedAt = time.GetUtcNow();

        await db.SaveChangesAsync(ct);

        return Outcome<bool>.Ok(true);
    }

    // ---- Shared ----

    private async Task<Outcome<VehicleDto>> CreateAsync(
        Guid personId,
        Cleaned cleaned,
        Guid createdBy,
        bool approveNow,
        CancellationToken ct)
    {
        var institutionId = Require();
        var active = VehicleStatuses.Active;

        if (await db.Vehicles.CountAsync(v => v.PersonId == personId && active.Contains(v.Status), ct) >= MaxActivePerStudent)
        {
            return Outcome<VehicleDto>.Fail("vehicle_limit_reached");
        }

        if (await PlateTakenAsync(cleaned.Plate, ct))
        {
            return Outcome<VehicleDto>.Fail("plate_taken");
        }

        var now = time.GetUtcNow();

        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            InstitutionId = institutionId,
            PersonId = personId,
            Plate = cleaned.Plate,
            Make = cleaned.Make,
            Model = cleaned.Model,
            Color = cleaned.Color,
            Status = approveNow ? VehicleStatuses.Approved : VehicleStatuses.Pending,
            CreatedByUserId = createdBy,
            CreatedAt = now,
            ReviewedByUserId = approveNow ? createdBy : null,
            ReviewedAt = approveNow ? now : null
        };

        db.Vehicles.Add(vehicle);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.Entry(vehicle).State = EntityState.Detached;

            // Two requests with the same plate at once: the unique index let only one through.
            if (await PlateTakenAsync(cleaned.Plate, ct))
            {
                return Outcome<VehicleDto>.Fail("plate_taken");
            }

            throw;
        }

        return Outcome<VehicleDto>.Ok(ToDto(vehicle));
    }

    private Task<bool> PlateTakenAsync(string plate, CancellationToken ct)
    {
        var active = VehicleStatuses.Active;

        return db.Vehicles.AnyAsync(v => v.Plate == plate && active.Contains(v.Status), ct);
    }

    // The caller must be an active student with an active account: the person the vehicles belong to.
    private async Task<Guid?> OwnStudentAsync(CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId)
        {
            return null;
        }

        var personId = await db.Users
            .Where(u => u.Id == userId && u.IsActive)
            .Select(u => u.PersonId)
            .FirstOrDefaultAsync(ct);

        if (personId is not { } pid)
        {
            return null;
        }

        var isStudent = await db.StudentProfiles
            .AnyAsync(s => s.PersonId == pid && s.Status == StudentStatuses.Active && s.Person.IsActive, ct);

        return isStudent ? pid : null;
    }

    // Letters and digits in upper case, so "mot-123 456" and "MOT123456" are the same plate.
    private static string NormalizePlate(string? plate) =>
        new string((plate ?? string.Empty).Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();

    private static Cleaned? Clean(string? plate, string? make, string? model, string? color)
    {
        var normalized = NormalizePlate(plate);
        var m = make?.Trim() ?? string.Empty;
        var mo = model?.Trim() ?? string.Empty;
        var c = color?.Trim() ?? string.Empty;

        if (normalized.Length is < 3 or > 10)
        {
            return null;
        }

        if (m.Length is 0 or > 60 || mo.Length is 0 or > 60 || c.Length is 0 or > 40)
        {
            return null;
        }

        return new Cleaned(normalized, m, mo, c);
    }

    // The code for a vehicle that failed cleaning: the plate is checked first.
    private static string InvalidCode(string? plate) =>
        NormalizePlate(plate).Length is < 3 or > 10 ? "invalid_plate" : "invalid_vehicle_data";

    private static VehicleDto ToDto(Vehicle v) =>
        new(v.Id, v.Plate, v.Make, v.Model, v.Color, v.Status, v.StatusNote, v.CreatedAt, v.ReviewedAt);

    private Guid Require() =>
        tenant.InstitutionId ?? throw new TenantViolationException("Only institution users can manage vehicles.");

    private Guid RequireUser() =>
        currentUser.UserId ?? throw new InvalidOperationException("An authenticated user is required.");
}
