import { expect, test } from '@playwright/test';
import { asMigrator, inEnglish, ownOrganization, passwordOf, signIn } from './fixtures';

// The identity screens [B], against the real Api on the seed contract (PROOF_SPEC §7): omar holds two memberships
// (Al-Amin 'all', Maan 'assigned' with no assignment), sara one (Al-Amin). Organizations of the tests' own come
// from the real bootstrap path; what a fixture changes in the database is restored in `finally`.

const AL_AMIN_ROW = /^[ABCD] service \d+$/;

test.beforeEach(async ({ page }) => inEnglish(page));

test('1. a failed login: one generic message, whatever was wrong', async ({ page }) => {
  for (const [username, password] of [
    ['omar', 'not-the-password'], // a real user, a wrong password
    ['nobody-at-all', 'not-the-password'], // no such user
  ]) {
    await signIn(page, username!, password!);
    const alert = page.getByRole('alert');
    await expect(alert).toHaveText('The username or password is incorrect.');
    await expect(alert).toBeFocused();
    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByLabel('Password')).toHaveValue('');
  }
});

test('2. several memberships: choose, see the data, switch — no row of the first organization after the switch', async ({ page }) => {
  await signIn(page, 'omar', passwordOf('omar'));
  await expect(page.getByRole('heading', { name: 'Choose an organization' })).toBeVisible();
  await page.getByRole('button', { name: 'Al-Amin' }).click();

  await expect(page).toHaveURL(/\/app\/subscriptions$/);
  await expect(page.getByTestId('identity-organization')).toHaveText('Al-Amin');
  await expect(page.getByRole('cell', { name: 'A service 1', exact: true })).toBeVisible();

  // From the switch on, any Al-Amin row added to the page — for a single frame — is recorded.
  await page.evaluate((pattern) => {
    const seen: string[] = [];
    (window as unknown as { seen: string[] }).seen = seen;
    const re = new RegExp(pattern);
    new MutationObserver((records) => {
      for (const r of records)
        for (const node of r.addedNodes)
          for (const cell of node instanceof Element ? [node, ...node.querySelectorAll('td')] : [])
            if (cell.tagName === 'TD' && re.test(cell.textContent ?? '')) seen.push(cell.textContent ?? '');
    }).observe(document.body, { childList: true, subtree: true });
  }, AL_AMIN_ROW.source);

  await page.getByRole('button', { name: 'Switch organization' }).click();
  await expect(page.getByRole('heading', { name: 'Choose an organization' })).toBeVisible();
  await expect(page.getByRole('cell', { name: AL_AMIN_ROW })).toHaveCount(0);
  await page.getByRole('button', { name: 'Maan' }).click();

  await expect(page.getByTestId('identity-organization')).toHaveText('Maan');
  await expect(page.locator('.scope')).toHaveText(
    'You are shown only the subscriptions of the clients assigned to you, and they have none now.',
  );
  await expect(page.getByRole('cell', { name: AL_AMIN_ROW })).toHaveCount(0);
  expect(await page.evaluate(() => (window as unknown as { seen: string[] }).seen)).toEqual([]);
});

test('3. sign out: then any protected page leads to the login screen', async ({ page }) => {
  await signIn(page, 'omar', passwordOf('omar'));
  await page.getByRole('button', { name: 'Al-Amin' }).click();
  await expect(page.getByRole('cell', { name: 'A service 1', exact: true })).toBeVisible();

  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/login$/);
  await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();

  for (const path of ['/app/subscriptions', '/organizations', '/']) {
    await page.goto(path);
    await expect(page).toHaveURL(/\/login(\?next=.*)?$/);
    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
  }
  // The server agrees: the cookie is gone.
  expect((await page.request.get('/me')).status()).toBe(401);
});

test('4. one membership: entered straight away', async ({ page }) => {
  await signIn(page, 'sara', passwordOf('sara'));

  await expect(page).toHaveURL(/\/app\/subscriptions$/);
  await expect(page.getByTestId('identity-user')).toHaveText('sara');
  await expect(page.getByTestId('identity-organization')).toHaveText('Al-Amin');
  await expect(page.getByRole('cell', { name: 'A service 1', exact: true })).toBeVisible();

  // A reload keeps the session and the organization: the address is the interface's, never an API route.
  await page.reload();
  await expect(page).toHaveURL(/\/app\/subscriptions$/);
  await expect(page.getByTestId('identity-organization')).toHaveText('Al-Amin');
  await expect(page.getByRole('cell', { name: 'A service 1', exact: true })).toBeVisible();
});

test('5. a membership with no membership_scope: an explicit error, and no entry (T3.8)', async ({ page }) => {
  const own = await ownOrganization('scope-missing');
  const row = await asMigrator(async (db) => {
    const { rows } = await db.query('DELETE FROM membership_scope WHERE membership_id = $1 RETURNING *', [own.membershipId]);
    expect(rows).toHaveLength(1);
    return rows[0];
  });
  try {
    await signIn(page, own.username, own.password);

    const alert = page.getByRole('alert');
    await expect(alert).toHaveText('Your membership has no scope. Contact your administrator.');
    await expect(alert).toBeFocused();
    await expect(page).toHaveURL(/\/organizations/);
    await expect(page.getByTestId('identity-organization')).toHaveText('None chosen');
    await expect(page.getByRole('table')).toHaveCount(0);
  } finally {
    await asMigrator((db) =>
      db.query('INSERT INTO membership_scope (id, tenant_id, membership_id, scope_mode) VALUES ($1, $2, $3, $4)', [
        row.id,
        row.tenant_id,
        row.membership_id,
        row.scope_mode,
      ]),
    );
  }
});

test('6. a membership disabled during the session: /me names no organization, everything is dropped, back to the selection', async ({ page }) => {
  const own = await ownOrganization('disabled');
  try {
    await signIn(page, own.username, own.password);
    await expect(page.getByTestId('identity-organization')).toHaveText(own.name);
    await expect(page.locator('.scope')).toBeVisible();

    await asMigrator((db) => db.query("UPDATE memberships SET status = 'disabled' WHERE id = $1", [own.membershipId]));
    // The interface still believes in the organization; when the user comes back to the window, it asks the API.
    await page.evaluate(() => window.dispatchEvent(new Event('focus')));

    await expect(page).toHaveURL(/\/organizations/);
    await expect(page.getByTestId('identity-organization')).toHaveText('None chosen');
    await expect(page.getByRole('heading', { name: 'No active membership' })).toBeVisible();
    await expect(page.locator('.scope')).toHaveCount(0);
    await expect(page.getByRole('table')).toHaveCount(0);
  } finally {
    await asMigrator((db) => db.query("UPDATE memberships SET status = 'active' WHERE id = $1", [own.membershipId]));
  }
});
