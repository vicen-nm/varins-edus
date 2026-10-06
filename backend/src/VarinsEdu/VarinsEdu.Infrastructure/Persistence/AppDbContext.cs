using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Entities;

namespace VarinsEdu.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Institution> Institutions => Set<Institution>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        modelBuilder.UseSnakeCaseNames();
    }
}
