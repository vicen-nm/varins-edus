namespace VarinsEdu.Infrastructure.Security;

// The result of a successful login: who the user is and what they may do.
public sealed record AuthenticatedUser(
    Guid UserId,
    string Username,
    Guid? InstitutionId,
    IReadOnlyCollection<string> Permissions)
{
    // A user without an institution is a platform-level user.
    public bool IsPlatform => InstitutionId is null;
}
