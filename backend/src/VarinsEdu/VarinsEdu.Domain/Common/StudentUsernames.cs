namespace VarinsEdu.Domain.Common;

public static class StudentUsernames
{
    // "111111111@demo-a": unique across the platform and easy to explain to a student.
    public static string Build(string institutionSlug, string identificationNumber) =>
        $"{IdentificationNumbers.Normalize(identificationNumber).ToLowerInvariant()}@{institutionSlug.Trim().ToLowerInvariant()}";
}
