using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VarinsEdu.Domain.Entities;

namespace VarinsEdu.Infrastructure.Persistence.Configurations;

public class InstitutionConfiguration : IEntityTypeConfiguration<Institution>
{
    public void Configure(EntityTypeBuilder<Institution> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(100).IsRequired();
        builder.Property(x => x.TimeZone).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Locale).HasMaxLength(20).IsRequired();

        builder.HasIndex(x => x.Slug).IsUnique();
    }
}
