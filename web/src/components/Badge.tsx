import type { ReactNode } from 'react';
import { Icon, type IconName } from './Icon';

export type BadgeTone = 'success' | 'warning' | 'danger' | 'neutral';

const ICONS: Record<BadgeTone, IconName | null> = { success: 'check', warning: 'warn', danger: 'warn', neutral: null };

/** A status (docs/ui/reference: .badge): an icon and a word together, never a color alone. */
export function Badge({ tone, children }: { tone: BadgeTone; children: ReactNode }) {
  const icon = ICONS[tone];
  return (
    <span className={`badge ${tone}`}>
      {icon && <Icon name={icon} size={14} />}
      {children}
    </span>
  );
}
