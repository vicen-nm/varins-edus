using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Infrastructure.Seeding;

// Everything a new institution needs: empty settings, its modules (off) and its template roles.
// Safe to run more than once. It will also be used when a platform admin creates an institution.
public static class InstitutionDefaults
{
    public static async Task EnsureAsync(AppDbContext db, Guid institutionId, CancellationToken ct = default)
    {
        if (!await db.InstitutionSettings.AnyAsync(s => s.InstitutionId == institutionId, ct))
        {
            db.InstitutionSettings.Add(new InstitutionSettings { InstitutionId = institutionId });
        }

        var existingModules = await db.InstitutionModules
            .Where(m => m.InstitutionId == institutionId)
            .Select(m => m.ModuleKey)
            .ToListAsync(ct);

        foreach (var moduleKey in ModuleKeys.All.Except(existingModules))
        {
            db.InstitutionModules.Add(new InstitutionModule
            {
                InstitutionId = institutionId,
                ModuleKey = moduleKey,
                IsEnabled = false
            });
        }

        foreach (var template in RoleTemplates.ForInstitutions)
        {
            await RoleSeeder.EnsureRoleAsync(
                db, institutionId, template.Key, template.Name, template.Permissions, alwaysSync: false, ct);
        }

        await db.SaveChangesAsync(ct);
    }
}
