import { expect, test, type Page } from '@playwright/test';
import { inEnglish, invitation, membershipsOf, ownOrganization, signIn } from './fixtures';

// Accepting an invitation [B] (option C, decided by the project owner), against the real Api serving the built
// interface. Organizations and accounts of the tests' own, from the real bootstrap and invitation paths. The link
// carries the token in its fragment; the screen takes it into memory and out of the address at once.

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

/**
 * Signs in on the login screen already shown — never by loading /login again: the token lives in memory, and a page
 * load would lose it (as it would lose the "account created" notice).
 */
async function signInHere(page: Page, username: string, password: string) {
  await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
  await page.getByLabel('Username').fill(username);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
}

/**
 * On the selection, and the organization that invited not entered — not even a moment later (an automatic entry
 * starts once the list has loaded): the page settles, then the session itself is asked.
 */
async function expectSelectionWithNothingEntered(page: Page, invitedBy: string, ...organizations: string[]) {
  await expect(page.getByRole('heading', { name: 'Choose an organization' })).toBeVisible();
  for (const name of organizations) await expect(page.getByRole('button', { name })).toBeVisible();
  await page.waitForLoadState('networkidle');
  await expect(page).toHaveURL(/\/organizations$/);
  const me = await (await page.request.get('/api/me')).json();
  expect(me.active_tenant?.tenant_id).not.toBe(invitedBy);
}

test('1. a new account: the form, then sign in — the selection, nothing entered', async ({ page }) => {
  const host = await ownOrganization('acc-new');
  const username = `e2e-invitee-${Date.now().toString(36)}`;
  const email = `${username}@e2e.test`;
  const { link } = await invitation(host, email);

  await page.goto(link);
  await expect(page.getByRole('heading', { name: 'Accept the invitation' })).toBeVisible();
  await page.getByLabel('Full name').fill('Invitee');
  // The address as the invitee types it — another case, spaces around it — is the invitation's.
  await page.getByLabel('Email').fill(` ${email.toUpperCase()} `);
  await page.getByLabel('Username').fill(username);
  await page.getByLabel('Password').fill(`${username}-password`);
  await page.getByRole('button', { name: 'Create account and accept' }).click();

  await expect(page).toHaveURL(/\/login$/);
  await expect(page.getByRole('status')).toHaveText('Your account has been created and the invitation accepted. Sign in to continue.');
  expect(await membershipsOf(username, host.tenantId)).toBe(1);
  await signInHere(page, username, `${username}-password`);
  // One organization, and still not entered by itself: after an acceptance, the choice is the user's.
  await expectSelectionWithNothingEntered(page, host.tenantId, host.name);
});

test('2. an existing account, signed in: one button — a second organization, nothing entered', async ({ page }) => {
  const host = await ownOrganization('acc-second');
  const member = await ownOrganization('acc-second-home');
  const { link } = await invitation(host, member.email);
  await signIn(page, member.username, member.password);
  await expect(page).toHaveURL(/\/app\/subscriptions$/);

  await page.goto(link);
  await expect(page.getByText(member.username)).toBeVisible();
  await page.getByRole('button', { name: 'Accept the invitation' }).click();

  await expectSelectionWithNothingEntered(page, host.tenantId, host.name, member.name);
  expect(await membershipsOf(member.username, host.tenantId)).toBe(1);
});

test('3. an existing account, not signed in: refused as a new account — sign in, back, accept', async ({ page }) => {
  const host = await ownOrganization('acc-signin');
  const member = await ownOrganization('acc-signin-home');
  const { link } = await invitation(host, member.email);

  await page.goto(link);
  await page.getByLabel('Full name').fill('Someone');
  await page.getByLabel('Email').fill(member.email);
  await page.getByLabel('Username').fill(`${member.username}-again`);
  await page.getByLabel('Password').fill('another-password');
  await page.getByRole('button', { name: 'Create account and accept' }).click();
  await expect(page.getByRole('alert')).toHaveText('An account with this email already exists. Sign in, then accept the invitation.');

  await page.getByRole('link', { name: 'Sign in to accept' }).click();
  await expect(page.getByRole('status')).toHaveText('Sign in to accept the invitation.');
  await signInHere(page, member.username, member.password);
  // Straight back to the acceptance — the token from memory — and nothing entered on the way.
  await expect(page).toHaveURL(/\/app\/invitations\/accept$/);
  await page.getByRole('button', { name: 'Accept the invitation' }).click();

  await expectSelectionWithNothingEntered(page, host.tenantId, host.name, member.name);
  expect(await membershipsOf(member.username, host.tenantId)).toBe(1);
});

test('4. the invitation is addressed to someone else: refused, no membership', async ({ page }) => {
  const host = await ownOrganization('acc-mismatch');
  const member = await ownOrganization('acc-mismatch-home');
  const { link } = await invitation(host, `  ${member.email.replace('@', '-other@').toUpperCase()} `);
  await signIn(page, member.username, member.password);
  await expect(page).toHaveURL(/\/app\/subscriptions$/);

  await page.goto(link);
  await page.getByRole('button', { name: 'Accept the invitation' }).click();

  await expect(page.getByRole('alert')).toHaveText('This invitation was sent to a different email address.');
  await expect(page.getByRole('button', { name: 'Accept the invitation' })).toHaveCount(0);
  expect(await membershipsOf(member.username, host.tenantId)).toBe(0);
});

test('5. the token leaves the address at once: not in the URL, the history, a request or a referrer', async ({ page }) => {
  const host = await ownOrganization('acc-token');
  const member = await ownOrganization('acc-token-home');
  const { token, link } = await invitation(host, member.email);
  const seen = watchRequests(page);

  await page.goto(link);
  await expect(page.getByRole('heading', { name: 'Accept the invitation' })).toBeVisible();
  expect(page.url()).toMatch(/\/app\/invitations\/accept$/);
  // The entry the link opened was replaced: no entry of this tab's history holds the token.
  expect(await page.evaluate(() => window.location.href)).not.toContain(token);
  expect(await page.evaluate(() => history.length)).toBe(2); // about:blank, then the acceptance — replaced in place

  // Through sign-in and back, and the acceptance itself.
  await page.getByRole('link', { name: 'Sign in to accept' }).click();
  await signInHere(page, member.username, member.password);
  await expect(page).toHaveURL(/\/app\/invitations\/accept$/);
  await page.getByRole('button', { name: 'Accept the invitation' }).click();
  await expectSelectionWithNothingEntered(page, host.tenantId, host.name, member.name);
  await page.goBack();
  expect(page.url()).not.toContain(token);

  const encoded = encodeURIComponent(token);
  expect(seen.filter((url) => url.includes(token) || url.includes(encoded))).toEqual([]);
  // Opened again without its link (a reload loses memory): the screen says so, and asks for nothing.
  await page.goto('/app/invitations/accept');
  await expect(page.getByRole('alert')).toHaveText(
    'This invitation link is not valid: it may have expired or already been used. Ask for a new invitation.',
  );
});

test('6. a second link opened in the same tab: that invitation, not the first', async ({ page }) => {
  const first = await ownOrganization('acc-first');
  const second = await ownOrganization('acc-second-link');
  const member = await ownOrganization('acc-links-home');
  const one = await invitation(first, member.email);
  const two = await invitation(second, member.email);
  await signIn(page, member.username, member.password);
  await expect(page).toHaveURL(/\/app\/subscriptions$/);

  await page.goto(one.link);
  await expect(page.getByRole('button', { name: 'Accept the invitation' })).toBeVisible();
  // The same path, another fragment: no page load.
  await page.goto(two.link);
  expect(page.url()).toMatch(/\/app\/invitations\/accept$/);
  await page.getByRole('button', { name: 'Accept the invitation' }).click();

  await expectSelectionWithNothingEntered(page, second.tenantId, second.name, member.name);
  expect(await membershipsOf(member.username, second.tenantId)).toBe(1);
  expect(await membershipsOf(member.username, first.tenantId)).toBe(0);
});
