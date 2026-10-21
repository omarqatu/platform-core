import { expect, test, type Page } from '@playwright/test';
import { asMigrator, BASE_URL, inEnglish, ownOrganization, signIn } from './fixtures';

// The client screens and the subscription lifecycle (PLATFORM_CORE Amendment 01 §2.8, §2.9; Amendment 02 §5.1) [B],
// against the real Api: an organization of the test's own, its owner entering it directly. Everything here is done
// through the interface — this is the stage that first gives the module a client screen, so the manual entry of M3
// goes through exactly this path. Nothing of the seed contract is written.

const clients = (tenantId: string) =>
  asMigrator(async (db) => {
    const { rows } = await db.query(
      'SELECT name, email, phone_e164, telegram_chat_id, is_active FROM clients WHERE tenant_id = $1 ORDER BY name',
      [tenantId],
    );
    return rows;
  });

const subscriptions = (tenantId: string) =>
  asMigrator(async (db) => {
    const { rows } = await db.query(
      `SELECT s.service_name, s.ends_on::text, s.status, (s.renewed_from_id IS NOT NULL) AS renews, c.name AS client
       FROM subscriptions s JOIN clients c ON c.id = s.scope_ref_id
       WHERE s.tenant_id = $1 ORDER BY s.ends_on`,
      [tenantId],
    );
    return rows;
  });

async function enter(page: Page, owner: { username: string; password: string }) {
  await signIn(page, owner.username, owner.password);
  await expect(page).toHaveURL(/\/app\/subscriptions$/);
  await page.getByRole('link', { name: 'Clients' }).click();
  await expect(page).toHaveURL(/\/app\/clients$/);
}

test('1. a client is created with its channels, edited, disabled and enabled again, in English', async ({ page }) => {
  await inEnglish(page);
  const owner = await ownOrganization('client-form');
  await enter(page, owner);

  await expect(page.locator('.scope')).toHaveText("You are shown all of the organization's clients: 0 clients.");

  await page.getByRole('link', { name: 'New client' }).click();
  await expect(page.getByRole('heading', { name: 'New client' })).toBeVisible();

  // Sent with a name missing and a phone that is not E.164: each field says so, and nothing is written.
  await page.getByLabel(/^Phone/).fill('0791234567');
  await page.getByRole('button', { name: 'Create' }).click();
  await expect(page.getByRole('alert')).toHaveText('Check the fields marked below.');
  await expect(page.getByLabel(/^Name/)).toHaveAttribute('aria-invalid', 'true');
  await expect(page.getByLabel(/^Phone/)).toHaveAttribute('aria-invalid', 'true');
  expect(await clients(owner.tenantId)).toEqual([]);

  await page.getByLabel(/^Name/).fill('شركة النور');
  await page.getByLabel(/^E-mail/).fill('nour@example.com');
  await page.getByLabel(/^Phone/).fill('+962791234567');
  await page.getByLabel(/^Telegram chat id/).fill('-1001234567890');
  await page.getByRole('button', { name: 'Create' }).click();

  await expect(page).toHaveURL(/\/app\/clients$/);
  expect(await clients(owner.tenantId)).toEqual([
    {
      name: 'شركة النور',
      email: 'nour@example.com',
      phone_e164: '+962791234567',
      telegram_chat_id: '-1001234567890',
      is_active: true,
    },
  ]);

  // Editing: the stored channels come back into the fields, and clearing one stores null, not an empty string.
  await page.getByRole('button', { name: 'Actions for شركة النور' }).click();
  await page.getByRole('menuitem', { name: 'Edit شركة النور' }).click();
  await expect(page.getByLabel(/^E-mail/)).toHaveValue('nour@example.com');
  await page.getByLabel(/^Telegram chat id/).fill('');
  await page.getByRole('button', { name: 'Save' }).click();

  await expect(page).toHaveURL(/\/app\/clients$/);
  expect(await clients(owner.tenantId)).toEqual([
    expect.objectContaining({ telegram_chat_id: null, email: 'nour@example.com', is_active: true }),
  ]);

  // Disabling asks first, and says what it does not change.
  page.once('dialog', (dialog) => {
    expect(dialog.message()).toBe(
      'Stop the reminders for شركة النور? Its subscriptions are not changed, and you can enable it again.',
    );
    return dialog.accept();
  });
  await page.getByRole('button', { name: 'Actions for شركة النور' }).click();
  await page.getByRole('menuitem', { name: 'Disable شركة النور' }).click();

  await expect(page.getByText('Disabled')).toBeVisible();
  expect(await clients(owner.tenantId)).toEqual([expect.objectContaining({ is_active: false })]);

  // The disabled client is still listed, with its way back.
  await page.getByRole('button', { name: 'Actions for شركة النور' }).click();
  await page.getByRole('menuitem', { name: 'Enable شركة النور' }).click();
  await expect(page.getByText('Active')).toBeVisible();
  expect(await clients(owner.tenantId)).toEqual([expect.objectContaining({ is_active: true })]);
});

test('2. a subscription is renewed into a new row and cancelled, and neither touches the client', async ({ page }) => {
  await inEnglish(page);
  const owner = await ownOrganization('client-lifecycle');
  await enter(page, owner);

  await page.getByRole('link', { name: 'New client' }).click();
  await page.getByLabel(/^Name/).fill('Nour Co');
  await page.getByRole('button', { name: 'Create' }).click();
  await expect(page).toHaveURL(/\/app\/clients$/);

  await page.getByRole('link', { name: 'Subscriptions' }).click();
  await page.getByRole('link', { name: 'New subscription' }).click();
  await page.getByLabel(/^Client/).selectOption({ label: 'Nour Co' });
  await page.getByLabel(/^Service/).fill('VPS');
  await page.getByLabel(/^Ends on/).fill('2027-03-31');
  await page.getByLabel(/^Category/).selectOption({ label: 'Hosting' });
  await page.getByLabel(/^Billing cycle/).selectOption({ label: 'Yearly' });
  await page.getByRole('button', { name: 'Create subscription' }).click();
  await expect(page).toHaveURL(/\/app\/subscriptions$/);

  // Renewing: one field, and the new row keeps the client and the service of the one it renews.
  await page.getByRole('button', { name: 'Actions for VPS' }).click();
  await page.getByRole('menuitem', { name: 'Renew VPS' }).click();
  await expect(page.getByRole('heading', { name: 'Renew subscription' })).toBeVisible();
  await expect(page.getByLabel(/^Service/)).toHaveText('VPS');
  // There is no client to choose: a renewal never names one.
  await expect(page.getByLabel(/^Client/)).toHaveCount(0);

  // A date that is not later than the one it renews is refused before it is sent.
  await page.getByLabel(/^New end date/).fill('2027-03-31');
  await page.getByRole('button', { name: 'Renew' }).click();
  await expect(page.getByRole('alert')).toHaveText('Some fields need attention.');
  expect((await subscriptions(owner.tenantId)).length).toBe(1);

  await page.getByLabel(/^New end date/).fill('٢٠٢٨-٠٣-٣١'); // typed on an Arabic keyboard
  await expect(page.getByLabel(/^New end date/)).toHaveValue('2028-03-31');
  await page.getByRole('button', { name: 'Renew' }).click();

  await expect(page).toHaveURL(/\/app\/subscriptions$/);
  expect(await subscriptions(owner.tenantId)).toEqual([
    { service_name: 'VPS', ends_on: '2027-03-31', status: 'renewed', renews: false, client: 'Nour Co' },
    { service_name: 'VPS', ends_on: '2028-03-31', status: 'active', renews: true, client: 'Nour Co' },
  ]);
  // The renewed row keeps its place in the list, marked, and offers nothing to do to it.
  await expect(page.getByText('Renewed', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Actions for VPS' })).toHaveCount(1);
  await expect(page.getByText('Renewed, no actions')).toBeVisible();

  // Cancelling asks first, and keeps the row.
  page.once('dialog', (dialog) => {
    expect(dialog.message()).toBe(
      'Cancel VPS? It stays in the list with its history, and it cannot be edited or renewed afterwards.',
    );
    return dialog.accept();
  });
  await page.getByRole('button', { name: 'Actions for VPS' }).click();
  await page.getByRole('menuitem', { name: 'Cancel VPS' }).click();

  await expect(page.getByText('Cancelled', { exact: true })).toBeVisible();
  expect(await subscriptions(owner.tenantId)).toEqual([
    expect.objectContaining({ ends_on: '2027-03-31', status: 'renewed' }),
    expect.objectContaining({ ends_on: '2028-03-31', status: 'cancelled' }),
  ]);
  // Nothing is left to act on, and nothing was deleted.
  await expect(page.getByRole('button', { name: /^Actions for VPS/ })).toHaveCount(0);
  await expect(page.locator('tbody tr')).toHaveCount(2);
  // And the client is untouched by either act.
  expect(await clients(owner.tenantId)).toEqual([expect.objectContaining({ name: 'Nour Co', is_active: true })]);
});

test('3. the client screens in Arabic: right-to-left, glossary terms, Western digits', async ({ page }) => {
  await inEnglish(page); // the sign-in helper reads the English labels
  const owner = await ownOrganization('client-form-ar');
  await signIn(page, owner.username, owner.password);
  await expect(page).toHaveURL(/\/app\/subscriptions$/); // let the sign-in land before the language changes
  await page.context().addCookies([{ name: 'lang', value: 'ar', url: BASE_URL }]);
  await page.reload();

  await page.getByRole('link', { name: 'العملاء' }).click();
  await expect(page).toHaveURL(/\/app\/clients$/);
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');

  await page.getByRole('link', { name: 'عميل جديد' }).click();
  await expect(page.getByRole('heading', { name: 'عميل جديد' })).toBeVisible();
  await page.getByLabel(/^الاسم/).fill('شركة النور');
  await page.getByLabel(/^الهاتف/).fill('+٩٦٢٧٩١٢٣٤٥٦٧'); // Arabic-Indic digits, converted as they are typed
  await expect(page.getByLabel(/^الهاتف/)).toHaveValue('+962791234567');
  await page.getByRole('button', { name: 'إنشاء' }).click();

  await expect(page).toHaveURL(/\/app\/clients$/);
  const main = (await page.locator('main').textContent())!;
  expect(main).not.toMatch(/[٠-٩۰-۹]/); // Western digits in Arabic
  expect(main).not.toContain('مستأجر'); // the glossary's term, never the platform's
  expect(await clients(owner.tenantId)).toEqual([
    expect.objectContaining({ name: 'شركة النور', phone_e164: '+962791234567', is_active: true }),
  ]);
});
