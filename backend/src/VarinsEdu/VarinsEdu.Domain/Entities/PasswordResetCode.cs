using VarinsEdu.Domain.Common;

namespace VarinsEdu.Domain.Entities;

// A one-time code that lets a person choose a new password. One row per user: a new code replaces the old one.
public class PasswordResetCode : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid InstitutionId { get; set; }

    public Guid UserId { get; set; }

    // Only the hash is stored, never the code itself.
    public string CodeHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public int FailedAttempts { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
