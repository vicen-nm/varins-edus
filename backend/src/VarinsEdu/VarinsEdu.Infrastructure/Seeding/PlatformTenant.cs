using VarinsEdu.Domain.Common;

namespace VarinsEdu.Infrastructure.Seeding;

// Platform-wide scope for system tasks such as seeding.
// Never use this from an HTTP request.
public sealed class PlatformTenant : ICurrentTenant
{
    public Guid? InstitutionId => null;

    public bool IsPlatformScope => true;
}
