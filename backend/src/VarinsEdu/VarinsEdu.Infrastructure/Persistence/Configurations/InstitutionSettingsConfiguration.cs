using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VarinsEdu.Domain.Entities;

namespace VarinsEdu.Infrastructure.Persistence.Configurations;

public class InstitutionSettingsConfiguration : IEntityTypeConfiguration<InstitutionSettings>
{
    public void Configure(EntityTypeBuilder<InstitutionSettings> builder)
    {
        builder.HasKey(x => x.InstitutionId);

        builder.HasOne(x => x.Institution)
            .WithOne(x => x.Settings)
            .HasForeignKey<InstitutionSettings>(x => x.InstitutionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.DisplayName).HasMaxLength(200);
        builder.Property(x => x.LogoUrl).HasMaxLength(500);
        builder.Property(x => x.PrimaryColor).HasMaxLength(9);
        builder.Property(x => x.AccentColor).HasMaxLength(9);
    }
}
