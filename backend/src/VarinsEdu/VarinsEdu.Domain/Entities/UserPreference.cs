using VarinsEdu.Domain.Constants;

namespace VarinsEdu.Domain.Entities;

// Display preferences only. No health or diagnosis data is ever stored here.
public class UserPreference
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string UiMode { get; set; } = UiModes.Standard;
    public double FontScale { get; set; } = 1.0;
    public bool ReadAloud { get; set; }
    public bool HighContrast { get; set; }
    public bool ReducedMotion { get; set; }
}
