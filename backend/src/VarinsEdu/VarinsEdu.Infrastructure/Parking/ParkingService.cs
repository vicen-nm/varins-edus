using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Access;
using VarinsEdu.Infrastructure.Auditing;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Students;

namespace VarinsEdu.Infrastructure.Parking;

// Entries and exits of students' motorcycles, identified with the one-time code of the student's card.
// This service and CardScanService share the request's database context, so the scan and the movement
// are saved together: the card is only used up if the movement is recorded.
public class ParkingService(
    AppDbContext db,
    ICurrentTenant tenant,
    ICurrentUser currentUser,
    CardScanService cards,
    AuditSuppression auditSuppression,
    TimeProvider time)
{
    public const string Entry = "entry";
    public const string Exit = "exit";
    public const string Inside = "inside";
    public const string Exited = "exited";
    public const string NoExit = "no_exit";

    // Shows the student and their approved vehicles. The card is checked but NOT used up.
    public async Task<Outcome<ParkingPreview>> PreviewAsync(string? token, CancellationToken ct = default)
    {
        Require();

        var verified = await cards.VerifyAsync(token, ct);

        if (verified.Error is { } error)
        {
            return Outcome<ParkingPreview>.Fail(error);
        }

        var card = verified.Value!;
        var today = DateOnly.FromDateTime(await LocalTime.NowAsync(db, time, ct));

        var vehicles = await db.Vehicles
            .AsNoTracking()
            .Where(v => v.PersonId == card.PersonId && v.Status == VehicleStatuses.Approved)
            .OrderBy(v => v.Plate)
            .ToListAsync(ct);

        if (vehicles.Count == 0)
        {
            return Outcome<ParkingPreview>.Fail("no_approved_vehicle");
        }

        var ids = vehicles.Select(v => v.Id).ToList();

        var open = await db.ParkingEvents
            .AsNoTracking()
            .Where(e => ids.Contains(e.VehicleId) && e.ExitedAt == null && !e.AutoClosed && e.ServiceDate == today)
            .Select(e => new { e.VehicleId, e.EnteredAt })
            .ToListAsync(ct);

        var items = vehicles
            .Select(v =>
            {
                var visit = open.FirstOrDefault(o => o.VehicleId == v.Id);

                return new ParkingVehicle(v.Id, v.Plate, v.Make, v.Model, v.Color, visit is not null, visit?.EnteredAt);
            })
            .ToList();

        return Outcome<ParkingPreview>.Ok(new ParkingPreview(card.Student, items));
    }

    // Registers the entry, or the exit if the vehicle is inside, and uses the card up.
    // Every check comes before the card is used: a refusal leaves the card usable.
    public async Task<Outcome<ParkingResult>> RegisterAsync(string? token, Guid vehicleId, CancellationToken ct = default)
    {
        var institutionId = Require();

        var staffId = currentUser.UserId
            ?? throw new InvalidOperationException("An authenticated user is required to register parking.");

        var verified = await cards.VerifyAsync(token, ct);

        if (verified.Error is { } error)
        {
            return Outcome<ParkingResult>.Fail(error);
        }

        var card = verified.Value!;

        // A vehicle that is not the student's looks just like one that does not exist.
        var vehicle = await db.Vehicles
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == vehicleId && v.PersonId == card.PersonId, ct);

        if (vehicle is null)
        {
            return Outcome<ParkingResult>.Fail("vehicle_not_found");
        }

        if (vehicle.Status != VehicleStatuses.Approved)
        {
            return Outcome<ParkingResult>.Fail("vehicle_not_approved");
        }

        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(await LocalTime.NowAsync(db, time, ct));

        var open = await db.ParkingEvents
            .FirstOrDefaultAsync(e => e.VehicleId == vehicle.Id && e.ExitedAt == null && !e.AutoClosed, ct);

        // A visit left open on a previous day was never closed: it is marked, and today starts a new one.
        // It is saved first, apart: two statements of one save could cross on the unique index.
        if (open is not null && open.ServiceDate != today)
        {
            open.AutoClosed = true;
            await SaveAsync(ct);
            open = null;
        }

        string action;

        if (open is not null)
        {
            open.ExitedAt = now;
            open.ExitedByUserId = staffId;
            action = Exit;
        }
        else
        {
            db.ParkingEvents.Add(new ParkingEvent
            {
                Id = Guid.NewGuid(),
                InstitutionId = institutionId,
                VehicleId = vehicle.Id,
                PersonId = card.PersonId,
                ServiceDate = today,
                EnteredAt = now,
                EnteredByUserId = staffId
            });
            action = Entry;
        }

        cards.Stage(card);

        try
        {
            await SaveAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();

            // Two scans of the same code at once: the unique index let only one through.
            if (await cards.IsUsedAsync(card.PersonId, card.Nonce, ct))
            {
                return await cards.ReusedAsync<ParkingResult>(card.PersonId, ct);
            }

            // Two entries of the same vehicle at once. Nothing was saved, so the card is still usable.
            return Outcome<ParkingResult>.Fail("parking_conflict");
        }

        return Outcome<ParkingResult>.Ok(new ParkingResult(action, card.Student, vehicle.Plate, now));
    }

    // Defaults to today in the institution's time zone.
    public async Task<ParkingPage> ListEventsAsync(DateOnly? date, int skip, int take, CancellationToken ct = default)
    {
        Require();

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(skip, 0);

        var today = DateOnly.FromDateTime(await LocalTime.NowAsync(db, time, ct));
        var day = date ?? today;

        var query = db.ParkingEvents.AsNoTracking().Where(e => e.ServiceDate == day);

        var total = await query.CountAsync(ct);
        var inside = await query.CountAsync(e => e.ExitedAt == null && !e.AutoClosed && e.ServiceDate == today, ct);

        var rows = await query
            .OrderByDescending(e => e.EnteredAt)
            .ThenBy(e => e.Id)
            .Skip(skip)
            .Take(take)
            .Select(e => new
            {
                e.Id,
                e.Vehicle.Plate,
                e.Person.FirstName,
                e.Person.LastName,
                Group = e.Person.StudentProfile != null && e.Person.StudentProfile.Group != null
                    ? e.Person.StudentProfile.Group.Name
                    : null,
                e.EnteredAt,
                e.ExitedAt,
                e.AutoClosed,
                e.ServiceDate
            })
            .ToListAsync(ct);

        var items = rows
            .Select(r => new ParkingEventItem(
                r.Id,
                r.Plate,
                r.FirstName,
                r.LastName,
                r.Group,
                r.EnteredAt,
                r.ExitedAt,
                r.ExitedAt is not null
                    ? Exited
                    : r.ServiceDate == today && !r.AutoClosed ? Inside : NoExit))
            .ToList();

        return new ParkingPage(total, inside, items);
    }

    // The parking movements are their own history: they are not audited one by one.
    private async Task SaveAsync(CancellationToken ct)
    {
        using (auditSuppression.Begin())
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private Guid Require() =>
        tenant.InstitutionId ?? throw new TenantViolationException("Only institution users can use the parking.");
}
