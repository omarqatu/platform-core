import { useEffect, useState, type ReactNode } from 'react';
import { useIntl } from 'react-intl';
import { Link, NavLink, useNavigate } from 'react-router';
import { useLocale } from '../i18n/I18nProvider';
import { useStayed } from '../navigation/useStayed';
import { CHANGE_PATH } from '../screens/password/ChangePasswordScreen';
import { getPermissions, PERMISSION } from '../session/permissions';
import { useAuthenticated, useSession } from '../session/SessionProvider';
import { useAppearance } from '../theme/ThemeProvider';
import { AccentPicker } from './AccentPicker';
import { Brand } from './Brand';
import { Icon } from './Icon';
import { messages } from './messages';
import './AppShell.css';

/** The sidebar's own path to the member screens, so the shell and the router name it once. */
export const MEMBERS_PATH = '/app/members';

/**
 * Whether the sidebar offers the member screens: `core.members.manage` or `core.scope.manage`, from
 * `GET /me/scope` and from nowhere else (OPEN_ITEMS 77) — never the cookie, a header or anything the browser
 * keeps.
 *
 * Three things this deliberately does:
 *
 * - **starts false and only ever turns on.** A link shown and then withdrawn is worse than one that arrives a
 *   moment late, and the screens themselves are guarded by the server either way;
 * - **re-asks when the organization changes**, keyed on the tenant's id: permissions belong to the membership,
 *   not to the person (§4.8), and the same person is an owner in one organization and an operator in another;
 * - **swallows the failure.** The shell is drawn around every screen, including the ones that report an error.
 *   If `/me/scope` is unreachable the screen under it says so — the sidebar must not turn that into a second
 *   error message, and a missing link is the safe way to be wrong here.
 */
function useManagesMembersOrScope(tenantId: string | undefined): boolean {
  // The answer is kept with the organization it answers for, and reset **while rendering** when that changes —
  // React's pattern for state that follows a prop, the one the list's filter uses (§15). An effect would reset
  // it a commit late, which is the frame in which the previous organization's link would still be offered.
  const [state, setState] = useState<{ tenant: string | undefined; allowed: boolean }>({ tenant: tenantId, allowed: false });
  if (state.tenant !== tenantId) setState({ tenant: tenantId, allowed: false });

  useEffect(() => {
    if (tenantId === undefined) return;
    const abort = new AbortController();
    getPermissions(abort.signal).then(
      (permissions) => {
        if (!abort.signal.aborted)
          setState({ tenant: tenantId, allowed: permissions.has(PERMISSION.membersManage) || permissions.has(PERMISSION.scopeManage) });
      },
      () => undefined,
    );
    return () => abort.abort();
  }, [tenantId]);

  return state.tenant === tenantId && state.allowed;
}

/** Up to two letters for the avatar: the first of each of the first two words, or the first two of a single word. */
export function initials(name: string): string {
  const words = name.trim().split(/[\s._-]+/).filter(Boolean);
  const letters = words.length > 1 ? [words[0]![0], words[1]![0]] : [...(words[0] ?? '')].slice(0, 2);
  return letters.join('').toLocaleUpperCase();
}

/**
 * The global shell on every screen after signing in (docs/ui/reference, "Global shell"): the top bar — who, in which
 * organization, the language, the display mode, the accent, switching and signing out — and, inside an organization,
 * the sidebar with its modules. The screen itself is `children`, after a skip link to it.
 */
export function AppShell({ children }: { children: ReactNode }) {
  const intl = useIntl();
  const navigate = useNavigate();
  const { switchTenant, signOut } = useSession();
  const { username, activeTenant } = useAuthenticated();
  const { lang, setLang } = useLocale();
  const { theme, setTheme } = useAppearance();
  const [leaving, setLeaving] = useState(false);
  const begin = useStayed();
  const canManageMembersOrScope = useManagesMembersOrScope(activeTenant?.tenant_id);

  // The tenant's screens are gone at once; the selection shows after the API has forgotten the tenant (deselect) —
  // unless the person has gone somewhere else meanwhile ("Change password" is in this bar): then that is theirs.
  const onSwitch = async () => {
    const stayed = begin();
    try {
      await switchTenant();
    } finally {
      if (stayed()) navigate('/organizations');
    }
  };
  const onSignOut = async () => {
    setLeaving(true);
    try {
      await signOut();
    } finally {
      navigate('/login', { replace: true });
    }
  };

  const other = lang === 'ar' ? 'en' : 'ar';

  return (
    <div className={activeTenant ? 'shell with-sidebar' : 'shell'}>
      <a className="skip-link" href="#content">
        {intl.formatMessage(messages.skipToContent)}
      </a>
      <header className="topbar" aria-label={intl.formatMessage(messages.identityLabel)}>
        <Brand />
        <div className="context">
          <div className="context-control">
            <span className="context-icon" aria-hidden="true">
              {activeTenant ? initials(activeTenant.name) : <Icon name="layers" size={15} />}
            </span>
            <span className="control-copy">
              <small>{intl.formatMessage(messages.organization)}</small>
              <span data-testid="identity-organization">
                {activeTenant ? <bdi dir="auto">{activeTenant.name}</bdi> : intl.formatMessage(messages.noOrganization)}
              </span>
            </span>
          </div>
          <div className="context-control user">
            <span className="avatar" aria-hidden="true">
              {initials(username)}
            </span>
            <span className="control-copy">
              <small>{intl.formatMessage(messages.signedInAs)}</small>
              <span data-testid="identity-user">
                <bdi dir="auto">{username}</bdi>
              </span>
            </span>
          </div>
        </div>
        <div className="shell-controls">
          {/* The other language, named in itself: its visible name is its accessible name. */}
          <button type="button" className="icon-button" lang={other} onClick={() => setLang(other)}>
            <Icon name="globe" />
            <span>{intl.formatMessage(other === 'ar' ? messages.arabic : messages.english)}</span>
          </button>
          <button
            type="button"
            className="icon-button"
            aria-label={intl.formatMessage(messages.darkMode)}
            aria-pressed={theme === 'dark'}
            onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}
          >
            <Icon name="moon" />
          </button>
          <AccentPicker />
          {activeTenant && (
            <button type="button" className="btn secondary" onClick={onSwitch}>
              <Icon name="swap" size={16} />
              <span className="label">{intl.formatMessage(messages.switchOrganization)}</span>
            </button>
          )}
          {/* Changing one's own password needs no organization, so it is here and not in the sidebar. */}
          <Link to={CHANGE_PATH} className="btn-link">
            <Icon name="lock" size={16} />
            <span className="label">{intl.formatMessage(messages.changePassword)}</span>
          </Link>
          <button type="button" className="btn ghost" onClick={onSignOut} disabled={leaving}>
            <Icon name="logout" size={16} directional />
            <span className="label">{intl.formatMessage(messages.signOut)}</span>
          </button>
        </div>
      </header>
      {activeTenant && (
        <aside className="sidebar">
          <nav aria-label={intl.formatMessage(messages.navigation)}>
            <NavLink to="/app/subscriptions" className="side-link">
              <Icon name="grid" />
              <span>{intl.formatMessage(messages.subscriptions)}</span>
            </NavLink>
            <NavLink to="/app/clients" className="side-link">
              <Icon name="layers" />
              <span>{intl.formatMessage(messages.clients)}</span>
            </NavLink>
            {/* Shown only to whoever may reach it: GET /members takes core.members.manage or core.scope.manage,
                and offering a link that answers 403 is the shown-then-refused shape the rest of the interface
                avoids. Absent until /me/scope has answered — never shown first and taken away. */}
            {canManageMembersOrScope && (
              <NavLink to={MEMBERS_PATH} className="side-link">
                <Icon name="users" />
                <span>{intl.formatMessage(messages.members)}</span>
              </NavLink>
            )}
          </nav>
        </aside>
      )}
      <div id="content" className="shell-content" tabIndex={-1}>
        {children}
      </div>
    </div>
  );
}
