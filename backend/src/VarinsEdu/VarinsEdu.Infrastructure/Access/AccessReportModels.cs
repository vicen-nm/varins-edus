namespace VarinsEdu.Infrastructure.Access;

public sealed record ReportTotals(int Total, int Qr, int Manual);

public sealed record ReportDayRow(DateOnly Date, string Point, string Service, int Total, int Qr, int Manual);

// Group is null for students without a group.
public sealed record ReportGroupRow(string? Group, int Total);

public sealed record ReportDetailRow(
    DateOnly Date,
    TimeOnly LocalTime,
    string IdentificationNumber,
    string FirstName,
    string LastName,
    string? Group,
    string Point,
    string Service,
    string Method);

public sealed record AccessReport(
    DateOnly From,
    DateOnly To,
    ReportTotals Totals,
    IReadOnlyList<ReportDayRow> ByDay,
    IReadOnlyList<ReportGroupRow> ByGroup,
    IReadOnlyList<ReportDetailRow> Details);

public sealed record ReportFile(byte[] Bytes, string FileName);
