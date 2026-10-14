namespace VarinsEdu.Infrastructure.Auditing;

// Lets one operation (a bulk import) skip the per-row audit entries
// and be recorded as a whole instead.
public sealed class AuditSuppression
{
    public bool IsSuppressed { get; private set; }

    // Usage: using (suppression.Begin()) { ... }. Suppression always ends with the block.
    public IDisposable Begin()
    {
        IsSuppressed = true;
        return new Scope(this);
    }

    private sealed class Scope(AuditSuppression owner) : IDisposable
    {
        public void Dispose() => owner.IsSuppressed = false;
    }
}
