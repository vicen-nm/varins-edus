using VarinsEdu.Domain.Constants;

namespace VarinsEdu.Infrastructure.Access;

// Either a value or a stable error code, which the frontend translates.
public sealed record Outcome<T>(string? Error, T? Value)
{
    public static Outcome<T> Ok(T value) => new(null, value);

    public static Outcome<T> Fail(string error) => new(error, default);
}

public sealed record AccessPointInput(
    string? Name,
    string? Kind = AccessPointKinds.Cafeteria,
    double? Latitude = null,
    double? Longitude = null,
    int RadiusMeters = 150,
    int MaxAccuracyMeters = 100,
    bool IsActive = true);

public sealed record AccessPointDto(
    Guid Id,
    string Name,
    string Kind,
    double? Latitude,
    double? Longitude,
    int RadiusMeters,
    int MaxAccuracyMeters,
    bool IsActive);

public sealed record AccessServiceInput(string? Name, TimeOnly StartTime, TimeOnly EndTime, bool IsActive = true);

public sealed record AccessServiceDto(
    Guid Id,
    Guid AccessPointId,
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    bool IsActive);

public sealed record CheckInCommand(string? Token, double Latitude, double Longitude, double AccuracyMeters);

public sealed record RegistrationResult(
    string FirstName,
    string LastName,
    string PointName,
    string ServiceName,
    DateTimeOffset OccurredAt);

public sealed record AccessEventItem(
    Guid Id,
    string PointName,
    string ServiceName,
    string FirstName,
    string LastName,
    string Method,
    DateTimeOffset OccurredAt,
    DateOnly ServiceDate);

public sealed record AccessEventPage(int Total, IReadOnlyList<AccessEventItem> Items);
