namespace VarinsEdu.Domain.Entities;

public class Institution
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string TimeZone { get; set; } = "America/Costa_Rica";
    public string Locale { get; set; } = "es-CR";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    public InstitutionSettings? Settings { get; set; }
    public ICollection<InstitutionModule> Modules { get; set; } = new List<InstitutionModule>();
}
