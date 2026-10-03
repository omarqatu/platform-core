import { useId } from 'react';
import { useIntl, type MessageDescriptor } from 'react-intl';
import { useLocale } from '../i18n/I18nProvider';
import { isLang, LANGS, type Lang } from '../i18n/locale';
import { messages } from './messages';

const NAMES: Record<Lang, MessageDescriptor> = { ar: messages.arabic, en: messages.english };
const TAGS: Record<Lang, string> = { ar: 'ar', en: 'en' };

/** The language, kept in the "lang" cookie by I18nProvider: no other storage. */
export function LanguageSwitcher() {
  const intl = useIntl();
  const { lang, setLang } = useLocale();
  const id = useId();
  return (
    <span className="language">
      <label htmlFor={id}>{intl.formatMessage(messages.language)}</label>
      <select id={id} value={lang} onChange={(e) => isLang(e.target.value) && setLang(e.target.value)}>
        {LANGS.map((l) => (
          <option key={l} value={l} lang={TAGS[l]}>
            {intl.formatMessage(NAMES[l])}
          </option>
        ))}
      </select>
    </span>
  );
}
