using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VarinsEdu.Domain.Entities;

namespace VarinsEdu.Infrastructure.Persistence.Configurations;

public class AccessEventConfiguration : IEntityTypeConfiguration<AccessEvent>
{
    public void Configure(EntityTypeBuilder<AccessEvent> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Method).HasMaxLength(20).IsRequired();

        // The database itself guarantees one registration per person, service and day.
        builder.HasIndex(x => new { x.AccessServiceId, x.PersonId, x.ServiceDate }).IsUnique();
        builder.HasIndex(x => new { x.InstitutionId, x.ServiceDate });

        builder.HasOne<Institution>()
            .WithMany()
            .HasForeignKey(x => x.InstitutionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.AccessPoint)
            .WithMany()
            .HasForeignKey(x => x.AccessPointId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.AccessService)
            .WithMany()
            .HasForeignKey(x => x.AccessServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Person)
            .WithMany()
            .HasForeignKey(x => x.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.RecordedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
