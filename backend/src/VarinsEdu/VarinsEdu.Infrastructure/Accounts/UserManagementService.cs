using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Access;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;

namespace VarinsEdu.Infrastructure.Accounts;

// Accounts of an institution: staff creation, roles, activation and reset codes.
// Nobody ever chooses or learns another person's password: a one-time code is issued instead.
public class UserManagementService(
    AppDbContext db,
    ICurrentTenant tenant,
    ICurrentUser currentUser,
    IPasswordHasher passwordHasher,
    ResetCodeHasher codeHasher,
    TimeProvider time)
{
    // The tenant filter limits every query to the caller's institution (and hides platform users).
    // kind: "staff" (default), "student" or "all".
    public async Task<UserPage> ListAsync(string? kind, string? search, int skip, int take, CancellationToken ct = default)
    {
        Require();

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(skip, 0);
        var now = time.GetUtcNow();

        var query = db.Users.AsNoTracking().AsQueryable();

        query = kind switch
        {
            "student" => query.Where(u => u.PersonId != null),
            "all" => query,
            _ => query.Where(u => u.PersonId == null)
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Usernames are stored in lower case.
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(u => u.Username.Contains(term));
        }

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderBy(u => u.Username)
            .Skip(skip)
            .Take(take)
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.IsActive,
                IsStudent = u.PersonId != null,
                Roles = u.UserRoles.Select(ur => ur.Role.Key).OrderBy(k => k).ToList(),
                u.CreatedAt,
                u.LastLoginAt,
                u.LockedUntil
            })
            .ToListAsync(ct);

        var items = rows
            .Select(r => new UserListItem(
                r.Id,
                r.Username,
                r.IsActive,
                r.IsStudent,
                r.Roles,
                r.CreatedAt,
                r.LastLoginAt,
                r.LockedUntil is { } until && until > now))
            .ToList();

        return new UserPage(total, items);
    }

    public async Task<IReadOnlyList<RoleItem>> RolesAsync(CancellationToken ct = default)
    {
        Require();

        var keys = RoleKeys.StaffRoles;

        return await db.Roles
            .AsNoTracking()
            .Where(r => keys.Contains(r.Key))
            .OrderBy(r => r.Name)
            .Select(r => new RoleItem(r.Key, r.Name))
            .ToListAsync(ct);
    }

    public async Task<Outcome<IssuedResetCode>> CreateStaffAsync(CreateStaffInput input, CancellationToken ct = default)
    {
        var institutionId = Require();

        var name = StaffUsernames.Normalize(input.Username);

        if (!StaffUsernames.IsValid(name))
        {
            return Outcome<IssuedResetCode>.Fail("invalid_username");
        }

        var roleKey = input.RoleKey?.Trim();

        if (roleKey is null || !RoleKeys.StaffRoles.Contains(roleKey))
        {
            return Outcome<IssuedResetCode>.Fail("invalid_role");
        }

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Key == roleKey, ct);

        if (role is null)
        {
            return Outcome<IssuedResetCode>.Fail("invalid_role");
        }

        var slug = await db.Institutions.Select(i => i.Slug).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("The institution of the caller was not found.");

        var username = StaffUsernames.Build(slug, name);

        // Usernames are unique across the platform, so this check skips the tenant filter.
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Username == username, ct))
        {
            return Outcome<IssuedResetCode>.Fail("username_taken");
        }

        var now = time.GetUtcNow();

        var user = new User
        {
            Id = Guid.NewGuid(),
            InstitutionId = institutionId,
            Username = username,
            // Nobody knows this secret: the account can only be opened with the one-time code.
            PasswordHash = passwordHasher.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))),
            IsActive = true,
            CreatedAt = now
        };
        user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });

        var row = new PasswordResetCode { Id = Guid.NewGuid(), InstitutionId = institutionId, UserId = user.Id };
        var (code, expiresAt) = SetCode(row, now);

        db.Users.Add(user);
        db.PasswordResetCodes.Add(row);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two requests with the same name at once: the unique index let only one through.
            return Outcome<IssuedResetCode>.Fail("username_taken");
        }

        return Outcome<IssuedResetCode>.Ok(new IssuedResetCode(user.Id, username, code, expiresAt));
    }

    // Replaces the roles of a staff account.
    public async Task<Outcome<bool>> SetRolesAsync(Guid userId, IReadOnlyList<string>? roleKeys, CancellationToken ct = default)
    {
        Require();

        if (userId == currentUser.UserId)
        {
            return Outcome<bool>.Fail("cannot_modify_self");
        }

        var keys = roleKeys?.Select(k => k.Trim()).Distinct().ToList() ?? [];

        if (keys.Count == 0 || keys.Any(k => !RoleKeys.StaffRoles.Contains(k)))
        {
            return Outcome<bool>.Fail("invalid_role");
        }

        var user = await db.Users.Include(u => u.UserRoles).FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return Outcome<bool>.Fail("user_not_found");
        }

        if (user.PersonId is not null)
        {
            return Outcome<bool>.Fail("not_staff");
        }

        var roles = await db.Roles
            .Include(r => r.RolePermissions)
            .Where(r => keys.Contains(r.Key))
            .ToListAsync(ct);

        if (roles.Count != keys.Count)
        {
            return Outcome<bool>.Fail("invalid_role");
        }

        var keepsAdmin = roles.Any(r => r.RolePermissions.Any(rp => rp.PermissionKey == PermissionKeys.UsersManage));

        if (user.IsActive && !keepsAdmin && await IsAdminAsync(userId, ct) && !await HasOtherAdminAsync(userId, ct))
        {
            return Outcome<bool>.Fail("last_admin");
        }

        var wanted = roles.Select(r => r.Id).ToHashSet();

        foreach (var link in user.UserRoles.Where(ur => !wanted.Contains(ur.RoleId)).ToList())
        {
            db.UserRoles.Remove(link);
        }

        foreach (var roleId in wanted.Except(user.UserRoles.Select(ur => ur.RoleId)).ToList())
        {
            user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
        }

        await db.SaveChangesAsync(ct);

        return Outcome<bool>.Ok(true);
    }

    public async Task<Outcome<bool>> SetActiveAsync(Guid userId, bool active, CancellationToken ct = default)
    {
        Require();

        if (userId == currentUser.UserId)
        {
            return Outcome<bool>.Fail("cannot_modify_self");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return Outcome<bool>.Fail("user_not_found");
        }

        if (!active && user.IsActive && await IsAdminAsync(userId, ct) && !await HasOtherAdminAsync(userId, ct))
        {
            return Outcome<bool>.Fail("last_admin");
        }

        user.IsActive = active;
        await db.SaveChangesAsync(ct);

        return Outcome<bool>.Ok(true);
    }

    // Issues a new one-time code (new account, forgotten password) and invalidates the old one.
    public async Task<Outcome<IssuedResetCode>> IssueResetCodeAsync(Guid userId, CancellationToken ct = default)
    {
        var institutionId = Require();

        if (userId == currentUser.UserId)
        {
            return Outcome<IssuedResetCode>.Fail("cannot_modify_self");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return Outcome<IssuedResetCode>.Fail("user_not_found");
        }

        if (!user.IsActive)
        {
            return Outcome<IssuedResetCode>.Fail("user_inactive");
        }

        var row = await db.PasswordResetCodes.FirstOrDefaultAsync(c => c.UserId == userId, ct);

        if (row is null)
        {
            row = new PasswordResetCode { Id = Guid.NewGuid(), InstitutionId = institutionId, UserId = userId };
            db.PasswordResetCodes.Add(row);
        }

        var (code, expiresAt) = SetCode(row, time.GetUtcNow());

        await db.SaveChangesAsync(ct);

        return Outcome<IssuedResetCode>.Ok(new IssuedResetCode(user.Id, user.Username, code, expiresAt));
    }

    // One code per person: reusing the row replaces the old fingerprint.
    private (string Code, DateTimeOffset ExpiresAt) SetCode(PasswordResetCode row, DateTimeOffset now)
    {
        var code = ActivationCodeGenerator.Generate();

        row.CodeHash = codeHasher.Hash(code);
        row.ExpiresAt = now.Add(PasswordService.CodeLifetime);
        row.UsedAt = null;
        row.FailedAttempts = 0;
        row.CreatedAt = now;

        return (code, row.ExpiresAt);
    }

    private Task<bool> IsAdminAsync(Guid userId, CancellationToken ct) =>
        db.UserRoles.AnyAsync(
            ur => ur.UserId == userId
                  && ur.Role.RolePermissions.Any(rp => rp.PermissionKey == PermissionKeys.UsersManage),
            ct);

    // An institution must never be left without someone who can manage its accounts.
    private Task<bool> HasOtherAdminAsync(Guid excludedUserId, CancellationToken ct) =>
        db.Users.AnyAsync(
            u => u.Id != excludedUserId
                 && u.IsActive
                 && u.UserRoles.Any(ur => ur.Role.RolePermissions.Any(rp => rp.PermissionKey == PermissionKeys.UsersManage)),
            ct);

    private Guid Require() =>
        tenant.InstitutionId ?? throw new TenantViolationException("Only institution users can manage accounts.");
}
