import { useEffect, useRef } from 'react';

/**
 * An error the user must notice: role="alert", and focus moves to it when it appears or changes, so a keyboard or
 * screen-reader user lands on it. `id` lets a field point at it (aria-describedby).
 */
export function ErrorAlert({ message, id }: { message: string | null; id?: string }) {
  const ref = useRef<HTMLParagraphElement>(null);
  useEffect(() => {
    if (message) ref.current?.focus();
  }, [message]);
  if (!message) return null;
  return (
    <p ref={ref} id={id} role="alert" tabIndex={-1} className="alert">
      {message}
    </p>
  );
}
