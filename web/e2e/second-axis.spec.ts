import { expect, test, type Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import {
  acceptLinkIn,
  asMigrator,
  BASE_URL,
  clearMailbox,
  emailProviderIsMailpit,
  inEnglish,
  messageBody,
  ownOrganization,
  signIn,
  waitForMessage,
} from './fixtures';

/**
 * The second axis, end to end, through screens only (group ب, OPEN_ITEMS 100 and 60).
 *
 * This is the deliverable the group exists for: an employee is invited by e-mail, accepts, is put in mode
 * `assigned`, is given two of four clients, and sees those two and nothing of the others — not their rows, not
 * their subscriptions, not their number. Until this spec, the axis was proven in the database (T5, T8) and could
 * not be shown to anyone.
 *
 * **It starts from the e-mail, not from the API's response.** `POST /members/invitations` also returns the token,
 * and a test reading it there would skip delivery entirely — which is the part OPEN_ITEMS 60 exists for. The link
 * is parsed out of the message Mailpit caught.
 */

// Serial, and longer than the 30 s default: test 1 provisions an organization, four clients and their
// subscriptions, sends and waits for a real e-mail, accepts it in a second browser, sets a scope and two
// assignments, and then signs in as the new member. Raised here and not in playwright.config.ts, which every
// other spec shares.
test.describe.configure({ mode: 'serial', timeout: 240_000 });

test.beforeEach(async ({ page }) => inEnglish(page));

/** Four clients for an organization, created as migrator — the module has no client-creation endpoint for a test. */
async function clientsFor(tenantId: string, names: string[], disabled: string[] = []) {
  const ids: Record<string, string> = {};
  await asMigrator(async (c) => {
    for (const name of names) {
      const id = randomUUID();
      ids[name] = id;
      await c.query(
        `INSERT INTO clients (id, tenant_id, scope_ref_id, name, created_at, email, phone_e164, telegram_chat_id, is_active)
         VALUES ($1, $2, $3, $4, now(), NULL, NULL, NULL, $5)`,
        [id, tenantId, id, name, !disabled.includes(name)],
      );
      await c.query(
        `INSERT INTO subscriptions (id, tenant_id, scope_ref_id, service_name, ends_on, created_at, reminder_type,
                                    category_id, billing_cycle, auto_renew, status, renewed_from_id)
         VALUES ($1, $2, $3, $4, DATE '2027-06-01', now(), 'subscription',
                 (SELECT id FROM categories WHERE code = 'other'), 'yearly', false, 'active', NULL)`,
        [randomUUID(), tenantId, id, `${name} service`],
      );
    }
  });
  return ids;
}

/**
 * The owner, signed in with their organization entered. An owner from `ownOrganization` has exactly one
 * membership, so the interface enters it without a selection screen (as every other spec relies on).
 */
async function enterAsOwner(page: Page, owner: { username: string; password: string }) {
  await signIn(page, owner.username, owner.password);
  await expect(page).toHaveURL(/\/app\/subscriptions$/);
}

test('1. Ahmad is invited by e-mail, accepts, and sees the two clients assigned to him and no others', async ({ page }) => {
  await emailProviderIsMailpit();
  await clearMailbox();
  const owner = await ownOrganization('axis');
  await clientsFor(owner.tenantId, ['Alpha', 'Beta', 'Gamma', 'Delta']);
  const ahmad = `ahmad-${randomUUID().slice(0, 8)}@e2e.test`;
  const ahmadUser = `ahmad-${randomUUID().slice(0, 8)}`;
  const ahmadPassword = `${ahmadUser}-password`;

  // ---- Omar invites Ahmad, through the screen.
  await enterAsOwner(page, owner);
  await page.getByRole('link', { name: 'Members' }).click();
  await expect(page.getByRole('heading', { name: 'Members' })).toBeVisible();
  await page.getByRole('link', { name: 'Invite a member' }).click();
  await page.getByLabel('E-mail address').fill(ahmad);
  await page.getByLabel('Role').selectOption({ label: 'Operator' });
  await page.getByRole('button', { name: 'Send the invitation' }).click();
  await expect(page.getByRole('heading', { name: 'The invitation is on its way' })).toBeVisible();

  // ---- The e-mail arrives, and the link comes out of it — not out of the API's response.
  const message = await waitForMessage(ahmad);
  const link = acceptLinkIn(await messageBody(message.ID));
  expect(link).toContain('#tenant=');

  // ---- Ahmad accepts it, in a browser of his own, and sets his password.
  const his = await page.context().browser()!.newContext({ baseURL: BASE_URL });
  const ahmadPage = await his.newPage();
  await inEnglish(ahmadPage);
  await ahmadPage.goto(link.replace(BASE_URL, '').startsWith('/') ? link : link);
  await expect(ahmadPage.getByRole('heading', { name: 'Accept the invitation' })).toBeVisible();
  await ahmadPage.getByLabel('Full name').fill('Ahmad');
  await ahmadPage.getByLabel('Email').fill(ahmad);
  await ahmadPage.getByLabel('Username').fill(ahmadUser);
  await ahmadPage.getByLabel('Password').fill(ahmadPassword);
  await ahmadPage.getByRole('button', { name: 'Create account and accept' }).click();
  await expect(ahmadPage.getByRole('heading', { name: 'Sign in' })).toBeVisible();

  // ---- Omar sets his scope to 'assigned' and gives him two of the four.
  await page.getByRole('button', { name: 'Back to the members' }).click();
  await page.reload();
  const row = page.locator('tr', { hasText: 'Ahmad' });
  await row.getByRole('button', { name: 'Actions for Ahmad' }).click();
  await page.getByRole('menuitem', { name: 'Scope for Ahmad' }).click();
  await expect(page.getByRole('heading', { name: 'What Ahmad sees' })).toBeVisible();
  await expect(page.getByRole('radio', { name: 'Assigned clients' })).toBeChecked();
  for (const client of ['Alpha', 'Beta']) {
    await page.getByRole('switch', { name: `Assign ${client}` }).click();
    // data-assigned comes from what the server returned on the reload, not from the checkbox: a checkbox's own
    // `checked` flips in the DOM the instant it is clicked, so asserting it would pass even if the write never
    // landed — a verifier that verifies nothing (OPEN_ITEMS 83).
    await expect(page.locator('li', { hasText: client })).toHaveAttribute('data-assigned', 'true');
  }

  // ---- Ahmad signs in and sees exactly those two, and nothing of the others.
  // Signing in after accepting lands on the selection, not inside the organization — the behaviour
  // invitations.spec.ts pins: the acceptance path issues no cookie, so this is a first sign-in like any other.
  await signIn(ahmadPage, ahmadUser, ahmadPassword);
  await expect(ahmadPage.getByRole('heading', { name: 'Choose an organization' })).toBeVisible();
  await ahmadPage.getByRole('button', { name: owner.name }).click();
  await expect(ahmadPage).toHaveURL(/\/app\//);
  await ahmadPage.goto(`${BASE_URL}/app/clients`);
  await expect(ahmadPage.locator('tbody tr')).toHaveCount(2);
  await expect(ahmadPage.locator('tbody')).toContainText('Alpha');
  await expect(ahmadPage.locator('tbody')).toContainText('Beta');
  await expect(ahmadPage.locator('tbody')).not.toContainText('Gamma');
  await expect(ahmadPage.locator('tbody')).not.toContainText('Delta');

  // PROOF_SPEC T8.2: the declaration says what he is shown and names no total, in the text or in the markup.
  const scope = ahmadPage.locator('.scope');
  await expect(scope).toHaveAttribute('data-scope-mode', 'assigned');
  await expect(scope).toHaveText('You are shown only the clients assigned to you: 2 clients.');
  expect(await ahmadPage.content()).not.toContain('data-total');

  // And their subscriptions, and only theirs.
  await ahmadPage.goto(`${BASE_URL}/app/subscriptions?status=all`);
  await expect(ahmadPage.locator('tbody')).toContainText('Alpha service');
  await expect(ahmadPage.locator('tbody')).toContainText('Beta service');
  await expect(ahmadPage.locator('tbody')).not.toContainText('Gamma service');
  await expect(ahmadPage.locator('tbody')).not.toContainText('Delta service');
  await expect(ahmadPage.locator('.scope')).toHaveAttribute('data-scope-mode', 'assigned');

  // The member screens are not his: he manages neither members nor scope, so the sidebar does not offer them
  // and the address refuses. That refusal is the whole of T8.2 for member data — the database would show him
  // every membership of the organization (OPEN_ITEMS 103).
  await expect(ahmadPage.getByRole('link', { name: 'Members' })).toHaveCount(0);
  await ahmadPage.goto(`${BASE_URL}/app/members`);
  await expect(ahmadPage.getByRole('heading', { name: 'Not permitted' })).toBeVisible();
  expect(await ahmadPage.content()).not.toContain('data-membership');

  await his.close();
});

/**
 * The project owner's addition 5, second case. A withdrawn assignment takes effect on the next request, with no
 * re-login: Test 20 proves that at the API, and this is what the person actually sees.
 *
 * The first case — a direct address to a client that is not his — is pinned where it belongs, in Conformance
 * (`ExistenceLeakTests`): a hidden client and an unknown uuid answer byte-identically, so the answer reveals no
 * existence. Here the claim is only what Ahmad sees when he follows it.
 */
test('2. an assignment withdrawn while Ahmad has the client open takes effect on his next request', async ({ page }) => {
  await emailProviderIsMailpit();
  const owner = await ownOrganization('axis-stale');
  const ids = await clientsFor(owner.tenantId, ['Kept', 'Taken']);
  const ahmadUser = `stale-${randomUUID().slice(0, 8)}`;
  const ahmadPassword = `${ahmadUser}-password`;
  const ahmad = `${ahmadUser}@e2e.test`;

  // Invited and accepted through the API, since the screens' own path is test 1's subject.
  const context = await page.context().browser()!.newContext({ baseURL: BASE_URL });
  const apiPage = await context.newPage();
  await inEnglish(apiPage);
  await clearMailbox();
  await enterAsOwner(page, owner);
  await page.getByRole('link', { name: 'Members' }).click();
  await page.getByRole('link', { name: 'Invite a member' }).click();
  await page.getByLabel('E-mail address').fill(ahmad);
  await page.getByLabel('Role').selectOption({ label: 'Operator' });
  await page.getByRole('button', { name: 'Send the invitation' }).click();
  const link = acceptLinkIn(await messageBody((await waitForMessage(ahmad)).ID));
  await apiPage.goto(link);
  await apiPage.getByLabel('Full name').fill('Stale');
  await apiPage.getByLabel('Email').fill(ahmad);
  await apiPage.getByLabel('Username').fill(ahmadUser);
  await apiPage.getByLabel('Password').fill(ahmadPassword);
  await apiPage.getByRole('button', { name: 'Create account and accept' }).click();
  await expect(apiPage.getByRole('heading', { name: 'Sign in' })).toBeVisible();

  // Both clients assigned, as migrator: this test's subject is the withdrawal, not the assigning.
  await asMigrator(async (c) => {
    const membership = (
      await c.query('SELECT m.id FROM memberships m JOIN users u ON u.id = m.user_id WHERE u.username = $1', [ahmadUser])
    ).rows[0].id as string;
    for (const name of ['Kept', 'Taken'])
      await c.query(
        `INSERT INTO scope_assignments (id, tenant_id, membership_id, scope_ref_id, assignment_role, active, reason)
         VALUES ($1, $2, $3, $4, 'contributor', true, 'e2e')`,
        [randomUUID(), owner.tenantId, membership, ids[name]],
      );
  });

  await signIn(apiPage, ahmadUser, ahmadPassword);
  await expect(apiPage.getByRole('heading', { name: 'Choose an organization' })).toBeVisible();
  await apiPage.getByRole('button', { name: owner.name }).click();
  await expect(apiPage).toHaveURL(/\/app\//);
  await apiPage.goto(`${BASE_URL}/app/clients/${ids['Taken']}/edit`);
  await expect(apiPage.getByLabel(/^Name/)).toHaveValue('Taken');

  // Omar withdraws it while that page is open.
  await page.goto(`${BASE_URL}/app/members`);
  await page.locator('tr', { hasText: 'Stale' }).getByRole('button', { name: 'Actions for Stale' }).click();
  await page.getByRole('menuitem', { name: 'Scope for Stale' }).click();
  await page.getByRole('switch', { name: 'Assign Taken' }).click();
  await expect(page.locator('[data-client] [data-note="withdrawn"]')).toHaveCount(1);

  // Ahmad's next request — no re-login, no new session. The page he is holding is already stale.
  await apiPage.reload();
  await expect(apiPage.getByLabel(/^Name/)).toHaveCount(0);
  await expect(apiPage.getByRole('heading', { name: 'Not permitted' })).toBeVisible();
  await apiPage.goto(`${BASE_URL}/app/clients`);
  await expect(apiPage.locator('tbody tr')).toHaveCount(1);
  await expect(apiPage.locator('tbody')).toContainText('Kept');
  await expect(apiPage.locator('tbody')).not.toContainText('Taken');

  await context.close();
});

/**
 * The project owner's item 32-c, decided explicitly rather than by silence: a disabled client stays assignable
 * and stays visible to whoever holds it. Asserted at the screen, where the decision is visible.
 */
test('3. a disabled client appears in the assignment list, with its badge, and can be assigned', async ({ page }) => {
  const owner = await ownOrganization('axis-disabled');
  await clientsFor(owner.tenantId, ['Live Co', 'Dormant Co'], ['Dormant Co']);
  await enterAsOwner(page, owner);
  await page.goto(`${BASE_URL}/app/members`);

  // The owner's own row offers nothing, so a second member is needed to reach the screen.
  const second = `dis-${randomUUID().slice(0, 8)}`;
  await emailProviderIsMailpit();
  await clearMailbox();
  await page.getByRole('link', { name: 'Invite a member' }).click();
  await page.getByLabel('E-mail address').fill(`${second}@e2e.test`);
  await page.getByLabel('Role').selectOption({ label: 'Operator' });
  await page.getByRole('button', { name: 'Send the invitation' }).click();
  const link = acceptLinkIn(await messageBody((await waitForMessage(`${second}@e2e.test`)).ID));
  const theirs = await page.context().browser()!.newContext({ baseURL: BASE_URL });
  const theirPage = await theirs.newPage();
  await inEnglish(theirPage);
  await theirPage.goto(link);
  await theirPage.getByLabel('Full name').fill('Second');
  await theirPage.getByLabel('Email').fill(`${second}@e2e.test`);
  await theirPage.getByLabel('Username').fill(second);
  await theirPage.getByLabel('Password').fill(`${second}-password`);
  await theirPage.getByRole('button', { name: 'Create account and accept' }).click();
  await expect(theirPage.getByRole('heading', { name: 'Sign in' })).toBeVisible();
  await theirs.close();

  await page.goto(`${BASE_URL}/app/members`);
  await page.locator('tr', { hasText: 'Second' }).getByRole('button', { name: 'Actions for Second' }).click();
  await page.getByRole('menuitem', { name: 'Scope for Second' }).click();

  await expect(page.getByRole('switch', { name: 'Assign Dormant Co' })).toBeEnabled();
  await expect(page.locator('[data-client] [data-client-status="disabled"]')).toHaveCount(1);
  await page.getByRole('switch', { name: 'Assign Dormant Co' }).click();
  await expect(page.getByRole('switch', { name: 'Assign Dormant Co' })).toBeChecked();
});

/**
 * Delivery alone, for the recovery path — **deliberately the narrowest claim this file makes** (the project
 * owner's decision, 2026-10-21). It asserts that a message reaches the right address and nothing about its body,
 * its language or its link, because the recovery e-mail is English-only and hardcoded (OPEN_ITEMS 102) and its
 * link template still defaults to a bare path (OPEN_ITEMS 105). A test pinning either today would become a
 * declared exception the group that fixes them has to amend. This one catches a wiring regression and leaves the
 * fix completely unconstrained — whoever closes 102 and 105 should find it here and widen it then.
 */
test('4. the recovery e-mail reaches the address (delivery only — 102 and 105 are open)', async ({ page }) => {
  await emailProviderIsMailpit();
  await clearMailbox();
  const owner = await ownOrganization('axis-forgot');

  await page.goto(`${BASE_URL}/app/password/forgot`);
  await page.getByLabel('Email').fill(owner.email);
  await page.getByRole('button', { name: 'Send the link' }).click();

  const message = await waitForMessage(owner.email);
  expect(message.To[0]!.Address.toLowerCase()).toBe(owner.email.toLowerCase());
});
