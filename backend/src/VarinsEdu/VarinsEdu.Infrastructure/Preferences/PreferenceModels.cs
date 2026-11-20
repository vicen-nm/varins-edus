namespace VarinsEdu.Infrastructure.Preferences;

// Display settings only. No health or diagnosis data is ever stored here, and nothing says why a person uses them.
public sealed record DisplayPreferences(
    string UiMode,
    double FontScale,
    bool ReadAloud,
    bool HighContrast,
    bool ReducedMotion);

public static class DisplayDefaults
{
    public const string Standard = "standard";
    public const string Simple = "simple";

    public const double MinFontScale = 1.0;
    public const double MaxFontScale = 2.0;

    public static readonly DisplayPreferences Default = new(Standard, 1.0, false, false, false);
}
