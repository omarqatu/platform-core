using System.Text.Json;
using Core;
using Core.Data;

namespace Api.Endpoints;

public sealed record AuditItem(Guid Id, string Action, string EntityType, Guid? EntityId, Guid? ActorId, string ActorType,
    JsonElement? OldValue, JsonElement? NewValue, DateTime CreatedAt);

/// <summary>
/// The audit log's surface (PLATFORM_CORE 7, PROOF_SPEC T5): read under audit_read — the tenant, and scope_all —
/// so an assigned member reads zero rows, and the response declares scope_mode rather than showing emptiness
/// (Test 25). Its permission is core.audit.read (5; the project owner's decision in T5).
/// </summary>
public static class AuditEndpoints
{
    public const string Read = "core.audit.read";

    public static void MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/audit-log", async (int? offset, int? limit, CoreDbContext db, CancellationToken ct) =>
        {
            if (db.Scope is null)
                return Results.Json(new { error = "no_active_tenant" }, statusCode: StatusCodes.Status409Conflict);
            var scope = await Permissions.RequireAsync(db, Read, ct);
            var page = await ScopedList.PageAsync(scope,
                db.AuditLog.OrderBy(a => a.Id), offset, limit, ct);
            return Results.Ok(new ScopedList<AuditItem>(page.ScopeMode, page.VisibleCount, page.TotalCount, page.HasMoreInScope,
                page.Items.Select(a => new AuditItem(a.Id, a.Action, a.EntityType, a.EntityId, a.ActorId, a.ActorType,
                    Json(a.OldValue), Json(a.NewValue), a.CreatedAt)).ToList()));
        }).RequireAuthorization();
    }

    private static JsonElement? Json(string? value) => value is null ? null : JsonDocument.Parse(value).RootElement.Clone();
}
