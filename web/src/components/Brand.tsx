import { useIntl } from 'react-intl';
import { Icon } from './Icon';
import { messages } from './messages';

/** The product's mark and name (docs/ui/reference: .brand-lockup) — the shell's top bar and the sign-in screens. */
export function Brand() {
  const intl = useIntl();
  return (
    <div className="brand-lockup">
      <span className="brand-mark">
        <Icon name="layers" size={20} />
      </span>
      <span className="brand-name">{intl.formatMessage(messages.brand)}</span>
    </div>
  );
}
