using VarinsEdu.Infrastructure.Security;

namespace VarinsEdu.Api.Security;

// The renewal token lives in a cookie that JavaScript cannot read. It is only sent to /api/auth,
// only over HTTPS, and never from another site.
public static class SessionCookies
{
    public const string Name = "varinsedu_rt";

    private static CookieOptions Options(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        IsEssential = true,
        Expires = expires
    };

    // Without "keep me signed in" there is no expiry: the browser drops the cookie when it closes.
    public static void Set(HttpResponse response, IssuedSession session) =>
        response.Cookies.Append(Name, session.Token, Options(session.Persistent ? session.ExpiresAt : null));

    public static void Clear(HttpResponse response) => response.Cookies.Delete(Name, Options(null));

    public static string? Read(HttpRequest request) => request.Cookies[Name];
}
