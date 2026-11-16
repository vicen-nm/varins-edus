namespace VarinsEdu.Domain.Constants;

public static class VehicleStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Suspended = "suspended";
    public const string Withdrawn = "withdrawn";

    // The ones that count toward the limit per student and keep the plate reserved.
    public static readonly string[] Active = [Pending, Approved, Suspended];

    public static readonly string[] All = [Pending, Approved, Rejected, Suspended, Withdrawn];
}
