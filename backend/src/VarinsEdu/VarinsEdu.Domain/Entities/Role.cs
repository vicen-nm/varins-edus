using VarinsEdu.Domain.Common;

namespace VarinsEdu.Domain.Entities;

public class Role : IOptionalTenantEntity
{
    public Guid Id { get; set; }

    // Null = platform-level role.
    public Guid? InstitutionId { get; set; }

    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    // System roles come from templates and cannot be deleted.
    public bool IsSystem { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
