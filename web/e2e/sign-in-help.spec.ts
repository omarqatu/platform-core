import { randomUUID } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';
import {
  BASE_URL,
  api,
  asMigrator,
  clearMailbox,
  emailProviderIsMailpit,
  inEnglish,
  invitation,
  messageBody,
  ownOrganization,
  signIn,
  waitForMessage,
} from './fixtures';

// Sign-in help (the project owner's item 37, group ج) [B], end to end against the real Api and Mailpit. The member
// list shows what each member signs in with, and a manager can send a member a plain message pointing them at the
// forgot-password page — no token, which only the member's own request mints. The act is audited.

test.beforeEach(async ({ page }) => inEnglish(page));

async function enterAsOwner(page: Page, owner: { username: string; password: string }) {
  await signIn(page, owner.username, owner.password);
  await expect(page).toHaveURL(/\/app\/subscriptions$/);
}

/** A member of the owner's organization: invited, and accepted as a new account, through the API. */
async function memberOf(owner: Awaited<ReturnType<typeof ownOrganization>>) {
  const username = `aya-${randomUUID().slice(0, 8)}`;
  const email = `${username}@e2e.test`;
  const { token } = await invitation(owner, email);
  const context = await api();
  try {
    const accepted = await context.post('/api/invitations/accept', {
      data: { tenant_id: owner.tenantId, token, email, full_name: 'Aya', username, password: `${username}-password` },
    });
    expect(accepted.status()).toBe(204);
  } finally {
    await context.dispose();
  }
  return { username, email };
}

test('1. the members table names what each member signs in with', async ({ page }) => {
  const owner = await ownOrganization('help-column');
  const aya = await memberOf(owner);
  await enterAsOwner(page, owner);
  await page.getByRole('link', { name: 'Members' }).click();

  await expect(page.getByRole('columnheader', { name: 'Username' })).toBeVisible();
  await expect(page.getByRole('row', { name: /Aya/ }).getByText(aya.username, { exact: true })).toBeVisible();
});

test('2. sign-in help: the member receives their username and the forgot-password page, and the act is audited', async ({ page }) => {
  await emailProviderIsMailpit();
  await clearMailbox();
  const owner = await ownOrganization('help-sent');
  const aya = await memberOf(owner);
  await enterAsOwner(page, owner);
  await page.getByRole('link', { name: 'Members' }).click();

  page.once('dialog', (dialog) => void dialog.accept());
  await page.getByRole('button', { name: 'Actions for Aya' }).click();
  await page.getByRole('menuitem', { name: 'Sign-in help for Aya' }).click();
  await expect(page.getByText('Sent', { exact: true })).toBeVisible();

  const body = await messageBody((await waitForMessage(aya.email)).ID);
  expect(body).toContain(aya.username);
  const link = body.match(/https?:\/\/\S*\/app\/password\/forgot\b\S*/)?.[0];
  expect(link).toBeTruthy();
  expect(body).not.toMatch(/token|#|\/password\/reset/i);

  const audited = await asMigrator(async (c) =>
    Number(
      (
        await c.query(
          `SELECT count(*) FROM audit_log a JOIN memberships m ON m.id = a.entity_id
             JOIN users u ON u.id = m.user_id
            WHERE a.tenant_id = $1 AND u.username = $2 AND a.action = 'sign_in_help_sent'`,
          [owner.tenantId, aya.username],
        )
      ).rows[0].count,
    ),
  );
  expect(audited).toBe(1);

  // The link is the page the member starts from themselves.
  await page.goto(link!.replace(new URL(BASE_URL).origin, ''));
  await expect(page.getByRole('heading', { name: 'Forgot your password' })).toBeVisible();
});
