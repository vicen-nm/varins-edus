using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Access;
using VarinsEdu.Infrastructure.Auditing;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;

namespace VarinsEdu.Infrastructure.Students;

// Only what staff need to recognise the student: no cédula, no photo.
public sealed record ScannedStudent(string FirstName, string LastName, string? Group);

// A card that passed every check but is not used up yet.
public sealed record ResolvedCard(Guid PersonId, string Nonce, ScannedStudent Student);

// Identifies a student from the QR of their card, and uses the token up: it works only once.
// Parking, exits and the gate use the same service: they verify first and use the card up when they record
// what the scan authorizes.
public class CardScanService(
    AppDbContext db,
    ICurrentTenant tenant,
    ICurrentUser currentUser,
    CardTokenService tokens,
    AuditSuppression auditSuppression,
    TimeProvider time)
{
    private const int MaxNonceLength = 64;

    // Verifies and uses the card up in one step.
    public async Task<Outcome<ScannedStudent>> ScanAsync(string? token, CancellationToken ct = default)
    {
        var verified = await VerifyAsync(token, ct);

        if (verified.Error is { } error)
        {
            return Outcome<ScannedStudent>.Fail(error);
        }

        var card = verified.Value!;
        var scan = Stage(card);

        try
        {
            // The table is already the history of these scans: they are not audited one by one.
            using (auditSuppression.Begin())
            {
                await db.SaveChangesAsync(ct);
            }
        }
        catch (DbUpdateException)
        {
            db.Entry(scan).State = EntityState.Detached;

            // Two scans at once: the unique index let only one through.
            if (await IsUsedAsync(card.PersonId, card.Nonce, ct))
            {
                return await ReusedAsync<ScannedStudent>(card.PersonId, ct);
            }

            throw;
        }

        return Outcome<ScannedStudent>.Ok(card.Student);
    }

    // Checks go from cheapest to most specific. Nothing is written here: the card is not used up.
    // The tenant filter limits every query to the caller's institution.
    public async Task<Outcome<ResolvedCard>> VerifyAsync(string? token, CancellationToken ct = default)
    {
        var institutionId = tenant.InstitutionId
            ?? throw new TenantViolationException("Only institution users can scan cards.");

        _ = currentUser.UserId
            ?? throw new InvalidOperationException("An authenticated user is required to scan cards.");

        var check = tokens.Verify(token);

        if (check.Status == CardTokenStatus.Expired)
        {
            return Fail("expired_card");
        }

        // A card of another institution looks just like a forged one.
        if (check.Status != CardTokenStatus.Valid || check.InstitutionId != institutionId)
        {
            return Fail("invalid_card");
        }

        if (check.PersonId is not { } personId)
        {
            return Fail("invalid_card");
        }

        if (string.IsNullOrEmpty(check.Nonce) || check.Nonce.Length > MaxNonceLength)
        {
            return Fail("invalid_card");
        }

        var nonce = check.Nonce;

        var student = await db.StudentProfiles
            .AsNoTracking()
            .Where(s => s.PersonId == personId && s.Status == StudentStatuses.Active && s.Person.IsActive)
            .Select(s => new ScannedStudent(
                s.Person.FirstName,
                s.Person.LastName,
                s.Group != null ? s.Group.Name : null))
            .FirstOrDefaultAsync(ct);

        if (student is null)
        {
            return Fail("card_student_unavailable");
        }

        if (await IsUsedAsync(personId, nonce, ct))
        {
            return await ReusedAsync<ResolvedCard>(personId, ct);
        }

        return Outcome<ResolvedCard>.Ok(new ResolvedCard(personId, nonce, student));
    }

    // Adds the scan to the context WITHOUT saving: the caller saves it together with what the scan authorizes.
    public CardScan Stage(ResolvedCard card)
    {
        var institutionId = tenant.InstitutionId
            ?? throw new TenantViolationException("Only institution users can scan cards.");

        var staffId = currentUser.UserId
            ?? throw new InvalidOperationException("An authenticated user is required to scan cards.");

        var scan = new CardScan
        {
            Id = Guid.NewGuid(),
            InstitutionId = institutionId,
            PersonId = card.PersonId,
            Nonce = card.Nonce,
            ScannedByUserId = staffId,
            ScannedAt = time.GetUtcNow()
        };

        db.CardScans.Add(scan);

        return scan;
    }

    public Task<bool> IsUsedAsync(Guid personId, string nonce, CancellationToken ct = default) =>
        db.CardScans.AnyAsync(c => c.PersonId == personId && c.Nonce == nonce, ct);

    // A code scanned twice is a clue: a screenshot, or a card shown to two people. No cédula is recorded.
    public async Task<Outcome<T>> ReusedAsync<T>(Guid personId, CancellationToken ct = default)
    {
        var institutionId = tenant.InstitutionId
            ?? throw new TenantViolationException("Only institution users can scan cards.");

        var staffId = currentUser.UserId
            ?? throw new InvalidOperationException("An authenticated user is required to scan cards.");

        db.AuditLogs.Add(new AuditLog
        {
            InstitutionId = institutionId,
            UserId = staffId,
            Action = "card.scan.reused",
            EntityType = "Person",
            EntityId = personId.ToString(),
            OccurredAt = time.GetUtcNow(),
            IpAddress = currentUser.IpAddress
        });

        await db.SaveChangesAsync(ct);

        return Outcome<T>.Fail("card_already_used");
    }

    private static Outcome<ResolvedCard> Fail(string code) => Outcome<ResolvedCard>.Fail(code);
}
