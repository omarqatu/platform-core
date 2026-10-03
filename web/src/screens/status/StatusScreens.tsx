import { useIntl } from 'react-intl';
import { Link } from 'react-router';
import { messages as common } from '../../components/messages';
import { apiErrors } from '../../i18n/apiErrors';
import { messages } from './messages';

export function LoadingScreen() {
  const intl = useIntl();
  return (
    <main className="status">
      <p role="status">{intl.formatMessage(common.loading)}</p>
    </main>
  );
}

/** A path the interface does not define. */
export function NotFoundScreen() {
  const intl = useIntl();
  return (
    <main className="status">
      <h1>{intl.formatMessage(messages.notFoundTitle)}</h1>
      <p>{intl.formatMessage(messages.notFoundBody)}</p>
      <p>
        <Link to="/">{intl.formatMessage(messages.home)}</Link>
      </p>
    </main>
  );
}

/** not_permitted from the API, and nothing more: no internal detail. */
export function ForbiddenScreen() {
  const intl = useIntl();
  return (
    <main className="status">
      <h1>{intl.formatMessage(messages.forbiddenTitle)}</h1>
      <p role="alert">{intl.formatMessage(apiErrors.not_permitted)}</p>
    </main>
  );
}
