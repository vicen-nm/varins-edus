using VarinsEdu.Infrastructure.Students;

namespace VarinsEdu.Infrastructure.Parking;

public sealed record ParkingVehicle(
    Guid Id,
    string Plate,
    string Make,
    string Model,
    string Color,
    bool Inside,
    DateTimeOffset? InsideSince);

public sealed record ParkingPreview(ScannedStudent Student, IReadOnlyList<ParkingVehicle> Vehicles);

// Action: "entry" or "exit".
public sealed record ParkingResult(string Action, ScannedStudent Student, string Plate, DateTimeOffset At);

// Status: "inside", "exited" or "no_exit" (a visit left open on a previous day).
public sealed record ParkingEventItem(
    Guid Id,
    string Plate,
    string FirstName,
    string LastName,
    string? Group,
    DateTimeOffset EnteredAt,
    DateTimeOffset? ExitedAt,
    string Status);

public sealed record ParkingPage(int Total, int Inside, IReadOnlyList<ParkingEventItem> Items);
