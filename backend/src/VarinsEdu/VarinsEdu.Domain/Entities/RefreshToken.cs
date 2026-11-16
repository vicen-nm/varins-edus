using VarinsEdu.Domain.Common;

namespace VarinsEdu.Domain.Entities;

// One link of a renewable session. Each renewal replaces the token with a new one of the same family.
public class RefreshToken : IOptionalTenantEntity
{
    public Guid Id { get; set; }

    // Null for platform users.
    public Guid? InstitutionId { get; set; }

    public Guid UserId { get; set; }

    // All the tokens of one session share the family.
    public Guid FamilyId { get; set; }

    // Only the fingerprint is stored, never the token itself.
    public string TokenHash { get; set; } = string.Empty;

    // "Keep me signed in": decides whether the browser cookie outlives the browser.
    public bool Persistent { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset FamilyStartedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
