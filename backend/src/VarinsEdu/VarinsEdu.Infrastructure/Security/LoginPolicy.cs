using VarinsEdu.Domain.Entities;

namespace VarinsEdu.Infrastructure.Security;

// Per-account protection against guessing passwords. The limit per IP is useless in a school,
// where everyone shares one address, so each account counts its own failures.
public static class LoginPolicy
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    public static bool IsLocked(User user, DateTimeOffset now) => user.LockedUntil > now;

    public static void RegisterFailure(User user, DateTimeOffset now)
    {
        user.FailedLoginAttempts++;

        if (user.FailedLoginAttempts >= MaxFailedAttempts)
        {
            user.LockedUntil = now + LockDuration;
            user.FailedLoginAttempts = 0;
        }
    }

    public static void Clear(User user)
    {
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
    }
}
