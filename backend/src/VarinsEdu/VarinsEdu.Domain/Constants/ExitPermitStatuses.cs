namespace VarinsEdu.Domain.Constants;

public static class ExitPermitStatuses
{
    public const string Active = "active";
    public const string Used = "used";
    public const string Cancelled = "cancelled";

    // Never stored: an active permit whose window already passed. It is worked out when it is read.
    public const string Expired = "expired";
}
