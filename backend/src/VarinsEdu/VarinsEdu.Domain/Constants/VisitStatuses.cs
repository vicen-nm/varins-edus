namespace VarinsEdu.Domain.Constants;

public static class VisitStatuses
{
    public const string Pending = "pending";
    public const string Inside = "inside";
    public const string Exited = "exited";
    public const string Rejected = "rejected";

    // Never stored: a visit left pending or inside on a previous day. They are worked out when read.
    public const string Expired = "expired";
    public const string NoExit = "no_exit";

    // The visits that count as "already here": the same person does not need another one.
    public static readonly string[] Open = [Pending, Inside];
}
