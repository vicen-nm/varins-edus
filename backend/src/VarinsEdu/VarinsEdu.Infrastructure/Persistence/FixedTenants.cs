using VarinsEdu.Domain.Common;

namespace VarinsEdu.Infrastructure.Persistence;

// A tenant fixed to one institution. Used when code must work "inside" an institution
// without a logged-in user, after the institution has been identified safely.
public sealed class InstitutionTenant(Guid institutionId) : ICurrentTenant
{
    public Guid? InstitutionId => institutionId;

    public bool IsPlatformScope => false;
}

// Nobody is identified yet: nothing tenant-owned is visible or writable.
public sealed class NoTenant : ICurrentTenant
{
    public Guid? InstitutionId => null;

    public bool IsPlatformScope => false;
}
