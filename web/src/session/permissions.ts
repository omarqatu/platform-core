import { getJson } from '../api/client';

/** The permission codes the screens gate on (Amendment 01 §4.2; the module's own, 0008 and 0013). */
export const PERMISSION = {
  subscriptionsWrite: 'subscriptions.write',
  clientsWrite: 'subscriptions.clients.write',
  clientsManage: 'subscriptions.clients.manage',
  // The core's two management permissions (PLATFORM_CORE §3.10, §4.8). They are orthogonal — a member may hold
  // either without the other, and the platform's own seed has a member with scope management and no member
  // management — so the member screens gate on each of them separately, never on "one of them" standing for both.
  membersManage: 'core.members.manage',
  scopeManage: 'core.scope.manage',
} as const;

/**
 * The caller's permissions in the active tenant, from GET /me/scope — the only way the interface learns them: never
 * from the cookie, a header or anything the interface keeps (OPEN_ITEMS 77). Read with the screen's own data, on
 * every load of it, so it is as old as the screen and no older: a role changed elsewhere shows on the next load. A
 * courtesy that hides what the server would refuse — the server still refuses it.
 */
export async function getPermissions(signal?: AbortSignal): Promise<ReadonlySet<string>> {
  const scope = await getJson<{ permissions: string[] }>('/me/scope', signal);
  return new Set(scope.permissions);
}
