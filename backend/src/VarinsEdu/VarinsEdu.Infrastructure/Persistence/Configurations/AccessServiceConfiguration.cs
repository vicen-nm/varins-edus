using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VarinsEdu.Domain.Entities;

namespace VarinsEdu.Infrastructure.Persistence.Configurations;

public class AccessServiceConfiguration : IEntityTypeConfiguration<AccessService>
{
    public void Configure(EntityTypeBuilder<AccessService> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(50).IsRequired();

        builder.HasIndex(x => new { x.AccessPointId, x.Name }).IsUnique();

        builder.HasOne<Institution>()
            .WithMany()
            .HasForeignKey(x => x.InstitutionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AccessPoint>()
            .WithMany()
            .HasForeignKey(x => x.AccessPointId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
