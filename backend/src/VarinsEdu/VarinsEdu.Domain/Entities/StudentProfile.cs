using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;

namespace VarinsEdu.Domain.Entities;

public class StudentProfile : ITenantEntity
{
    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;

    public Guid InstitutionId { get; set; }

    public Guid? GroupId { get; set; }
    public Group? Group { get; set; }

    public string Status { get; set; } = StudentStatuses.Active;
}
