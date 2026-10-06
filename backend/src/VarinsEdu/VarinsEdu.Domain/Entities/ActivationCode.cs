using VarinsEdu.Domain.Common;

namespace VarinsEdu.Domain.Entities;

public class ActivationCode : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid InstitutionId { get; set; }

    public Guid PersonId { get; set; }

    // Only the hash is stored, never the code itself.
    public string CodeHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public int FailedAttempts { get; set; }

    public Guid? ImportBatchId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
