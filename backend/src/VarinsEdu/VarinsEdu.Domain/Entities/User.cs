using VarinsEdu.Domain.Common;

namespace VarinsEdu.Domain.Entities;

public class User : IOptionalTenantEntity
{
    public Guid Id { get; set; }

    // Null = platform-level user (superadmin), not tied to any institution.
    public Guid? InstitutionId { get; set; }

    // Null for platform users, who have no Person record.
    public Guid? PersonId { get; set; }
    public Person? Person { get; set; }

    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
