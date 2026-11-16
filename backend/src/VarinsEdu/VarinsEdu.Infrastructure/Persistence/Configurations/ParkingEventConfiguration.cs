using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VarinsEdu.Domain.Entities;

namespace VarinsEdu.Infrastructure.Persistence.Configurations;

public class ParkingEventConfiguration : IEntityTypeConfiguration<ParkingEvent>
{
    public void Configure(EntityTypeBuilder<ParkingEvent> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.InstitutionId, x.ServiceDate });

        // The database itself guarantees that a vehicle has one open visit at most.
        builder.HasIndex(x => x.VehicleId)
            .IsUnique()
            .HasFilter("exited_at IS NULL AND auto_closed = false");

        builder.HasOne<Institution>()
            .WithMany()
            .HasForeignKey(x => x.InstitutionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Vehicle)
            .WithMany()
            .HasForeignKey(x => x.VehicleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Person)
            .WithMany()
            .HasForeignKey(x => x.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.EnteredByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ExitedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
