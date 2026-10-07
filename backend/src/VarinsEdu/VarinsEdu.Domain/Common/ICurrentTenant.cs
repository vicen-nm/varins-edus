namespace VarinsEdu.Domain.Common;

// Tells the data layer which institution is making the current request.
public interface ICurrentTenant
{
    // The caller's institution. Null when unknown (and then nothing is visible).
    Guid? InstitutionId { get; }

    // True only for platform-level users who may see every institution.
    bool IsPlatformScope { get; }
}
