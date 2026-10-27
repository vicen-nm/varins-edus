using VarinsEdu.Domain.Common;

namespace VarinsEdu.Domain.Entities;

// A meal or period of a point (breakfast, lunch) with its own local-time window.
public class AccessService : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid InstitutionId { get; set; }

    public Guid AccessPointId { get; set; }

    public string Name { get; set; } = string.Empty;

    // Local time of the institution. The window is [StartTime, EndTime).
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public bool IsActive { get; set; } = true;
}
