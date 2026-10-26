namespace VarinsEdu.Infrastructure.Students;

public sealed record StudentListItem(
    Guid PersonId,
    string IdentificationNumber,
    string FirstName,
    string LastName,
    Guid? GroupId,
    string? Group,
    bool HasAccount,
    string CodeStatus,
    DateTimeOffset? CodeExpiresAt);

public sealed record StudentPage(int Total, IReadOnlyList<StudentListItem> Items);

public sealed record GroupItem(Guid Id, string Name);

public enum CodeRegenerationError
{
    StudentNotFound,
    HasAccount
}

// The plain-text code appears only here, once.
public sealed record CodeRegenerationResult(CodeRegenerationError? Error, string? Code, DateTimeOffset? ExpiresAt);
