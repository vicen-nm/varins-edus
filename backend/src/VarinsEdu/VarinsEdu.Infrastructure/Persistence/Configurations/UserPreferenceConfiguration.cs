using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VarinsEdu.Domain.Entities;

namespace VarinsEdu.Infrastructure.Persistence.Configurations;

public class UserPreferenceConfiguration : IEntityTypeConfiguration<UserPreference>
{
    public void Configure(EntityTypeBuilder<UserPreference> builder)
    {
        builder.HasKey(x => x.UserId);

        builder.Property(x => x.UiMode).HasMaxLength(20).IsRequired();

        builder.HasOne(x => x.User)
            .WithOne()
            .HasForeignKey<UserPreference>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
