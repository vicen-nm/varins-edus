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

// Identifies a student from the QR of their card, and uses the token up: it works only once.
// Vehicles, exits and the gate will call this same service.
public class CardScanService(
    AppDbContext db,
    ICurrentTenant tenant,
    ICurrentUser currentUser,
    CardTokenService tokens,
    AuditSuppression auditSuppression,
    TimeProvider time)
{
    private const int MaxNonceLength = 64;

    // The tenant filter limits every query to the caller's institution.
    // Checks go from cheapest to most specific, and the token is only consumed at the very end:
    // a token refused for any other reason is not used up.
    public async Task<Outcome<ScannedStudent>> ScanAsync(string? token, CancellationToken ct = default)
    {
        var institutionId = tenant.InstitutionId
            ?? throw new TenantViolationException("Only institution users can scan cards.");

        var staffId = currentUser.UserId
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

        if (await UsedAsync(personId, nonce, ct))
        {
            return await ReusedAsync(institutionId, staffId, personId, ct);
        }

        var scan = new CardScan
        {
            Id = Guid.NewGuid(),
            InstitutionId = institutionId,
            PersonId = personId,
            Nonce = nonce,
            ScannedByUserId = staffId,
            ScannedAt = time.GetUtcNow()
        };

        db.CardScans.Add(scan);

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
            if (await UsedAsync(personId, nonce, ct))
            {
                return await ReusedAsync(institutionId, staffId, personId, ct);
            }

            throw;
        }

        return Outcome<ScannedStudent>.Ok(student);
    }

    private Task<bool> UsedAsync(Guid personId, string nonce, CancellationToken ct) =>
        db.CardScans.AnyAsync(c => c.PersonId == personId && c.Nonce == nonce, ct);

    // A code scanned twice is a clue: a screenshot, or a card shown to two people. No cédula is recorded.
    private async Task<Outcome<ScannedStudent>> ReusedAsync(
        Guid institutionId,
        Guid staffId,
        Guid personId,
        CancellationToken ct)
    {
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

        return Fail("card_already_used");
    }

    private static Outcome<ScannedStudent> Fail(string code) => Outcome<ScannedStudent>.Fail(code);
}
