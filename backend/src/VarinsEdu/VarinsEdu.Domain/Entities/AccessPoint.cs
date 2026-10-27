using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;

namespace VarinsEdu.Domain.Entities;

// A place where people are registered: the cafeteria, the gate...
public class AccessPoint : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid InstitutionId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = AccessPointKinds.Cafeteria;

    // Where the point is. Null until configured: check-ins are refused without it.
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public int RadiusMeters { get; set; } = 150;
    public int MaxAccuracyMeters { get; set; } = 100;

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
