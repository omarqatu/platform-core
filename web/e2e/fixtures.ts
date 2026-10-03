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
