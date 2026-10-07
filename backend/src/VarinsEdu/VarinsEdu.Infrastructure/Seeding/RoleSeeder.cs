using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Infrastructure.Seeding;

internal static class RoleSeeder
{
    // Creates the role if missing. Template permissions are applied when the role is created;
    // for existing roles they are only re-synced when alwaysSync is true.
    public static async Task EnsureRoleAsync(
        AppDbContext db,
        Guid? institutionId,
        string key,
        string name,
        IReadOnlyCollection<string> permissionKeys,
        bool alwaysSync,
        CancellationToken ct)
    {
        var role = await db.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.InstitutionId == institutionId && r.Key == key, ct);

        var isNew = role is null;

        if (role is null)
        {
            role = new Role
            {
                Id = Guid.NewGuid(),
                InstitutionId = institutionId,
                Key = key,
                Name = name,
                IsSystem = true
            };
            db.Roles.Add(role);
        }

        if (!isNew && !alwaysSync)
        {
            return;
        }

        var current = role.RolePermissions.Select(rp => rp.PermissionKey).ToHashSet();

        foreach (var permissionKey in permissionKeys.Where(k => !current.Contains(k)))
        {
            role.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionKey = permissionKey });
        }
    }
}
