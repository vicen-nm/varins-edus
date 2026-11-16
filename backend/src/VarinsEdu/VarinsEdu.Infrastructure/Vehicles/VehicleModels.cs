namespace VarinsEdu.Infrastructure.Vehicles;

public sealed record VehicleInput(string? Plate, string? Make, string? Model, string? Color);

// The staff register a vehicle for a student by cédula. ApproveNow: the documents are in hand, so it is approved at once.
public sealed record StaffVehicleInput(
    string? IdentificationNumber,
    string? Plate,
    string? Make,
    string? Model,
    string? Color,
    bool ApproveNow = false);

public sealed record StatusInput(string? Status, string? Note);

public sealed record VehicleDto(
    Guid Id,
    string Plate,
    string Make,
    string Model,
    string Color,
    string Status,
    string? StatusNote,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReviewedAt);

// No cédula: the staff see the name and the group.
public sealed record VehicleListItem(
    Guid Id,
    string Plate,
    string Make,
    string Model,
    string Color,
    string Status,
    string? StatusNote,
    string FirstName,
    string LastName,
    string? Group,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReviewedAt,
    string? ReviewedBy);

public sealed record VehiclePage(int Total, IReadOnlyList<VehicleListItem> Items);
