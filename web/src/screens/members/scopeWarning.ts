import { PERMISSION } from '../../session/permissions';
import type { CatalogueRole } from './types';

/**
 * Whether any of these roles manages members or scope (the project owner's item 36, group ج). Combined with scope
 * `assigned` it reads as a mistake — someone who manages members or scope yet sees only some clients — so the
 * screens say so in one line. It is legitimate (the two axes are independent by design), so they never refuse it.
 * Decided by permission, as GET /roles returns it, never by a role's name: every vertical's roles are covered by
 * their data. The opposite (a read-only role with `all`) is the ordinary auditor's case and is not warned about.
 */
export function managesMembersOrScope(roles: readonly CatalogueRole[]): boolean {
  return roles.some((role) => role.permissions.includes(PERMISSION.membersManage) || role.permissions.includes(PERMISSION.scopeManage));
}
