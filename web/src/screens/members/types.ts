/** A role of the organization, from GET /roles. Named from the row, as a category is — data, not messages. */
export interface RoleItem {
  id: string;
  code: string;
  name_ar: string;
  name_en: string;
  is_system: boolean;
}

/**
 * A membership, from GET /members. `scope_mode` is null where the caller does not hold `core.scope.manage`:
 * `membership_scope_read` admits no colleague's row to them (PLATFORM_CORE §4.8, 1.8), so the server returns the
 * absence rather than a value. The screen renders no column at all from that — never an empty one.
 */
export interface MemberItem {
  membership_id: string;
  full_name: string;
  email: string;
  username: string;
  status: 'active' | 'disabled' | 'left';
  roles: RoleItem[];
  scope_mode: 'all' | 'assigned' | null;
  is_self: boolean;
}

/** A pending invitation, from GET /members/invitations. It never carries the token (DEVELOPER_GUIDE §9). */
export interface InvitationItem {
  id: string;
  email: string;
  role_id: string;
  intended_scope_mode: 'all' | 'assigned';
  status: 'pending' | 'accepted' | 'revoked' | 'expired';
  expires_at: string;
  created_at: string;
}

/** A role's name in the interface's language, from the row (the catalogue pattern: `name_ar`/`name_en`). */
export function roleName(role: RoleItem, lang: string): string {
  return lang === 'ar' ? role.name_ar : role.name_en;
}

/**
 * Whether a pending invitation has run out, derived from `expires_at` — because **nothing writes
 * `status = 'expired'`**: the column accepts the value and no code path in `src/` ever sets it, exactly as
 * `subscriptionStatus` derives "expired" from `ends_on` while the row stays `active`. An invitation whose time has
 * passed is refused by the acceptance path on its own date, so the list must say so from the same date.
 */
export function invitationExpired(invitation: InvitationItem, now: Date = new Date()): boolean {
  return invitation.status === 'pending' && new Date(invitation.expires_at).getTime() <= now.getTime();
}
