using VarinsEdu.Domain.Common;

namespace VarinsEdu.Domain.Entities;

public class Group : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid InstitutionId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Level { get; set; }

    public ICollection<StudentProfile> Students { get; set; } = new List<StudentProfile>();
}
