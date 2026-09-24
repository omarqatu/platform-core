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
}
