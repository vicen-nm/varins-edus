using VarinsEdu.Domain.Common;

namespace VarinsEdu.Domain.Entities;

public class InstitutionModule : ITenantEntity
{
    public Guid InstitutionId { get; set; }
    public Institution Institution { get; set; } = null!;

    public string ModuleKey { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}
