namespace VarinsEdu.Domain.Common;

public static class IdentificationNumbers
{
    // Keeps only letters and digits, in uppercase: "1-1111-1111" becomes "111111111".
    // The same function must be used everywhere an identification number is read or compared.
    public static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}
