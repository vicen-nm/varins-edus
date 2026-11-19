namespace VarinsEdu.Infrastructure.Visits;

// What the public form sends. Signature: "data:image/png;base64,...".
public sealed record VisitInput(
    string? FullName,
    string? IdentificationNumber,
    string? Phone,
    string? Host,
    string? Reason,
    string? Signature);

// Status: pending, inside, exited, rejected, expired or no_exit. No signature: it is asked for apart.
public sealed record VisitItem(
    Guid Id,
    string FullName,
    string IdentificationNumber,
    string? Phone,
    string Host,
    string Reason,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EnteredAt,
    string? HandledBy,
    DateTimeOffset? ExitedAt,
    string? ExitedBy);

// Inside and Pending count today's visits, whatever the filters.
public sealed record VisitPage(int Total, int Inside, int Pending, IReadOnlyList<VisitItem> Items);
