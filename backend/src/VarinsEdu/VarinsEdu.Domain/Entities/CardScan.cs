using VarinsEdu.Domain.Common;

namespace VarinsEdu.Domain.Entities;

// One successful scan of a student's card. It is also the proof that the card token was used:
// a token can only be scanned once.
public class CardScan : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid InstitutionId { get; set; }

    public Guid PersonId { get; set; }

    // The random part of the card token.
    public string Nonce { get; set; } = string.Empty;

    // The staff member who scanned.
    public Guid ScannedByUserId { get; set; }

    public DateTimeOffset ScannedAt { get; set; }
}
