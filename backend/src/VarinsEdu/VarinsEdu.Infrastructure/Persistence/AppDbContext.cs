using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Entities;

namespace VarinsEdu.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        modelBuilder.UseSnakeCaseNames();
    }
}
