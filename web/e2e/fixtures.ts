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
  return { username, password, name: `E2E ${tag}`, tenantId: body.tenant_id as string, membershipId: body.membership_id as string };
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
