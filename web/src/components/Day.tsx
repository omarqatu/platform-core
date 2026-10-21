import { ltr } from './ltr';

/**
 * A calendar day as the platform writes it everywhere (the project owner's item 5): yyyy-MM-dd, the value the API
 * gives, shown as it is — never through Intl, so its digits are Western by construction and no time zone can move
 * it. Left-to-right in either language (the <ltr> tag), on one line, digits of one width (.day).
 */
export function Day({ day }: { day: string }) {
  return (
    <time dateTime={day} className="day">
      {ltr([day])}
    </time>
  );
}
