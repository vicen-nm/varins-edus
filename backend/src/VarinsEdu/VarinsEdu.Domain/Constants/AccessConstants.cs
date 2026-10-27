namespace VarinsEdu.Domain.Constants;

public static class AccessPointKinds
{
    public const string Cafeteria = "cafeteria";
    public const string Gate = "gate";

    public static readonly string[] All = [Cafeteria, Gate];
}

public static class AccessMethods
{
    public const string Qr = "qr";
    public const string Manual = "manual";
}
