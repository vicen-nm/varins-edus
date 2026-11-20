using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Access;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Infrastructure.Preferences;

// How each person wants to see the screen. A person sets their own; staff with the accessibility permission
// may set a student's. There is no way to list the preferences of several people.
public class PreferenceService(AppDbContext db, ICurrentTenant tenant, ICurrentUser currentUser)
{
    // ---- Your own ----

    public Task<DisplayPreferences> GetMineAsync(CancellationToken ct = default) => ReadAsync(RequireUser(), ct);

    public Task<Outcome<DisplayPreferences>> SetMineAsync(DisplayPreferences input, CancellationToken ct = default) =>
        WriteAsync(RequireUser(), input, retry: true, ct);

    // ---- A student's, for the staff ----

    public async Task<Outcome<DisplayPreferences>> GetForStudentAsync(Guid personId, CancellationToken ct = default)
    {
        if (await StudentUserAsync(personId, ct) is not { } userId)
        {
            return Outcome<DisplayPreferences>.Fail("student_account_not_found");
        }

        return Outcome<DisplayPreferences>.Ok(await ReadAsync(userId, ct));
    }

    public async Task<Outcome<DisplayPreferences>> SetForStudentAsync(
        Guid personId,
        DisplayPreferences input,
        CancellationToken ct = default)
    {
        if (await StudentUserAsync(personId, ct) is not { } userId)
        {
            return Outcome<DisplayPreferences>.Fail("student_account_not_found");
        }

        return await WriteAsync(userId, input, retry: true, ct);
    }

    // ---- Shared ----

    // The tenant filter limits this to the caller's institution: a student of another school looks like no student.
    private async Task<Guid?> StudentUserAsync(Guid personId, CancellationToken ct)
    {
        Require();

        return await db.Users
            .Where(u => u.PersonId == personId && u.IsActive)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<DisplayPreferences> ReadAsync(Guid userId, CancellationToken ct)
    {
        var row = await db.UserPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);

        return row is null ? DisplayDefaults.Default : ToDto(row);
    }

    private async Task<Outcome<DisplayPreferences>> WriteAsync(
        Guid userId,
        DisplayPreferences input,
        bool retry,
        CancellationToken ct)
    {
        if (input.UiMode is not (DisplayDefaults.Standard or DisplayDefaults.Simple)
            || !double.IsFinite(input.FontScale)
            || input.FontScale < DisplayDefaults.MinFontScale
            || input.FontScale > DisplayDefaults.MaxFontScale)
        {
            return Outcome<DisplayPreferences>.Fail("invalid_preferences");
        }

        var row = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var isNew = row is null;

        if (row is null)
        {
            row = new UserPreference { UserId = userId };
            db.UserPreferences.Add(row);
        }

        row.UiMode = input.UiMode;
        row.FontScale = Math.Round(input.FontScale, 2);
        row.ReadAloud = input.ReadAloud;
        row.HighContrast = input.HighContrast;
        row.ReducedMotion = input.ReducedMotion;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (isNew && retry)
        {
            // Two first saves at once: the other one created the row. Start again, now updating it.
            db.ChangeTracker.Clear();

            return await WriteAsync(userId, input, retry: false, ct);
        }

        return Outcome<DisplayPreferences>.Ok(ToDto(row));
    }

    private static DisplayPreferences ToDto(UserPreference row) =>
        new(
            row.UiMode == DisplayDefaults.Simple ? DisplayDefaults.Simple : DisplayDefaults.Standard,
            row.FontScale,
            row.ReadAloud,
            row.HighContrast,
            row.ReducedMotion);

    private Guid Require() =>
        tenant.InstitutionId ?? throw new TenantViolationException("Only institution users can configure students.");

    private Guid RequireUser() =>
        currentUser.UserId ?? throw new InvalidOperationException("An authenticated user is required.");
}
