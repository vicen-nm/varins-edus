using System.Text.RegularExpressions;

namespace VarinsEdu.Domain.Common;

public static class StaffUsernames
{
    private static readonly Regex Pattern = new("^[a-z0-9]+([._-][a-z0-9]+)*$", RegexOptions.Compiled);

    public static string Normalize(string? name) => name?.Trim().ToLowerInvariant() ?? string.Empty;

    // Without "@": the institution is added by Build, so nobody can write another school's name.
    public static bool IsValid(string normalizedName) =>
        normalizedName.Length is >= 3 and <= 40 && Pattern.IsMatch(normalizedName);

    // "maria.perez@demo-a": unique across the platform, like the students' usernames.
    public static string Build(string institutionSlug, string normalizedName) =>
        $"{normalizedName}@{institutionSlug.Trim().ToLowerInvariant()}";
}
