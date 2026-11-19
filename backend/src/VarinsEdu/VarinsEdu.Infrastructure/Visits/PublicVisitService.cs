using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Infrastructure.Access;
using VarinsEdu.Infrastructure.Parking;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Infrastructure.Visits;

// The public form. Nobody is signed in: the institution comes from the link, and nothing is trusted.
// The visit waits for the guard, so a flood of fake requests never counts as visitors.
public class PublicVisitService(DbContextOptions<AppDbContext> options, TimeProvider time)
{
    // The most requests one institution takes in a day: it only stops a flood.
    public const int MaxPerDay = 300;

    private const int MaxSignatureBytes = 60 * 1024;
    private const int MinSignatureBytes = 100;
    private const string PngPrefix = "data:image/png;base64,";

    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // Cheapest checks first. The answer for a repeated open visit is the same as for a new one:
    // nothing is revealed and no duplicate is created.
    public async Task<Outcome<bool>> SubmitAsync(string? slug, VisitInput input, CancellationToken ct = default)
    {
        var name = input.FullName?.Trim() ?? string.Empty;
        var host = input.Host?.Trim() ?? string.Empty;
        var reason = input.Reason?.Trim() ?? string.Empty;
        var phone = input.Phone?.Trim();
        var number = IdentificationNumbers.Normalize(input.IdentificationNumber);

        if (name.Length is < 3 or > 120
            || host.Length is < 2 or > 100
            || reason.Length is < 2 or > 100
            || phone is { Length: > 30 })
        {
            return Outcome<bool>.Fail("invalid_visit_data");
        }

        if (number.Length is < 4 or > 20)
        {
            return Outcome<bool>.Fail("invalid_visitor_id");
        }

        if (DecodeSignature(input.Signature) is not { } signature)
        {
            return Outcome<bool>.Fail("invalid_signature");
        }

        var normalizedSlug = (slug ?? string.Empty).Trim().ToLowerInvariant();
        Guid? institutionId;

        // Step 1: which institution? Nothing is written with this context.
        await using (var lookup = new AppDbContext(options, new NoTenant()))
        {
            institutionId = await lookup.Institutions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(i => i.Slug == normalizedSlug && i.IsActive)
                .Select(i => (Guid?)i.Id)
                .SingleOrDefaultAsync(ct);
        }

        if (institutionId is not { } id)
        {
            return Outcome<bool>.Fail("institution_not_found");
        }

        // From here on, everything runs inside that institution: filters and write rules apply.
        await using var db = new AppDbContext(options, new InstitutionTenant(id));

        var today = DateOnly.FromDateTime(await LocalTime.NowAsync(db, time, ct));
        var open = VisitStatuses.Open;

        if (await db.Visits.AnyAsync(v => v.ServiceDate == today && v.IdentificationNumber == number && open.Contains(v.Status), ct))
        {
            return Outcome<bool>.Ok(true);
        }

        if (await db.Visits.CountAsync(v => v.ServiceDate == today, ct) >= MaxPerDay)
        {
            return Outcome<bool>.Fail("visit_limit_reached");
        }

        db.Visits.Add(new Visit
        {
            Id = Guid.NewGuid(),
            InstitutionId = id,
            ServiceDate = today,
            FullName = name,
            IdentificationNumber = number,
            Phone = string.IsNullOrEmpty(phone) ? null : phone,
            Host = host,
            Reason = reason,
            SignaturePng = signature,
            Status = VisitStatuses.Pending,
            CreatedAt = time.GetUtcNow()
        });

        await db.SaveChangesAsync(ct);

        return Outcome<bool>.Ok(true);
    }

    // The image is never opened: only its prefix, its size and the first bytes of a PNG are checked.
    private static byte[]? DecodeSignature(string? value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith(PngPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var base64 = value[PngPrefix.Length..];

        // Refuses a huge text before decoding it.
        if (base64.Length > MaxSignatureBytes * 4 / 3 + 8)
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(base64);

            if (bytes.Length is < MinSignatureBytes or > MaxSignatureBytes)
            {
                return null;
            }

            return bytes.AsSpan(0, PngMagic.Length).SequenceEqual(PngMagic) ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
