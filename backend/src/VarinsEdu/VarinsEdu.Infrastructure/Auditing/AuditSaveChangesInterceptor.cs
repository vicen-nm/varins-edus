using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Entities;

namespace VarinsEdu.Infrastructure.Auditing;

// Records every create, update and delete in the audit log, inside the same save as the change itself.
public class AuditSaveChangesInterceptor(
    ICurrentTenant tenant,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : SaveChangesInterceptor
{
    private const string Redacted = "[redacted]";

    // The audit log records THAT these properties changed, never their values.
    private static readonly HashSet<string> RedactedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        // Secrets
        "PasswordHash", "CodeHash", "TokenHash", "QrTokenHash",

        // Personal data
        "IdentificationNumber", "FirstName", "LastName", "Phone", "Email"
    };

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        AddAuditRows(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AddAuditRows(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AddAuditRows(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var entries = context.ChangeTracker.Entries()
            .Where(e => e.Entity is not AuditLog
                        && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        var now = timeProvider.GetUtcNow();

        foreach (var entry in entries)
        {
            var (verb, details) = Describe(entry);

            if (details is null)
            {
                continue;
            }

            var entityType = entry.Metadata.ClrType.Name;

            context.Set<AuditLog>().Add(new AuditLog
            {
                InstitutionId = ResolveInstitutionId(entry),
                UserId = currentUser.UserId,
                Action = $"{entityType}.{verb}",
                EntityType = entityType,
                EntityId = GetKey(entry),
                OccurredAt = now,
                IpAddress = currentUser.IpAddress,
                Metadata = JsonSerializer.Serialize(details)
            });
        }
    }

    private static (string Verb, object? Details) Describe(EntityEntry entry)
    {
        switch (entry.State)
        {
            case EntityState.Added:
                return ("created", new
                {
                    after = entry.Properties.ToDictionary(p => p.Metadata.Name, p => Mask(p, p.CurrentValue))
                });

            case EntityState.Deleted:
                return ("deleted", new
                {
                    before = entry.Properties.ToDictionary(p => p.Metadata.Name, p => Mask(p, p.OriginalValue))
                });

            default:
                var changes = entry.Properties
                    .Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue))
                    .ToDictionary(
                        p => p.Metadata.Name,
                        p => (object?)new { @from = Mask(p, p.OriginalValue), to = Mask(p, p.CurrentValue) });

                // Nothing really changed: no audit row.
                return ("updated", changes.Count == 0 ? null : new { changes });
        }
    }

    private static object? Mask(PropertyEntry property, object? value) =>
        RedactedProperties.Contains(property.Metadata.Name) ? Redacted : value;

    private static string GetKey(EntityEntry entry) =>
        string.Join(",", entry.Properties.Where(p => p.Metadata.IsPrimaryKey()).Select(p => p.CurrentValue));

    // The institution the row belongs to; for rows without one, the institution of whoever is acting.
    private Guid? ResolveInstitutionId(EntityEntry entry) => entry.Entity switch
    {
        Institution institution => institution.Id,
        ITenantEntity tenantEntity => tenantEntity.InstitutionId,
        IOptionalTenantEntity optionalEntity => optionalEntity.InstitutionId,
        _ => tenant.InstitutionId
    };
}
