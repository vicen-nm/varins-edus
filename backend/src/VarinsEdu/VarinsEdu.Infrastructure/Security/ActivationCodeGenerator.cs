using System.Security.Cryptography;

namespace VarinsEdu.Infrastructure.Security;

public static class ActivationCodeGenerator
{
    // 31 characters: no I, L, O, 0 or 1, which are easy to confuse when copied by hand.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int Length = 8;

    // A code like "K7QX-4MBD". 31 to the power of 8 is about 850 billion combinations.
    // The cryptographic generator is used on purpose: Random would be predictable.
    public static string Generate()
    {
        var chars = new char[Length + 1];
        var position = 0;

        for (var i = 0; i < Length; i++)
        {
            if (i == Length / 2)
            {
                chars[position++] = '-';
            }

            chars[position++] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }

    // What a person types may have lowercase, spaces or a missing hyphen.
    public static string Normalize(string? input) =>
        string.IsNullOrWhiteSpace(input)
            ? string.Empty
            : new string(input.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}
