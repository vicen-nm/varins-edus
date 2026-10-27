using VarinsEdu.Domain.Common;

namespace VarinsEdu.Domain.Entities;

// One registration: this person was served at this point, in this service, on this day.
public class AccessEvent : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid InstitutionId { get; set; }

    public Guid AccessPointId { get; set; }
    public AccessPoint AccessPoint { get; set; } = null!;

    public Guid AccessServiceId { get; set; }
    public AccessService AccessService { get; set; } = null!;

    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;

    // The institution's local day, used by the "one per service per day" rule.
    public DateOnly ServiceDate { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    public string Method { get; set; } = string.Empty;

    // Set only for manual records: the staff member who made them.
    public Guid? RecordedByUserId { get; set; }

    // Distance reported by the phone, for QR check-ins.
    public int? DistanceMeters { get; set; }
}
