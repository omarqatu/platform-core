/**
 * The password rule as the interface checks it (OPEN_ITEMS 62): minimum length 12, no composition rules.
 *
 * The number is the server's — `PasswordPolicy.MinimumLength` in `src/Core/Identity/PasswordPolicy.cs` — and the
 * server is the enforcement: every endpoint that writes a password refuses a shorter one with
 * `password_too_short`. This copy exists so the screens can say the rule before the round trip rather than after
 * it. `checks/local/check-password-policy.sh` holds it, and the number inside each sentence that states the
 * rule, to the server's constant (OPEN_ITEMS 65) — change one and the check names the rest.
 */
export const MINIMUM_PASSWORD_LENGTH = 12;

/** Whether a password passes the rule the screens check. The server checks the same thing, and decides. */
export function isLongEnough(password: string): boolean {
  return password.length >= MINIMUM_PASSWORD_LENGTH;
}
