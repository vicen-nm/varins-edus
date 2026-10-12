using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Infrastructure.Institutions;

public enum BrandingError
{
    None,
    NotAnInstitutionUser,
    InvalidDisplayName,
    InvalidColor
}

public sealed record UpdateBrandingCommand(string? DisplayName, string? PrimaryColor, string? AccentColor);

public sealed record UpdateBrandingResult(BrandingError Error)
{
    public bool Succeeded => Error == BrandingError.None;
}

// Lets an institution set its own display name and colors. Blank values go back to the platform default.
public class BrandingService(AppDbContext db, ICurrentTenant tenant)
{
    private static readonly Regex HexColor = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    public async Task<UpdateBrandingResult> UpdateAsync(UpdateBrandingCommand command, CancellationToken ct = default)
    {
        if (tenant.InstitutionId is not { } institutionId)
        {
            return new UpdateBrandingResult(BrandingError.NotAnInstitutionUser);
        }

        var displayName = string.IsNullOrWhiteSpace(command.DisplayName) ? null : command.DisplayName.Trim();

        if (displayName is { Length: < 3 or > 200 })
        {
            return new UpdateBrandingResult(BrandingError.InvalidDisplayName);
        }

        if (!TryNormalizeColor(command.PrimaryColor, out var primaryColor)
            || !TryNormalizeColor(command.AccentColor, out var accentColor))
        {
            return new UpdateBrandingResult(BrandingError.InvalidColor);
        }

        // The tenant filter leaves only this institution's settings visible.
        var settings = await db.InstitutionSettings.SingleOrDefaultAsync(ct);

        if (settings is null)
        {
            settings = new InstitutionSettings { InstitutionId = institutionId };
            db.InstitutionSettings.Add(settings);
        }

        settings.DisplayName = displayName;
        settings.PrimaryColor = primaryColor;
        settings.AccentColor = accentColor;

        await db.SaveChangesAsync(ct);

        return new UpdateBrandingResult(BrandingError.None);
    }

    // Blank means "use the default" (null). Otherwise it must be #RRGGBB, stored in lowercase.
    private static bool TryNormalizeColor(string? value, out string? normalized)
    {
        normalized = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var trimmed = value.Trim();

        if (!HexColor.IsMatch(trimmed))
        {
            return false;
        }

        normalized = trimmed.ToLowerInvariant();
        return true;
    }
}
