using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;

namespace VarinsEdu.Infrastructure.Access;

// Configuration of points and their services, plus the rotating QR of a point's screen.
public class AccessPointService(
    AppDbContext db,
    ICurrentTenant tenant,
    AccessPointTokenService tokens,
    TimeProvider time)
{
    // The tenant filter limits every query to the caller's institution.
    public async Task<IReadOnlyList<AccessPointDto>> ListAsync(CancellationToken ct = default)
    {
        Require();

        var points = await db.AccessPoints.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);

        return points.Select(ToDto).ToList();
    }

    public async Task<Outcome<AccessPointDto>> CreateAsync(AccessPointInput input, CancellationToken ct = default)
    {
        var institutionId = Require();

        if (ValidatePoint(input) is { } error)
        {
            return Outcome<AccessPointDto>.Fail(error);
        }

        var name = input.Name!.Trim();

        if (await db.AccessPoints.AnyAsync(p => p.Name == name, ct))
        {
            return Outcome<AccessPointDto>.Fail("access_point_name_taken");
        }

        var point = new AccessPoint { Id = Guid.NewGuid(), InstitutionId = institutionId, CreatedAt = time.GetUtcNow() };
        Apply(point, input, name);

        db.AccessPoints.Add(point);
        await db.SaveChangesAsync(ct);

        return Outcome<AccessPointDto>.Ok(ToDto(point));
    }

    public async Task<Outcome<AccessPointDto>> UpdateAsync(Guid id, AccessPointInput input, CancellationToken ct = default)
    {
        Require();

        var point = await db.AccessPoints.FirstOrDefaultAsync(p => p.Id == id, ct);

        if (point is null)
        {
            return Outcome<AccessPointDto>.Fail("access_point_not_found");
        }

        if (ValidatePoint(input) is { } error)
        {
            return Outcome<AccessPointDto>.Fail(error);
        }

        var name = input.Name!.Trim();

        if (await db.AccessPoints.AnyAsync(p => p.Name == name && p.Id != id, ct))
        {
            return Outcome<AccessPointDto>.Fail("access_point_name_taken");
        }

        Apply(point, input, name);
        await db.SaveChangesAsync(ct);

        return Outcome<AccessPointDto>.Ok(ToDto(point));
    }

    public async Task<Outcome<IReadOnlyList<AccessServiceDto>>> ListServicesAsync(Guid pointId, CancellationToken ct = default)
    {
        Require();

        if (!await db.AccessPoints.AnyAsync(p => p.Id == pointId, ct))
        {
            return Outcome<IReadOnlyList<AccessServiceDto>>.Fail("access_point_not_found");
        }

        var rows = await db.AccessServices
            .AsNoTracking()
            .Where(s => s.AccessPointId == pointId)
            .OrderBy(s => s.StartTime)
            .ToListAsync(ct);

        return Outcome<IReadOnlyList<AccessServiceDto>>.Ok(rows.Select(ToDto).ToList());
    }

    public async Task<Outcome<AccessServiceDto>> CreateServiceAsync(
        Guid pointId,
        AccessServiceInput input,
        CancellationToken ct = default)
    {
        var institutionId = Require();

        if (!await db.AccessPoints.AnyAsync(p => p.Id == pointId, ct))
        {
            return Outcome<AccessServiceDto>.Fail("access_point_not_found");
        }

        if (ValidateService(input) is { } error)
        {
            return Outcome<AccessServiceDto>.Fail(error);
        }

        var name = input.Name!.Trim();

        if (await db.AccessServices.AnyAsync(s => s.AccessPointId == pointId && s.Name == name, ct))
        {
            return Outcome<AccessServiceDto>.Fail("access_service_name_taken");
        }

        var service = new AccessService
        {
            Id = Guid.NewGuid(),
            InstitutionId = institutionId,
            AccessPointId = pointId,
            Name = name,
            StartTime = input.StartTime,
            EndTime = input.EndTime,
            IsActive = input.IsActive
        };

        db.AccessServices.Add(service);
        await db.SaveChangesAsync(ct);

        return Outcome<AccessServiceDto>.Ok(ToDto(service));
    }

    public async Task<Outcome<AccessServiceDto>> UpdateServiceAsync(
        Guid pointId,
        Guid serviceId,
        AccessServiceInput input,
        CancellationToken ct = default)
    {
        Require();

        var service = await db.AccessServices
            .FirstOrDefaultAsync(s => s.Id == serviceId && s.AccessPointId == pointId, ct);

        if (service is null)
        {
            return Outcome<AccessServiceDto>.Fail("access_service_not_found");
        }

        if (ValidateService(input) is { } error)
        {
            return Outcome<AccessServiceDto>.Fail(error);
        }

        var name = input.Name!.Trim();

        if (await db.AccessServices.AnyAsync(s => s.AccessPointId == pointId && s.Name == name && s.Id != serviceId, ct))
        {
            return Outcome<AccessServiceDto>.Fail("access_service_name_taken");
        }

        service.Name = name;
        service.StartTime = input.StartTime;
        service.EndTime = input.EndTime;
        service.IsActive = input.IsActive;

        await db.SaveChangesAsync(ct);

        return Outcome<AccessServiceDto>.Ok(ToDto(service));
    }

    // The point's screen asks for a fresh QR every few seconds.
    public async Task<Outcome<AccessToken>> IssueQrAsync(Guid pointId, CancellationToken ct = default)
    {
        var institutionId = Require();

        if (!await db.AccessPoints.AnyAsync(p => p.Id == pointId && p.IsActive, ct))
        {
            return Outcome<AccessToken>.Fail("access_point_not_found");
        }

        return Outcome<AccessToken>.Ok(tokens.Issue(pointId, institutionId));
    }

    private static string? ValidatePoint(AccessPointInput i)
    {
        var name = i.Name?.Trim();

        if (string.IsNullOrEmpty(name) || name.Length is < 2 or > 100)
        {
            return "invalid_access_name";
        }

        if (i.Kind is null || !AccessPointKinds.All.Contains(i.Kind))
        {
            return "invalid_access_kind";
        }

        if ((i.Latitude is null) != (i.Longitude is null))
        {
            return "invalid_location";
        }

        if (i.Latitude is { } lat && i.Longitude is { } lon && !GeoDistance.IsValid(lat, lon))
        {
            return "invalid_location";
        }

        if (i.RadiusMeters is < 10 or > 2000)
        {
            return "invalid_radius";
        }

        if (i.MaxAccuracyMeters is < 10 or > 1000)
        {
            return "invalid_accuracy";
        }

        return null;
    }

    private static string? ValidateService(AccessServiceInput i)
    {
        var name = i.Name?.Trim();

        if (string.IsNullOrEmpty(name) || name.Length > 50)
        {
            return "invalid_access_name";
        }

        return i.StartTime < i.EndTime ? null : "invalid_schedule";
    }

    private static void Apply(AccessPoint point, AccessPointInput input, string name)
    {
        point.Name = name;
        point.Kind = input.Kind!;
        point.Latitude = input.Latitude;
        point.Longitude = input.Longitude;
        point.RadiusMeters = input.RadiusMeters;
        point.MaxAccuracyMeters = input.MaxAccuracyMeters;
        point.IsActive = input.IsActive;
    }

    private static AccessPointDto ToDto(AccessPoint p) =>
        new(p.Id, p.Name, p.Kind, p.Latitude, p.Longitude, p.RadiusMeters, p.MaxAccuracyMeters, p.IsActive);

    private static AccessServiceDto ToDto(AccessService s) =>
        new(s.Id, s.AccessPointId, s.Name, s.StartTime, s.EndTime, s.IsActive);

    private Guid Require() =>
        tenant.InstitutionId ?? throw new TenantViolationException("Only institution users can manage access points.");
}
