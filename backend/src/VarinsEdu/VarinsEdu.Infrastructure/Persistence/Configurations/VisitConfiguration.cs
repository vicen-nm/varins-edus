using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VarinsEdu.Domain.Entities;

namespace VarinsEdu.Infrastructure.Persistence.Configurations;

public class VisitConfiguration : IEntityTypeConfiguration<Visit>
{
    public void Configure(EntityTypeBuilder<Visit> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.FullName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.IdentificationNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(30);
        builder.Property(x => x.Host).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SignaturePng).IsRequired();

        // Two guards handling the same visit at once: the second update does not find the status it expects,
        // so it fails and nothing is saved.
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired().IsConcurrencyToken();

        builder.HasIndex(x => new { x.InstitutionId, x.ServiceDate });
        builder.HasIndex(x => new { x.InstitutionId, x.IdentificationNumber, x.ServiceDate });

        builder.HasOne<Institution>()
            .WithMany()
            .HasForeignKey(x => x.InstitutionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.HandledByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ExitedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
