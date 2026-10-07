namespace VarinsEdu.Domain.Common;

// For entities that may belong to an institution or to the platform itself (InstitutionId = null).
public interface IOptionalTenantEntity
{
    Guid? InstitutionId { get; set; }
}
