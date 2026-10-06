namespace VarinsEdu.Domain.Entities;

public class RolePermission
{
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;

    public string PermissionKey { get; set; } = string.Empty;
    public Permission Permission { get; set; } = null!;
}
