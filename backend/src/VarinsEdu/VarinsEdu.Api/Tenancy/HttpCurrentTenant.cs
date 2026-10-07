using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;

namespace VarinsEdu.Api.Tenancy;

// Reads the current institution from the authenticated user's claims.
public class HttpCurrentTenant(IHttpContextAccessor accessor) : ICurrentTenant
{
    public Guid? InstitutionId
    {
        get
        {
            var value = accessor.HttpContext?.User.FindFirst(AppClaims.InstitutionId)?.Value;
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public bool IsPlatformScope =>
        accessor.HttpContext?.User.HasClaim(AppClaims.Scope, AppClaims.PlatformScope) == true;
}
