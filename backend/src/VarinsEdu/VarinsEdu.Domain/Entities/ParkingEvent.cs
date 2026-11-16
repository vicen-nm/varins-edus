using VarinsEdu.Domain.Common;

namespace VarinsEdu.Domain.Entities;

// One visit of a vehicle to the parking: it enters and, later, it exits.
// The table is also the history: these rows are not audited one by one.
public class ParkingEvent : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid InstitutionId { get; set; }

    public Guid VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;

    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;

    // The institution's local day of the entry.
    public DateOnly ServiceDate { get; set; }

    public DateTimeOffset EnteredAt { get; set; }
    public Guid EnteredByUserId { get; set; }

    public DateTimeOffset? ExitedAt { get; set; }
    public Guid? ExitedByUserId { get; set; }

    // A visit left open on a previous day: nobody registered the exit, so it was closed without one.
    public bool AutoClosed { get; set; }
}
