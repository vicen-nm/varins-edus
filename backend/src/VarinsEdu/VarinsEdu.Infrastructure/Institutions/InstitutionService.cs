using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Seeding;

namespace VarinsEdu.Infrastructure.Institutions;

public enum InstitutionCreateError
{
    None,
    InvalidName,
    InvalidSlug,
    InvalidUsername,
    WeakPassword,
    SlugTaken,
    UsernameTaken
}

public sealed record CreateInstitutionCommand(string Name, string Slug, string AdminUsername, string AdminPassword);

public sealed record CreateInstitutionResult(
    InstitutionCreateError Error,
    Guid? InstitutionId = null,
    Guid? AdminUserId = null)
{
    public bool Succeeded => Error == InstitutionCreateError.None;
}

public class InstitutionService(
    AppDbContext db,
    IPasswordHasher passwordHasher,
    ICurrentTenant tenant,
    TimeProvider timeProvider)
{
    private const int MinPasswordLength = 12;

    private static readonly Regex SlugPattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

    // Creates an institution, its defaults and its first administrator, all or nothing.
    public async Task<CreateInstitutionResult> CreateAsync(
        CreateInstitutionCommand command,
        CancellationToken ct = default)
    {
        if (!tenant.IsPlatformScope)
        {
            throw new TenantViolationException("Only platform users can create institutions.");
        }

        var name = command.Name.Trim();
        var slug = command.Slug.Trim().ToLowerInvariant();
        var username = command.AdminUsername.Trim().ToLowerInvariant();

        if (name.Length is < 3 or > 200)
        {
            return Fail(InstitutionCreateError.InvalidName);
        }

        if (slug.Length is < 3 or > 50 || !SlugPattern.IsMatch(slug))
        {
            return Fail(InstitutionCreateError.InvalidSlug);
        }

        if (username.Length is < 3 or > 100)
        {
            return Fail(InstitutionCreateError.InvalidUsername);
        }

        if (command.AdminPassword.Length < MinPasswordLength)
        {
            return Fail(InstitutionCreateError.WeakPassword);
        }

        if (await db.Institutions.AnyAsync(i => i.Slug == slug, ct))
        {
            return Fail(InstitutionCreateError.SlugTaken);
        }

        if (await db.Users.AnyAsync(u => u.Username == username, ct))
        {
            return Fail(InstitutionCreateError.UsernameTaken);
        }

        var institution = new Institution
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = slug,
            CreatedAt = timeProvider.GetUtcNow()
        };

        // The in-memory test database does not support transactions, so only relational providers get one.
        IDbContextTransaction? transaction = null;
        if (db.Database.IsRelational())
        {
            transaction = await db.Database.BeginTransactionAsync(ct);
        }

        try
        {
            db.Institutions.Add(institution);
            await db.SaveChangesAsync(ct);

            await InstitutionDefaults.EnsureAsync(db, institution.Id, ct);

            var adminRole = await db.Roles.SingleAsync(
                r => r.InstitutionId == institution.Id && r.Key == RoleKeys.InstitutionAdmin, ct);

            var admin = new User
            {
                Id = Guid.NewGuid(),
                InstitutionId = institution.Id,
                Username = username,
                PasswordHash = passwordHasher.Hash(command.AdminPassword),
                IsActive = true,
                CreatedAt = timeProvider.GetUtcNow()
            };
            admin.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = adminRole.Id });

            db.Users.Add(admin);
            await db.SaveChangesAsync(ct);

            if (transaction is not null)
            {
                await transaction.CommitAsync(ct);
            }

            return new CreateInstitutionResult(InstitutionCreateError.None, institution.Id, admin.Id);
        }
        finally
        {
            if (transaction is not null)
            {
                // Without a commit, disposing the transaction rolls everything back.
                await transaction.DisposeAsync();
            }
        }
    }

    private static CreateInstitutionResult Fail(InstitutionCreateError error) => new(error);
}
