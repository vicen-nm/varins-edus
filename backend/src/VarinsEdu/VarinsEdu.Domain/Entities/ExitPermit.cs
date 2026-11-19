using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;

namespace VarinsEdu.Domain.Entities;

// Authorization for a student to leave the school on a day, within a window. It works once.
public class ExitPermit : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid InstitutionId { get; set; }

    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;

    // The institution's local day and window. The window is [FromTime, ToTime).
    public DateOnly ServiceDate { get; set; }
    public TimeOnly FromTime { get; set; }
    public TimeOnly ToTime { get; set; }

    // Seen by the staff who manage permits, never by the guard. It can be sensitive.
    public string Reason { get; set; } = string.Empty;

    public string Status { get; set; } = ExitPermitStatuses.Active;

    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CancelledByUserId { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    // The guard who registered the exit, and when.
    public Guid? UsedByUserId { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}
