using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Entities;

namespace VarinsEdu.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Institution> Institutions => Set<Institution>();
    public DbSet<InstitutionSettings> InstitutionSettings => Set<InstitutionSettings>();
    public DbSet<InstitutionModule> InstitutionModules => Set<InstitutionModule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        modelBuilder.UseSnakeCaseNames();
    }
}
