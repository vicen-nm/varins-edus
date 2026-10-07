using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Infrastructure.Seeding;

public class DatabaseSeeder(
    DbContextOptions<AppDbContext> dbOptions,
    IPasswordHasher passwordHasher,
    SeedOptions options,
    ILogger<DatabaseSeeder> logger)
{
    private const int MinPasswordLength = 12;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        // The seeder is a system task, so it works with platform-wide scope.
        await using var db = new AppDbContext(dbOptions, new PlatformTenant());

        await SyncPermissionsAsync(db, ct);
        await EnsurePlatformAdminAsync(db, ct);

        var institutionIds = await db.Institutions.Select(i => i.Id).ToListAsync(ct);
        foreach (var institutionId in institutionIds)
        {
            await InstitutionDefaults.EnsureAsync(db, institutionId, ct);
        }

        logger.LogInformation("Seed finished.");
    }

    private static async Task SyncPermissionsAsync(AppDbContext db, CancellationToken ct)
    {
        var existing = await db.Permissions.ToDictionaryAsync(p => p.Key, ct);

        foreach (var key in PermissionKeys.All)
        {
            var description = PermissionKeys.Descriptions[key];

            if (existing.TryGetValue(key, out var permission))
            {
                permission.Description = description;
            }
            else
            {
                db.Permissions.Add(new Permission { Key = key, Description = description });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task EnsurePlatformAdminAsync(AppDbContext db, CancellationToken ct)
    {
        // The platform role always has every permission.
        await RoleSeeder.EnsureRoleAsync(
            db, null, RoleKeys.PlatformAdmin, "Platform administrator", PermissionKeys.All, alwaysSync: true, ct);
        await db.SaveChangesAsync(ct);

        if (string.IsNullOrWhiteSpace(options.PlatformAdminUsername) ||
            string.IsNullOrWhiteSpace(options.PlatformAdminPassword))
        {
            logger.LogWarning(
                "Seed:PlatformAdminUsername / Seed:PlatformAdminPassword are not configured. No platform admin was created.");
            return;
        }

        if (options.PlatformAdminPassword.Length < MinPasswordLength)
        {
            throw new InvalidOperationException(
                $"The platform admin password must have at least {MinPasswordLength} characters.");
        }

        var username = options.PlatformAdminUsername.Trim().ToLowerInvariant();

        if (await db.Users.AnyAsync(u => u.Username == username, ct))
        {
            return;
        }

        var role = await db.Roles.SingleAsync(r => r.InstitutionId == null && r.Key == RoleKeys.PlatformAdmin, ct);

        var user = new User
        {
            Id = Guid.NewGuid(),
            InstitutionId = null,
            Username = username,
            PasswordHash = passwordHasher.Hash(options.PlatformAdminPassword),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Platform admin '{Username}' created.", username);
    }
}
