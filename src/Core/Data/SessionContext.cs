namespace Core.Data;

/// <summary>
/// The first-axis context of one transaction: the authenticated user and the
/// active tenant (4.3). The second-axis variables are resolved per transaction
/// (3.5/6) and are never part of this record.
/// </summary>
public sealed record SessionContext(Guid? UserId, Guid? TenantId)
{
    public static readonly SessionContext None = new(null, null);
}

/// <summary>
/// Supplies the current request's session. Its only source is the authenticated
/// session, never a request payload, header, or token claim (3.5/6).
/// </summary>
public interface ISessionContextAccessor
{
    SessionContext Current { get; }

    /// <summary>
    /// The security stamp the session carries (Amendment 01 §2.1), or null when it carries none. It is not part
    /// of <see cref="SessionContext"/>: no transaction variable is set from it — it is compared with the user's
    /// row on every request, and a difference ends the session.
    /// </summary>
    Guid? SecurityStamp => null;
}
