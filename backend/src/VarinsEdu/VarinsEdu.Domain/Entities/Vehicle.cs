using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;

namespace VarinsEdu.Domain.Entities;

// A vehicle (a motorcycle) of a student, with its approval by the staff.
// No photos, license or document numbers are stored: only what is needed to recognise it at the gate.
public class Vehicle : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid InstitutionId { get; set; }

    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;

    // Normalized: letters and digits in upper case.
    public string Plate { get; set; } = string.Empty;
    public string Make { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;

    public string Status { get; set; } = VehicleStatuses.Pending;

    // The reason of a rejection or suspension, shown to the student.
    public string? StatusNote { get; set; }

    // The student, or the staff member who registered it.
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // The staff member who made the last decision, and when.
    public Guid? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}
