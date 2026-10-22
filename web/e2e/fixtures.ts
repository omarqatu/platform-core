import { createHash, randomUUID } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, request, type APIRequestContext, type Page } from '@playwright/test';
import pg from 'pg';

const root = join(dirname(fileURLToPath(import.meta.url)), '../..');
const conformance = JSON.parse(readFileSync(join(root, 'tests/Conformance/appsettings.json'), 'utf8'));

export const BASE_URL = process.env.E2E_BASE_URL ?? 'http://127.0.0.1:5080';

/** The seed contract's test-only password (tests/seed/seed-contract.sql header), from Seed:PasswordFormat. */
export function passwordOf(username: string): string {
  const format: string = process.env.Seed__PasswordFormat ?? conformance.Seed.PasswordFormat;
  return format.replace('{0}', username);
}

/** migrator, for the fixtures' setup and their restore: from ConnectionStrings__migrator (CI), else the dev default. */
function migratorConfig(): pg.ClientConfig {
  const text: string = process.env.ConnectionStrings__migrator ?? conformance.ConnectionStrings.migrator;
  const parts = Object.fromEntries(
    text.split(';').filter(Boolean).map((p) => {
      const [k, ...v] = p.split('=');
      return [k!.trim().toLowerCase(), v.join('=').trim()];
    }),
  );
  return { host: parts.host, port: Number(parts.port ?? 5432), database: parts.database, user: parts.username, password: parts.password };
}

export async function asMigrator<T>(work: (client: pg.Client) => Promise<T>): Promise<T> {
  const client = new pg.Client(migratorConfig());
  await client.connect();
  try {
    return await work(client);
  } finally {
    await client.end();
  }
}

/** The API as the interface reaches it: same origin, X-Requested-With (Api's CSRF protection). */
export function api(): Promise<APIRequestContext> {
  return request.newContext({
    baseURL: BASE_URL,
    extraHTTPHeaders: { Origin: new URL(BASE_URL).origin, 'X-Requested-With': 'platform-web' },
  });
}

/**
 * An organization of the test's own, through the real path (POST /api/provision/tenants, registered in Development and
 * CI) — never the seed contract's. Its owner has exactly one membership, in it.
 */
export async function ownOrganization(label: string) {
  const tag = `${label}-${Date.now().toString(36)}`;
  const username = `e2e-${tag}`;
  const password = `${username}-password`;
  const context = await api();
  const response = await context.post('/api/provision/tenants', {
    data: { tenant_name: `E2E ${tag}`, full_name: username, email: `${username}@e2e.test`, username, password },
  });
  expect(response.status()).toBe(201);
  const body = await response.json();
  await context.dispose();
  return {
    username,
    password,
    email: `${username}@e2e.test`,
    name: `E2E ${tag}`,
    tenantId: body.tenant_id as string,
    membershipId: body.membership_id as string,
  };
}

/** The interface in English (the "lang" cookie), so the assertions read one language. */
export async function inEnglish(page: Page) {
  await page.context().addCookies([{ name: 'lang', value: 'en', url: BASE_URL }]);
}

export async function signIn(page: Page, username: string, password: string) {
  await page.goto('/login');
  await page.getByLabel('Username').fill(username);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
}

/**
 * An invitation from an organization's owner, through the real path (POST /api/members/invitations, the owner signed
 * in with the organization selected), and the link that carries it: the token in the fragment only.
 */
export async function invitation(owner: { username: string; password: string; tenantId: string }, email: string, mode = 'assigned') {
  const context = await api();
  try {
    expect((await context.post('/api/auth/login', { data: { username: owner.username, password: owner.password } })).status()).toBe(204);
    expect((await context.post(`/api/tenants/${owner.tenantId}/select`)).status()).toBe(204);
    const role = await asMigrator(async (c) =>
      (await c.query("SELECT id FROM roles WHERE tenant_id = $1 AND code = 'viewer'", [owner.tenantId])).rows[0].id as string,
    );
    const response = await context.post('/api/members/invitations', { data: { email, role_id: role, intended_scope_mode: mode } });
    expect(response.status()).toBe(201);
    const token = (await response.json()).token as string;
    return { token, link: `/app/invitations/accept#tenant=${owner.tenantId}&token=${encodeURIComponent(token)}` };
  } finally {
    await context.dispose();
  }
}

/**
 * A live recovery token for a user, and the link that carries it — minted as migrator rather than through
 * `POST /api/auth/password/forgot`, because that path answers 204 and puts the token in an e-mail: only its
 * SHA-256 reaches the database (`PasswordPaths`, Amendment 01 §2.2), so no test can read back the real one. The
 * row is the one `forgot` would have written, and the screen's half of the path — the fragment, the API call,
 * the refusals — is exercised for real.
 */
export async function recoveryToken(username: string, options: { expired?: boolean; used?: boolean } = {}) {
  const token = `e2e-token-${randomUUID()}`;
  const now = new Date();
  const expiresAt = new Date(now.getTime() + (options.expired ? -60_000 : 30 * 60_000));
  await asMigrator(async (c) => {
    const user = (await c.query('SELECT id FROM users WHERE username = $1', [username])).rows[0].id as string;
    await c.query(
      `INSERT INTO password_reset_tokens (id, user_id, token_hash, requested_at, expires_at, used_at)
       VALUES ($1, $2, $3, $4, $5, $6)`,
      [randomUUID(), user, createHash('sha256').update(token).digest('hex'), now, expiresAt, options.used ? now : null],
    );
  });
  return { token, link: `/app/password/reset#token=${encodeURIComponent(token)}` };
}

/** Memberships of a user (by username) in a tenant, as migrator. */
export function membershipsOf(username: string, tenantId: string): Promise<number> {
  return asMigrator(async (c) =>
    Number(
      (
        await c.query(
          'SELECT count(*) FROM memberships m JOIN users u ON u.id = m.user_id WHERE u.username = $1 AND m.tenant_id = $2',
          [username, tenantId],
        )
      ).rows[0].count,
    ),
  );
}

// ---- E-mail (group ب, OPEN_ITEMS 60). The platform really sends; the tests really read what arrived.

export const MAILPIT_URL = process.env.E2E_MAILPIT_URL ?? 'http://127.0.0.1:8025';

/** The settings key Api reads the SMTP credentials under in Development and CI (appsettings). */
const MAILPIT_CREDENTIAL_REF = 'local-mailpit';

/**
 * The platform's e-mail provider row, seeded **as migrator and only here**.
 *
 * `provider_accounts` has no INSERT grant for any application role — not even provisioner (§3.8) — so nothing but
 * a migration or this fixture can create it, and no migration seeds one: an installation names its own provider.
 * It is deliberately not in `tests/seed/seed-contract.sql`, which is the published contract another
 * implementation seeds; a local mail catcher is this harness's business, not the contract's.
 *
 * Its `credential_ref` names a settings key and never a secret (Amendment 01 §2.6): the row says *which* key, the
 * settings hold the value. Idempotent, because every spec that sends mail calls it.
 */
export async function emailProviderIsMailpit() {
  await asMigrator(async (c) => {
    await c.query(
      `INSERT INTO provider_accounts (id, tenant_id, channel, provider_code, sender_id, credential_ref, is_active, created_at)
       VALUES ($1, NULL, 'email', 'smtp', 'platform@e2e.test', $2, true, now())
       ON CONFLICT (tenant_id, channel) DO UPDATE
         SET provider_code = 'smtp', sender_id = 'platform@e2e.test', credential_ref = $2, is_active = true`,
      [randomUUID(), MAILPIT_CREDENTIAL_REF],
    );
  });
}

export interface CaughtMessage {
  ID: string;
  To: { Address: string }[];
  Subject: string;
}

/** Everything Mailpit is holding is thrown away, so a test reads only what its own run produced. */
export async function clearMailbox() {
  const response = await fetch(`${MAILPIT_URL}/api/v1/messages`, { method: 'DELETE' });
  if (!response.ok) throw new Error(`mailpit: could not clear the mailbox (${response.status})`);
}

/**
 * The message that arrived for an address, waited for rather than assumed: the send is deliberately off the
 * request's path (`INotificationDispatch`), so the API has already answered by the time it leaves.
 */
export async function waitForMessage(to: string, timeoutMs = 15_000): Promise<CaughtMessage> {
  const deadline = Date.now() + timeoutMs;
  let seen = 0;
  while (Date.now() < deadline) {
    const response = await fetch(`${MAILPIT_URL}/api/v1/messages?limit=200`);
    if (response.ok) {
      const body = (await response.json()) as { messages: CaughtMessage[] };
      seen = body.messages.length;
      const match = body.messages.find((m) => m.To.some((t) => t.Address.toLowerCase() === to.toLowerCase()));
      if (match) return match;
    }
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  throw new Error(`mailpit: no message for ${to} within ${timeoutMs} ms (${seen} other message(s) in the box)`);
}

/** A caught message's plain-text body. */
export async function messageBody(id: string): Promise<string> {
  const response = await fetch(`${MAILPIT_URL}/api/v1/message/${id}`);
  if (!response.ok) throw new Error(`mailpit: could not read message ${id} (${response.status})`);
  const body = (await response.json()) as { Text?: string; HTML?: string };
  return body.Text ?? body.HTML ?? '';
}

/**
 * The acceptance link out of an invitation e-mail — **the path the product actually uses**. An e2e that read the
 * token from the API response would skip delivery entirely, which is the part OPEN_ITEMS 60 exists for.
 */
export function acceptLinkIn(body: string): string {
  const match = body.match(/https?:\/\/\S*\/app\/invitations\/accept#tenant=[^\s]+/);
  if (!match) throw new Error(`no acceptance link in the message body:\n${body}`);
  return match[0];
}
