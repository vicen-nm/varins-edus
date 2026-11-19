using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VarinsEdu.Domain.Entities;

namespace VarinsEdu.Infrastructure.Persistence.Configurations;

public class ExitPermitConfiguration : IEntityTypeConfiguration<ExitPermit>
{
    public void Configure(EntityTypeBuilder<ExitPermit> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Reason).HasMaxLength(200).IsRequired();

        // Two guards using the same permit at once: the second update does not find the status it expects,
        // so it fails and nothing is saved.
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired().IsConcurrencyToken();

        builder.HasIndex(x => new { x.InstitutionId, x.ServiceDate });
        builder.HasIndex(x => new { x.PersonId, x.ServiceDate });

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
            .HasForeignKey(x => x.CancelledByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UsedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
