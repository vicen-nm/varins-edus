using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;

namespace VarinsEdu.Domain.Entities;

// A visitor who asked to come in through the public form. The guard admits them, and later registers the exit.
// The data is personal: it is redacted in the audit log, and the signature is only given to staff who may see it.
public class Visit : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid InstitutionId { get; set; }

    // The institution's local day of the request.
    public DateOnly ServiceDate { get; set; }

    public string FullName { get; set; } = string.Empty;

    // Normalized: letters and digits in upper case. Any document: cédula, passport...
    public string IdentificationNumber { get; set; } = string.Empty;
    public string? Phone { get; set; }

    // Who the visitor comes to see, as the visitor wrote it.
    public string Host { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;

    // A PNG image of the signature.
    public byte[] SignaturePng { get; set; } = [];

    public string Status { get; set; } = VisitStatuses.Pending;

    public DateTimeOffset CreatedAt { get; set; }

    // The guard who admitted or rejected the visit.
    public Guid? HandledByUserId { get; set; }
    public DateTimeOffset? EnteredAt { get; set; }

    public DateTimeOffset? ExitedAt { get; set; }
    public Guid? ExitedByUserId { get; set; }
}
