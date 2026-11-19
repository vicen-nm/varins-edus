using VarinsEdu.Infrastructure.Students;

namespace VarinsEdu.Infrastructure.Exits;

public sealed record ExitPermitInput(
    string? IdentificationNumber,
    DateOnly? Date,
    TimeOnly? FromTime,
    TimeOnly? ToTime,
    string? Reason);

public sealed record CreatedExitPermit(Guid Id);

// No cédula: the staff see the name and the group. Status: active, used, cancelled or expired.
public sealed record ExitPermitItem(
    Guid Id,
    string FirstName,
    string LastName,
    string? Group,
    DateOnly Date,
    TimeOnly FromTime,
    TimeOnly ToTime,
    string Reason,
    string Status,
    string? CreatedBy,
    DateTimeOffset? UsedAt,
    string? UsedBy);

public sealed record ExitPermitPage(int Total, IReadOnlyList<ExitPermitItem> Items);

// What the guard sees: that there is a valid permit and its window. Never the reason.
public sealed record ExitOption(Guid Id, TimeOnly FromTime, TimeOnly ToTime);

public sealed record ExitPreview(ScannedStudent Student, IReadOnlyList<ExitOption> Permits);

public sealed record ExitResult(ScannedStudent Student, DateTimeOffset At);
