namespace VarinsEdu.Domain.Common;

// Who is making the current request. Used by the audit log.
public interface ICurrentUser
{
    // Null when nobody is logged in (login attempts, background tasks).
    Guid? UserId { get; }

    string? IpAddress { get; }
}
