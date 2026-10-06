using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VarinsEdu.Domain.Entities;

namespace VarinsEdu.Infrastructure.Persistence.Configurations;

public class ActivationCodeConfiguration : IEntityTypeConfiguration<ActivationCode>
{
    public void Configure(EntityTypeBuilder<ActivationCode> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CodeHash).HasMaxLength(200).IsRequired();

        builder.HasIndex(x => x.PersonId);

        builder.HasOne<Institution>()
            .WithMany()
            .HasForeignKey(x => x.InstitutionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Person>()
            .WithMany()
            .HasForeignKey(x => x.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ImportBatch>()
            .WithMany()
            .HasForeignKey(x => x.ImportBatchId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
