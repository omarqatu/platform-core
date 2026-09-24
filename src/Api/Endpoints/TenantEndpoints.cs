using Core;
using Core.Data;
using Core.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

public sealed record TenantChoice(Guid TenantId, string Name, string TenantStatus, string MembershipStatus);

public static class TenantEndpoints
{
    public static void MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        // The tenant-selection screen (4.3-b): app.user_id alone — membership_self and tenant_visible_to_member.
        app.MapGet("/tenants", async (CoreDbContext db, ISessionContextAccessor session, CancellationToken ct) =>
        {
            var user = session.Current.UserId!.Value;
            var tenants = await (
                from m in db.Memberships
                where m.UserId == user
                join t in db.Tenants on m.TenantId equals t.Id
                orderby t.Name
                select new TenantChoice(t.Id, t.Name, t.Status, m.Status)).ToListAsync(ct);
            return Results.Ok(new { tenants });
        }).RequireAuthorization().WithMetadata(new WithoutActiveTenantAttribute());

        // Selecting a tenant: one transaction in the requested tenant's context. UnitOfWork resolves the second
        // axis first — an active membership, and its scope row or a loud error (3.5/7, Test 18) — then the
        // tenant must be active and the membership must accept the provider this session logged in with
        // (4.2: 'password' is the only provider today). Only then does the cookie hold the tenant.
        app.MapPost("/tenants/{tenantId:guid}/select", async (Guid tenantId, HttpContext http, CoreDbContext db,
            ISessionContextAccessor session, CancellationToken ct) =>
        {
            var user = session.Current.UserId!.Value;
            var refusal = await UnitOfWork.RunAsync(db, new SessionContext(user, tenantId), async (c, token) =>
            {
                var status = await c.Tenants.Where(t => t.Id == tenantId).Select(t => t.Status).SingleAsync(token);
                if (status != "active")
                    return "tenant_not_active";
                var membership = c.Scope!.MembershipId;
                var providers = await c.MembershipAuths.Where(a => a.MembershipId == membership).Select(a => a.Provider)
                    .ToListAsync(token);
                return providers.Contains("password") ? null : "step_up_required";
            }, ct);
            if (refusal is not null)
                return Results.Json(new { error = refusal }, statusCode: StatusCodes.Status403Forbidden);

            await http.SignInAsync(SessionCookie.Scheme, SessionCookie.Principal(user, tenantId));
            return Results.NoContent();
        }).RequireAuthorization().WithMetadata(new OwnUnitsOfWorkAttribute());
    }
}
