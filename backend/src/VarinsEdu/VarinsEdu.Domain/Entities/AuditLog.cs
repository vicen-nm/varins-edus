namespace VarinsEdu.Domain.Entities;

// Append-only: rows are never updated or deleted.
// No navigation properties or foreign keys on purpose, so history never depends on other tables.
public class AuditLog
{
    public long Id { get; set; }

    public Guid? InstitutionId { get; set; }
    public Guid? UserId { get; set; }

    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
    public string? IpAddress { get; set; }

    // JSON text with details (for example before/after values).
    public string? Metadata { get; set; }
}
