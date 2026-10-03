import type { MessageFormatElement } from 'react-intl';
import ar from '../../compiled-locales/ar.json';
import en from '../../compiled-locales/en.json';
import type { Lang } from './locale';

// The compiled catalogues (`npm run i18n:compile`, with --ast): IntlProvider receives parsed ICU, never source text.
// Two languages, both small: bundled, so a language switch never waits on the network.
type CompiledMessages = Record<string, MessageFormatElement[]>;

export const MESSAGES: Record<Lang, CompiledMessages> = {
  ar: ar as unknown as CompiledMessages,
  en: en as unknown as CompiledMessages,
};
