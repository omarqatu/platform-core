import type { ReactNode } from 'react';

/**
 * The <ltr> tag of a message: what it wraps reads left-to-right inside a sentence of either direction — a date, a
 * number written as text (a <bdi dir="ltr">, so it neither takes nor gives direction to the words around it).
 */
export const ltr = (chunks: ReactNode[]) => <bdi dir="ltr">{chunks}</bdi>;
