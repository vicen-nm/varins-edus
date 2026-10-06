using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VarinsEdu.Domain.Entities;

namespace VarinsEdu.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();

        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(100);
        builder.Property(x => x.IpAddress).HasMaxLength(45);
        builder.Property(x => x.Metadata).HasColumnType("jsonb");

        builder.HasIndex(x => new { x.InstitutionId, x.OccurredAt });
        builder.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}
