import { createContext, useCallback, useContext, useLayoutEffect, useMemo, useState, type ReactNode } from 'react';
import { IntlProvider, type IntlConfig } from 'react-intl';
import { browserLanguages, DIR, initialLang, INTL_LOCALE, langCookie, type Lang } from './locale';
import { MESSAGES } from './messages';

interface LocaleContextValue {
  lang: Lang;
  setLang: (lang: Lang) => void;
}

const LocaleContext = createContext<LocaleContextValue | null>(null);

// A missing translation or a malformed message: a warning while developing, nothing in production.
const onError: IntlConfig['onError'] = (error) => {
  if (import.meta.env.DEV) console.warn(error);
};

/**
 * Wraps the application: the language (cookie "lang", then the browser's languages, then Arabic), the compiled
 * messages, and the document's lang and dir, kept in step with the language and the cookie on every change.
 */
export function I18nProvider({ children, initial }: { children: ReactNode; initial?: Lang }) {
  const [lang, setLangState] = useState<Lang>(() => initial ?? initialLang(document.cookie, browserLanguages()));

  // Before paint, so the first frame already has the right direction.
  useLayoutEffect(() => {
    document.documentElement.lang = lang;
    document.documentElement.dir = DIR[lang];
    document.cookie = langCookie(lang);
  }, [lang]);

  const setLang = useCallback((next: Lang) => setLangState(next), []);
  const value = useMemo(() => ({ lang, setLang }), [lang, setLang]);

  return (
    <LocaleContext.Provider value={value}>
      <IntlProvider locale={INTL_LOCALE[lang]} defaultLocale="en" messages={MESSAGES[lang]} onError={onError}>
        {children}
      </IntlProvider>
    </LocaleContext.Provider>
  );
}

/** The current language and its setter — for the language switcher. */
export function useLocale(): LocaleContextValue {
  const value = useContext(LocaleContext);
  if (!value) throw new Error('useLocale() is used outside I18nProvider.');
  return value;
}
