namespace VarinsEdu.Infrastructure.Security;

public static class PasswordPolicy
{
    public const int MaxLength = 128;
    public const int StudentMinLength = 8;
    public const int StaffMinLength = 10;

    private static readonly string[] Common =
        ["12345678", "123456789", "1234567890", "password", "contrasena", "qwertyui", "11111111"];

    // "forbiddenParts" are things the password must not contain (the cédula, the username).
    public static bool IsWeak(string? password, int minLength, params string[] forbiddenParts)
    {
        if (password is null || password.Length < minLength || password.Length > MaxLength)
        {
            return true;
        }

        foreach (var part in forbiddenParts)
        {
            if (part.Length > 0 && password.Contains(part, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (Common.Contains(password.ToLowerInvariant()))
        {
            return true;
        }

        return password.Distinct().Count() == 1;
    }
}
