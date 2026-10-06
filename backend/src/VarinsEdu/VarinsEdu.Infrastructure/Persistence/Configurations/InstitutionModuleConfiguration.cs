using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VarinsEdu.Domain.Entities;

namespace VarinsEdu.Infrastructure.Persistence.Configurations;

public class InstitutionModuleConfiguration : IEntityTypeConfiguration<InstitutionModule>
{
    public void Configure(EntityTypeBuilder<InstitutionModule> builder)
    {
        builder.HasKey(x => new { x.InstitutionId, x.ModuleKey });

        builder.Property(x => x.ModuleKey).HasMaxLength(50).IsRequired();

        builder.HasOne(x => x.Institution)
            .WithMany(x => x.Modules)
            .HasForeignKey(x => x.InstitutionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
