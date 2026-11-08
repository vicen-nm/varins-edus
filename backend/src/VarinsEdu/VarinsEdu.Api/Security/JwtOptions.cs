namespace VarinsEdu.Api.Security;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "VarinsEdu";
    public string Audience { get; set; } = "VarinsEdu";

    // Secret used to sign tokens. Must come from configuration, never from code.
    public string Key { get; set; } = string.Empty;

    // Short on purpose: the session is renewed with the refresh token, so a stolen access token dies quickly.
    public int ExpiresMinutes { get; set; } = 10;

    // Renewable sessions: days without use before a session ends, and the absolute limit since sign-in.
    public int RefreshIdleDays { get; set; } = 7;
    public int RefreshMaxDays { get; set; } = 30;
}
