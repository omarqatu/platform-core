import { randomUUID } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';
import { api, asMigrator, inEnglish, ownOrganization, signIn } from './fixtures';

// The warning on a manager with assigned clients (the project owner's item 36, group ج) [B], against the real Api.
// The combination is legitimate — permission and scope are independent axes — so the screens ask, never refuse.

test.beforeEach(async ({ page }) => inEnglish(page));

async function enterAsOwner(page: Page, owner: { username: string; password: string }) {
  await signIn(page, owner.username, owner.password);
  await expect(page).toHaveURL(/\/app\/subscriptions$/);
}

test('1. inviting an Admin with assigned clients: the form warns, and the invitation is sent', async ({ page }) => {
  const owner = await ownOrganization('warn-invite');
  const email = `admin-${randomUUID().slice(0, 8)}@e2e.test`;
  await enterAsOwner(page, owner);
  await page.getByRole('link', { name: 'Members' }).click();
  await page.getByRole('link', { name: 'Invite a member' }).click();

  await page.getByLabel('E-mail address').fill(email);
  await page.getByLabel('Role').selectOption({ label: 'Admin' });
  await page.getByRole('radio', { name: 'Assigned clients' }).check();
  const warning = page.getByRole('status').filter({ hasText: 'This role manages members or scope' });
  await expect(warning).toBeVisible();
  await page.getByRole('button', { name: 'Send the invitation' }).click();

  await expect(page.getByRole('heading', { name: 'The invitation is on its way' })).toBeVisible();
  const written = await asMigrator(async (c) =>
    Number((await c.query("SELECT count(*) FROM invitations WHERE tenant_id = $1 AND email = $2 AND intended_scope_mode = 'assigned'", [owner.tenantId, email])).rows[0].count),
  );
  expect(written).toBe(1);
});

test("2. an Admin's scope page says it, on load, while they see assigned clients only", async ({ page }) => {
  const owner = await ownOrganization('warn-scope');
  const username = `nour-${randomUUID().slice(0, 8)}`;
  const email = `${username}@e2e.test`;
  const context = await api();
  try {
    expect((await context.post('/api/auth/login', { data: { username: owner.username, password: owner.password } })).status()).toBe(204);
    expect((await context.post(`/api/tenants/${owner.tenantId}/select`)).status()).toBe(204);
    const admin = await asMigrator(async (c) =>
      (await c.query("SELECT id FROM roles WHERE tenant_id = $1 AND code = 'admin'", [owner.tenantId])).rows[0].id as string,
    );
    const invited = await context.post('/api/members/invitations', { data: { email, role_id: admin, intended_scope_mode: 'assigned' } });
    expect(invited.status()).toBe(201);
    const token = (await invited.json()).token as string;
    const anonymous = await api();
    const accepted = await anonymous.post('/api/invitations/accept', {
      data: { tenant_id: owner.tenantId, token, email, full_name: 'Nour', username, password: `${username}-password` },
    });
    expect(accepted.status()).toBe(204);
    await anonymous.dispose();
  } finally {
    await context.dispose();
  }

  await enterAsOwner(page, owner);
  await page.getByRole('link', { name: 'Members' }).click();
  await page.getByRole('button', { name: 'Actions for Nour' }).click();
  await page.getByRole('menuitem', { name: 'Scope for Nour' }).click();

  const line = page.getByRole('status').filter({ hasText: 'Nour manages members or scope, but sees only their assigned clients.' });
  await expect(line).toBeVisible();
  await page.getByRole('radio', { name: 'All clients' }).check();
  await expect(line).toHaveCount(0);
});
