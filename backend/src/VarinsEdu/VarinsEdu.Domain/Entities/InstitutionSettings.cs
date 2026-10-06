using VarinsEdu.Domain.Common;

namespace VarinsEdu.Domain.Entities;

public class InstitutionSettings : ITenantEntity
{
    public Guid InstitutionId { get; set; }
    public Institution Institution { get; set; } = null!;

    // All optional: null means "use the VarinsEdu default".
    public string? DisplayName { get; set; }
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? AccentColor { get; set; }
}
