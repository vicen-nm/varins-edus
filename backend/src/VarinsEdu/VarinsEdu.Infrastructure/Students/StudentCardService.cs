using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;

namespace VarinsEdu.Infrastructure.Students;

public enum CardError
{
    Unavailable
}

public sealed record StudentCard(string FirstName, string LastName, string? Group, CardToken Token);

public sealed record StudentCardResult(CardError? Error, StudentCard? Card);

public class StudentCardService(
    AppDbContext db,
    ICurrentTenant tenant,
    ICurrentUser currentUser,
    CardTokenService tokens)
{
    // Only the caller's own card. The tenant filter also hides users of other institutions.
    public async Task<StudentCardResult> GetAsync(CancellationToken ct = default)
    {
        var institutionId = tenant.InstitutionId
            ?? throw new TenantViolationException("Only institution users have a card.");

        var userId = currentUser.UserId;

        if (userId is null)
        {
            return new StudentCardResult(CardError.Unavailable, null);
        }

        var personId = await db.Users
            .Where(u => u.Id == userId && u.IsActive)
            .Select(u => u.PersonId)
            .FirstOrDefaultAsync(ct);

        if (personId is null)
        {
            return new StudentCardResult(CardError.Unavailable, null);
        }

        var student = await db.StudentProfiles
            .AsNoTracking()
            .Where(s => s.PersonId == personId)
            .Select(s => new
            {
                s.Status,
                s.Person.IsActive,
                s.Person.FirstName,
                s.Person.LastName,
                Group = s.Group != null ? s.Group.Name : null
            })
            .FirstOrDefaultAsync(ct);

        if (student is null || !student.IsActive || student.Status != StudentStatuses.Active)
        {
            return new StudentCardResult(CardError.Unavailable, null);
        }

        var token = tokens.Issue(personId.Value, institutionId);

        return new StudentCardResult(null, new StudentCard(student.FirstName, student.LastName, student.Group, token));
    }
}
