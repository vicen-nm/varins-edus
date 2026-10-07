using System.Reflection;
using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Entities;

namespace VarinsEdu.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant currentTenant)
    : DbContext(options)
{
    public DbSet<Institution> Institutions => Set<Institution>();
    public DbSet<InstitutionSettings> InstitutionSettings => Set<InstitutionSettings>();
    public DbSet<InstitutionModule> InstitutionModules => Set<InstitutionModule>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<StudentProfile> StudentProfiles => Set<StudentProfile>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ActivationCode> ActivationCodes => Set<ActivationCode>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // The query filters read these two properties every time a query runs.
    private Guid? CurrentInstitutionId => currentTenant.InstitutionId;
    private bool IgnoreTenantFilter => currentTenant.IsPlatformScope;

    private static readonly MethodInfo ApplyTenantFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // 1) Every entity that implements ITenantEntity gets the same filter.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(e => typeof(ITenantEntity).IsAssignableFrom(e.ClrType))
                     .ToList())
        {
            ApplyTenantFilterMethod
                .MakeGenericMethod(entityType.ClrType)
                .Invoke(this, new object[] { modelBuilder });
        }

        // 2) Entities with a nullable InstitutionId need their own filter.
        //    The "!= null" guard matters: without it, null == null would expose platform rows.
        modelBuilder.Entity<Institution>().HasQueryFilter(e =>
            IgnoreTenantFilter || e.Id == CurrentInstitutionId);

        modelBuilder.Entity<User>().HasQueryFilter(e =>
            IgnoreTenantFilter || (CurrentInstitutionId != null && e.InstitutionId == CurrentInstitutionId));

        modelBuilder.Entity<Role>().HasQueryFilter(e =>
            IgnoreTenantFilter || (CurrentInstitutionId != null && e.InstitutionId == CurrentInstitutionId));

        modelBuilder.Entity<AuditLog>().HasQueryFilter(e =>
            IgnoreTenantFilter || (CurrentInstitutionId != null && e.InstitutionId == CurrentInstitutionId));

        // 3) Child and join entities follow their parent.
        modelBuilder.Entity<UserRole>().HasQueryFilter(e =>
            IgnoreTenantFilter || (CurrentInstitutionId != null && e.User.InstitutionId == CurrentInstitutionId));

        modelBuilder.Entity<RolePermission>().HasQueryFilter(e =>
            IgnoreTenantFilter || (CurrentInstitutionId != null && e.Role.InstitutionId == CurrentInstitutionId));

        modelBuilder.Entity<UserPreference>().HasQueryFilter(e =>
            IgnoreTenantFilter || (CurrentInstitutionId != null && e.User.InstitutionId == CurrentInstitutionId));

        modelBuilder.UseSnakeCaseNames();
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantEntity
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            IgnoreTenantFilter || e.InstitutionId == CurrentInstitutionId);
    }
}
