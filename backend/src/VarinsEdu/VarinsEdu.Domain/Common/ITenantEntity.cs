namespace VarinsEdu.Domain.Common;

// Marks an entity that belongs to a single institution.
public interface ITenantEntity
{
    Guid InstitutionId { get; set; }
}
