import type { ReactNode } from 'react';

/** The reference's line icons (docs/ui/reference): decorative only, so always aria-hidden — the text names the control. */
export type IconName = 'grid' | 'layers' | 'globe' | 'moon' | 'check' | 'warn' | 'palette' | 'logout' | 'swap' | 'lock';

const PATHS: Record<IconName, ReactNode> = {
  grid: (
    <>
      <rect x="3" y="3" width="7" height="7" rx="1" />
      <rect x="14" y="3" width="7" height="7" rx="1" />
      <rect x="3" y="14" width="7" height="7" rx="1" />
      <rect x="14" y="14" width="7" height="7" rx="1" />
    </>
  ),
  layers: (
    <>
      <path d="m12 3-9 5 9 5 9-5-9-5Z" />
      <path d="m3 12 9 5 9-5" />
      <path d="m3 16 9 5 9-5" />
    </>
  ),
  globe: (
    <>
      <circle cx="12" cy="12" r="9" />
      <path d="M3 12h18M12 3a15 15 0 0 1 0 18M12 3a15 15 0 0 0 0 18" />
    </>
  ),
  moon: <path d="M20 15.2A8.8 8.8 0 0 1 8.8 4 9 9 0 1 0 20 15.2Z" />,
  check: <path d="m5 12 4 4L19 6" />,
  warn: (
    <>
      <path d="M12 3 2 21h20L12 3Z" />
      <path d="M12 9v5M12 18h.01" />
    </>
  ),
  palette: (
    <>
      <path d="M12 3a9 9 0 0 0 0 18h1.5a1.5 1.5 0 0 0 0-3H12a2 2 0 0 1 0-4h3.5A5.5 5.5 0 0 0 21 8.5C21 5.5 17 3 12 3Z" />
      <circle cx="7.5" cy="10.5" r=".8" fill="currentColor" stroke="none" />
      <circle cx="10" cy="7" r=".8" fill="currentColor" stroke="none" />
      <circle cx="14" cy="6.5" r=".8" fill="currentColor" stroke="none" />
      <circle cx="17" cy="9" r=".8" fill="currentColor" stroke="none" />
    </>
  ),
  logout: (
    <>
      <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
      <path d="m16 17 5-5-5-5M21 12H9" />
    </>
  ),
  swap: (
    <>
      <path d="m16 3 4 4-4 4M20 7H4" />
      <path d="m8 21-4-4 4-4M4 17h16" />
    </>
  ),
  lock: (
    <>
      <rect x="4" y="10" width="16" height="11" rx="2" />
      <path d="M8 10V7a4 4 0 0 1 8 0v3" />
    </>
  ),
};

/** `directional` icons mirror in right-to-left (an arrow points the reading way); the others never do. */
export function Icon({ name, size = 18, directional = false }: { name: IconName; size?: number; directional?: boolean }) {
  return (
    <svg
      className={directional ? 'icon directional' : 'icon'}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      {PATHS[name]}
    </svg>
  );
}
