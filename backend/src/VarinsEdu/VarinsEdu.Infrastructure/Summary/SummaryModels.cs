namespace VarinsEdu.Infrastructure.Summary;

// Which sections the caller may see. A section without permission is not even computed.
public sealed record SummaryScope(
    bool Access,
    bool Parking,
    bool Visits,
    bool Exits,
    bool Vehicles,
    bool Students,
    bool Security);

public sealed record ServiceCount(string Service, int Total);

public sealed record AccessSummary(int Total, int Qr, int Manual, IReadOnlyList<ServiceCount> ByService);

// WithoutExit: visits of the last 30 days that were never closed.
public sealed record ParkingSummary(int Inside, int EntriesToday, int ExitsToday, int WithoutExit);

public sealed record VisitsSummary(int Total, int Pending, int Inside);

// Valid: permits of today that have not ended yet.
public sealed record ExitsSummary(int Total, int Valid, int Used);

public sealed record VehiclesSummary(int Pending, int Approved, int Suspended);

public sealed record StudentsSummary(int Active, int WithAccount, int WithoutAccount);

// Only accesses against accounts of this institution: attempts against unknown users cannot be attributed.
public sealed record SecuritySummary(int FailedLogins, int LockedAccounts, int CardReuse, int SessionReuse);

public sealed record DailySummary(
    DateOnly Date,
    AccessSummary? Access,
    ParkingSummary? Parking,
    VisitsSummary? Visits,
    ExitsSummary? Exits,
    VehiclesSummary? Vehicles,
    StudentsSummary? Students,
    SecuritySummary? Security);
