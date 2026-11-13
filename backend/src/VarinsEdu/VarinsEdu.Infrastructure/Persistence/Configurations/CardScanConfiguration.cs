using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VarinsEdu.Domain.Entities;

namespace VarinsEdu.Infrastructure.Persistence.Configurations;

public class CardScanConfiguration : IEntityTypeConfiguration<CardScan>
{
    public void Configure(EntityTypeBuilder<CardScan> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Nonce).HasMaxLength(64).IsRequired();

        // The database itself guarantees one use per token, even with two scans at the same time.
        builder.HasIndex(x => new { x.PersonId, x.Nonce }).IsUnique();
        builder.HasIndex(x => new { x.InstitutionId, x.ScannedAt });

        builder.HasOne<Institution>()
            .WithMany()
            .HasForeignKey(x => x.InstitutionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Person>()
            .WithMany()
            .HasForeignKey(x => x.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ScannedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
