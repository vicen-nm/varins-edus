namespace VarinsEdu.Infrastructure.Imports;

// The plain-text code appears here and nowhere else: it is shown once and never stored.
public sealed record IssuedCode(
    string IdentificationNumber,
    string FirstName,
    string LastName,
    string? Group,
    string Code);

public sealed record ImportResult(
    Guid BatchId,
    int Created,
    int AlreadyExisted,
    int ErrorRows,
    IReadOnlyList<RowError> Errors,
    IReadOnlyList<IssuedCode> Codes);
