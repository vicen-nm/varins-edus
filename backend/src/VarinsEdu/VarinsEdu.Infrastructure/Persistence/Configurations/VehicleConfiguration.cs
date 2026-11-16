using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VarinsEdu.Domain.Entities;

namespace VarinsEdu.Infrastructure.Persistence.Configurations;

public class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Plate).HasMaxLength(12).IsRequired();
        builder.Property(x => x.Make).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Model).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Color).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.StatusNote).HasMaxLength(200);

        builder.HasIndex(x => x.PersonId);
        builder.HasIndex(x => new { x.InstitutionId, x.Status });

        // The database itself guarantees that a plate belongs to one live vehicle per institution.
        // Rejected and withdrawn vehicles free their plate.
        builder.HasIndex(x => new { x.InstitutionId, x.Plate })
            .IsUnique()
            .HasFilter("status IN ('pending', 'approved', 'suspended')");

        builder.HasOne<Institution>()
            .WithMany()
            .HasForeignKey(x => x.InstitutionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Person)
            .WithMany()
            .HasForeignKey(x => x.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ReviewedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
