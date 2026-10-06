using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;

namespace VarinsEdu.Domain.Entities;

public class ImportBatch : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid InstitutionId { get; set; }

    // The user who ran the import.
    public Guid UserId { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = ImportStatuses.Pending;

    // JSON text: which Excel column maps to which field.
    public string? ColumnMapping { get; set; }

    public int TotalRows { get; set; }
    public int OkRows { get; set; }
    public int ErrorRows { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
