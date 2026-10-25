import { expect, test, type Page } from '@playwright/test';
import { api, asMigrator, inEnglish, ownOrganization, signIn } from './fixtures';

// The invitation's address (the project owner's item 35, group ج) [B], against the real Api serving the built
// interface. Two invitations, to "adnan" and "hasan", reached production through this form; and the API sent one
// link to two people when given "a@x.com, b@y.com". The server refuses both, the screen says so at the field, and
// no invitation row is written either way.

test.beforeEach(async ({ page }) => inEnglish(page));

async function enterAsOwner(page: Page, owner: { username: string; password: string }) {
  await signIn(page, owner.username, owner.password);
  await expect(page).toHaveURL(/\/app\/subscriptions$/);
}

async function openInviteForm(page: Page) {
  await page.getByRole('link', { name: 'Members' }).click();
  await page.getByRole('link', { name: 'Invite a member' }).click();
  await expect(page.getByLabel('E-mail address')).toBeVisible();
}

function invitationsTo(tenantId: string, email: string): Promise<number> {
  return asMigrator(async (c) =>
    Number((await c.query('SELECT count(*) FROM invitations WHERE tenant_id = $1 AND email = $2', [tenantId, email])).rows[0].count),
  );
}

test('1. "adnan": the screen says why at the field, and nothing is written', async ({ page }) => {
  const owner = await ownOrganization('addr-shape');
  await enterAsOwner(page, owner);
  await openInviteForm(page);

  await page.getByLabel('E-mail address').fill('adnan');
  await page.getByLabel('Role').selectOption({ label: 'Viewer' });
  await page.getByRole('button', { name: 'Send the invitation' }).click();

  await expect(page.getByText('Enter one e-mail address, such as name@example.com.')).toBeVisible();
  await expect(page.getByLabel('E-mail address')).toHaveAttribute('aria-invalid', 'true');
  await expect(page.getByRole('heading', { name: 'The invitation is on its way' })).toHaveCount(0);
  expect(await invitationsTo(owner.tenantId, 'adnan')).toBe(0);
});

test('2. one curl past the screen: two recipients are refused by the server, and nothing is written', async () => {
  const owner = await ownOrganization('addr-curl');
  const context = await api();
  try {
    expect((await context.post('/api/auth/login', { data: { username: owner.username, password: owner.password } })).status()).toBe(204);
    expect((await context.post(`/api/tenants/${owner.tenantId}/select`)).status()).toBe(204);
    const role = await asMigrator(async (c) =>
      (await c.query("SELECT id FROM roles WHERE tenant_id = $1 AND code = 'viewer'", [owner.tenantId])).rows[0].id as string,
    );
    const email = 'a@e2e.test, b@e2e.test';

    const response = await context.post('/api/members/invitations', { data: { email, role_id: role, intended_scope_mode: 'assigned' } });

    expect(response.status()).toBe(400);
    expect((await response.json()).error).toBe('invalid_value');
    expect(await invitationsTo(owner.tenantId, email)).toBe(0);
  } finally {
    await context.dispose();
  }
});

test('3. the address of someone already here: the screen says what that means to the person inviting', async ({ page }) => {
  const owner = await ownOrganization('addr-member');
  await enterAsOwner(page, owner);
  await openInviteForm(page);

  await page.getByLabel('E-mail address').fill(owner.email.toUpperCase());
  await page.getByLabel('Role').selectOption({ label: 'Viewer' });
  await page.getByRole('button', { name: 'Send the invitation' }).click();

  await expect(
    page.getByText(
      'This address already belongs to a member of this organization. A disabled member is enabled from the member list, not invited again.',
    ),
  ).toBeVisible();
  expect(await invitationsTo(owner.tenantId, owner.email.toUpperCase())).toBe(0);
});
