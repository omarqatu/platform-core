import { useState } from 'react';
import { useIntl } from 'react-intl';
import { Link, useNavigate } from 'react-router';
import { useAuthenticated, useSession } from '../session/SessionProvider';
import { LanguageSwitcher } from './LanguageSwitcher';
import { messages } from './messages';

/** On every screen after signing in: who, in which organization, the language, switching and signing out. */
export function IdentityBar() {
  const intl = useIntl();
  const navigate = useNavigate();
  const { switchTenant, signOut } = useSession();
  const { username, activeTenant } = useAuthenticated();
  const [leaving, setLeaving] = useState(false);

  const onSwitch = () => {
    switchTenant(); // the tenant's screens are gone before the selection shows
    navigate('/organizations');
  };
  const onSignOut = async () => {
    setLeaving(true);
    try {
      await signOut();
    } finally {
      navigate('/login', { replace: true });
    }
  };

  return (
    <header className="identity" aria-label={intl.formatMessage(messages.identityLabel)}>
      <dl>
        <div>
          <dt>{intl.formatMessage(messages.signedInAs)}</dt>
          <dd data-testid="identity-user">
            <bdi dir="auto">{username}</bdi>
          </dd>
        </div>
        <div>
          <dt>{intl.formatMessage(messages.organization)}</dt>
          <dd data-testid="identity-organization">
            {activeTenant ? <bdi dir="auto">{activeTenant.name}</bdi> : intl.formatMessage(messages.noOrganization)}
          </dd>
        </div>
      </dl>
      {activeTenant && (
        <nav aria-label={intl.formatMessage(messages.navigation)}>
          <Link to="/app/subscriptions">{intl.formatMessage(messages.subscriptions)}</Link>
        </nav>
      )}
      <div className="actions">
        <LanguageSwitcher />
        {activeTenant && (
          <button type="button" onClick={onSwitch}>
            {intl.formatMessage(messages.switchOrganization)}
          </button>
        )}
        <button type="button" onClick={onSignOut} disabled={leaving}>
          {intl.formatMessage(messages.signOut)}
        </button>
      </div>
    </header>
  );
}
