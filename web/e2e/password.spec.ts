import { expect, test, type Page } from '@playwright/test';
import { inEnglish, ownOrganization, recoveryToken, signIn } from './fixtures';

// Password recovery and change [B] (Amendment 01 §5.1), against the real Api serving the built interface.
// Organizations and accounts of the tests' own, from the real bootstrap path — never the seed contract's, because
// these tests change a password. The recovery link carries its token in the fragment, as the invitation link does.

test.beforeEach(async ({ page }) => inEnglish(page));

/** Every address the page requests, and every Referer it sends — to show the token in none of them. */
function watchRequests(page: Page) {
  const seen: string[] = [];
  page.on('request', (request) => {
    seen.push(request.url());
    const referer = request.headers()['referer'];
    if (referer) seen.push(referer);
  });
  return seen;
}

async function ask(page: Page, email: string) {
  await page.goto('/app/password/forgot');
  await page.getByLabel('Email').fill(email);
  await page.getByRole('button', { name: 'Send the link' }).click();
  return page.getByRole('status');
}

test('1. forgot: an address with an account and one without get the same answer', async ({ page }) => {
  const owner = await ownOrganization('forgot');

  const known = await ask(page, owner.email);
  await expect(known).toBeVisible();
  const withAccount = (await known.textContent())?.trim();

  const unknown = await ask(page, `nobody-${Date.now().toString(36)}@e2e.test`);
  await expect(unknown).toBeVisible();
  const withoutAccount = (await unknown.textContent())?.trim();

  expect(withAccount).toBe(withoutAccount);
  // The screen stays where it is either way: nothing in the address says which it was.
  await expect(page).toHaveURL(/\/app\/password\/forgot$/);
  expect(withAccount).not.toContain(owner.email);
});

test('2. reset: the token sets the password, and never leaves the fragment', async ({ page }) => {
  const owner = await ownOrganization('reset');
  const { token, link } = await recoveryToken(owner.username);
  const seen = watchRequests(page);

  await page.goto(link);
  await expect(page.getByRole('heading', { name: 'Set a new password' })).toBeVisible();
  // The token left the address before the screen rendered.
  await expect(page).toHaveURL(/\/app\/password\/reset$/);

  const newPassword = `${owner.password}-reset`;
  await page.getByLabel('New password', { exact: true }).fill(newPassword);
  await page.getByLabel('Repeat the new password').fill(newPassword);
  await page.getByRole('button', { name: 'Set the password' }).click();

  // No cookie is issued on this path: it ends on the sign-in screen, which says so.
  await expect(page.getByText('Your password is set. Sign in with it.')).toBeVisible();
  expect(seen.some((url) => url.includes(token))).toBe(false);

  // The password is the new one, and only the new one.
  await page.getByLabel('Username').fill(owner.username);
  await page.getByLabel('Password').fill(owner.password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('alert')).toHaveText('The username or password is incorrect.');

  await signIn(page, owner.username, newPassword);
  await expect(page.getByTestId('identity-user')).toHaveText(owner.username);
});

test('3. reset: a used token and an expired one read the same, with no form', async ({ page }) => {
  const owner = await ownOrganization('reset-refused');
  const used = await recoveryToken(owner.username, { used: true });
  const expired = await recoveryToken(owner.username, { expired: true });

  const messages: (string | null)[] = [];
  for (const { link } of [used, expired]) {
    await page.goto(link);
    await page.getByLabel('New password', { exact: true }).fill('a new password');
    await page.getByLabel('Repeat the new password').fill('a new password');
    await page.getByRole('button', { name: 'Set the password' }).click();
    const alert = page.getByRole('alert');
    await expect(alert).toBeVisible();
    messages.push(await alert.textContent());
    await expect(page.getByLabel('New password', { exact: true })).toHaveCount(0);
  }
  expect(messages[0]).toBe(messages[1]);

  // Reloading the screen loses the token with the memory that held it, and says the same thing.
  await page.goto('/app/password/reset');
  await expect(page.getByRole('alert')).toHaveText(messages[0] ?? '');
});

test('4. change: this session survives; every other session of the same user ends', async ({ page, browser }) => {
  const owner = await ownOrganization('change');

  // A second browser, signed in as the same user: it is the session that must end.
  const elsewhere = await browser.newContext();
  const other = await elsewhere.newPage();
  await inEnglish(other);
  await signIn(other, owner.username, owner.password);
  await expect(other.getByTestId('identity-user')).toHaveText(owner.username);

  await signIn(page, owner.username, owner.password);
  await page.getByRole('link', { name: 'Change password' }).click();
  await expect(page.getByRole('heading', { name: 'Change your password' })).toBeVisible();

  // A wrong current password is answered, and signs nobody out.
  const newPassword = `${owner.password}-changed`;
  await page.getByLabel('Current password').fill('not the current one');
  await page.getByLabel('New password', { exact: true }).fill(newPassword);
  await page.getByLabel('Repeat the new password').fill(newPassword);
  await page.getByRole('button', { name: 'Change the password' }).click();
  await expect(page.getByRole('alert')).toHaveText('The current password is incorrect.');
  await expect(page.getByTestId('identity-user')).toHaveText(owner.username);

  await page.getByLabel('Current password').fill(owner.password);
  await page.getByLabel('New password', { exact: true }).fill(newPassword);
  await page.getByLabel('Repeat the new password').fill(newPassword);
  await page.getByRole('button', { name: 'Change the password' }).click();
  await expect(page.getByRole('status')).toContainText('You stay signed in here');

  // This session: still in, and still in after a reload (the cookie was re-issued with the new stamp).
  await page.reload();
  await expect(page.getByTestId('identity-user')).toHaveText(owner.username);

  // The other one: ended on its next request.
  await other.reload();
  await expect(other.getByText('Your session has ended. Sign in again.')).toBeVisible();
  await elsewhere.close();
});
