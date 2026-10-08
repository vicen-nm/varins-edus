namespace VarinsEdu.Api.Security;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "VarinsEdu";
    public string Audience { get; set; } = "VarinsEdu";

    // Secret used to sign tokens. Must come from configuration, never from code.
    public string Key { get; set; } = string.Empty;

    public int ExpiresMinutes { get; set; } = 30;
}
