import { randomUUID } from 'node:crypto';
import { expect, test } from '@playwright/test';
import { api, asMigrator, BASE_URL, ownOrganization, signIn } from './fixtures';

// The subscription list's filter and its dates [B], against the real Api serving the built interface — the project
// owner's items 4 and 5 (PR ب).
//   * Item 4: the list shows one lifecycle status at a time, "Active" by default, the choice in the address; the
//     scope declaration says what is shown, counted after the filter.
//   * Item 5: every date is yyyy-MM-dd, left-to-right, on one line, Western digits, in both languages.
// The organization is the test's own; its client is the fixture's, as migrator (as row-actions.spec.ts), and its
// subscriptions go through the real API, renewal and cancellation included. Nothing of the seed is written.

/** One client; one subscription active, one renewed (with its active renewal), one cancelled. */
async function organizationWithHistory() {
  const owner = await ownOrganization('list-filter');
  const client = randomUUID();
  await asMigrator((db) =>
    db.query(
      'INSERT INTO clients (id, tenant_id, scope_ref_id, name, created_at, email, phone_e164, telegram_chat_id, is_active) ' +
        'VALUES ($1::uuid, $2, $1::uuid, $3, now(), NULL, NULL, NULL, true)',
      [client, owner.tenantId, 'Nour Co'],
    ),
  );
  const context = await api();
  try {
    expect((await context.post('/api/auth/login', { data: { username: owner.username, password: owner.password } })).status()).toBe(204);
    expect((await context.post(`/api/tenants/${owner.tenantId}/select`)).status()).toBe(204);
    const categories = await (await context.get('/api/subscriptions/categories')).json();
    const category = (categories.categories ?? categories.items ?? categories)[0].id as string;
    const create = async (service: string, endsOn: string) => {
      const response = await context.post('/api/subscriptions', {
        data: { client_id: client, service_name: service, ends_on: endsOn, category_id: category, billing_cycle: 'yearly', auto_renew: false },
      });
      expect(response.status()).toBe(201);
      return (await response.json()).id as string;
    };
    await create('Hosting', '2099-01-31');
    const mail = await create('Mail', '2026-05-01');
    expect((await context.post(`/api/subscriptions/${mail}/renew`, { data: { ends_on: '2099-05-01' } })).status()).toBe(201);
    const backup = await create('Backup', '2099-02-01');
    expect((await context.post(`/api/subscriptions/${backup}/cancel`)).status()).toBe(204);
  } finally {
    await context.dispose();
  }
  return owner;
}

const services = (page: import('@playwright/test').Page) => page.locator('tbody tr td:first-child').allTextContents();

test('1. the filter: "Active" by default, one status at a time, in the address, declared after it', async ({ page }) => {
  await page.context().addCookies([{ name: 'lang', value: 'en', url: BASE_URL }]);
  const owner = await organizationWithHistory();
  await signIn(page, owner.username, owner.password);
  await expect(page).toHaveURL(/\/app\/subscriptions$/);

  // The default: the present only — the active row and the renewal, not the history behind it.
  await expect(page.getByRole('radio', { name: 'Active' })).toBeChecked();
  await expect(page.locator('.scope')).toHaveText("You are shown the organization's active subscriptions: 2 subscriptions.");
  expect((await services(page)).sort()).toEqual(['Hosting', 'Mail']);

  await page.getByRole('radio', { name: 'Renewed' }).check();
  await expect(page).toHaveURL(/\/app\/subscriptions\?status=renewed$/);
  await expect(page.locator('.scope')).toHaveText("You are shown the organization's renewed subscriptions: 1 subscription.");
  expect(await services(page)).toEqual(['Mail']);

  // A reload keeps the filter; Back returns to the one before.
  await page.reload();
  await expect(page.getByRole('radio', { name: 'Renewed' })).toBeChecked();
  expect(await services(page)).toEqual(['Mail']);
  await page.goBack();
  await expect(page).toHaveURL(/\/app\/subscriptions$/);
  await expect(page.getByRole('radio', { name: 'Active' })).toBeChecked();
  await expect(page.locator('tbody tr')).toHaveCount(2);

  await page.getByRole('radio', { name: 'Cancelled' }).check();
  await expect(page.locator('.scope')).toHaveText("You are shown the organization's cancelled subscriptions: 1 subscription.");
  expect(await services(page)).toEqual(['Backup']);

  await page.getByRole('radio', { name: 'All' }).check();
  await expect(page).toHaveURL(/\/app\/subscriptions\?status=all$/);
  await expect(page.locator('.scope')).toHaveText("You are shown all of the organization's subscriptions: 4 subscriptions.");
  expect((await services(page)).sort()).toEqual(['Backup', 'Hosting', 'Mail', 'Mail']);
});

test('2. every date is yyyy-MM-dd, left-to-right, on one line — the list and the renew screen, ar and en, wide and narrow', async ({ page }) => {
  await page.context().addCookies([{ name: 'lang', value: 'en', url: BASE_URL }]);
  const owner = await organizationWithHistory();
  await signIn(page, owner.username, owner.password);
  await expect(page).toHaveURL(/\/app\/subscriptions$/);

  for (const lang of ['ar', 'en'] as const)
    for (const width of [1280, 390]) {
      await page.context().addCookies([{ name: 'lang', value: lang, url: BASE_URL }]);
      await page.setViewportSize({ width, height: 900 });
      await page.goto('/app/subscriptions?status=all');
      await expect(page.locator('html')).toHaveAttribute('dir', lang === 'ar' ? 'rtl' : 'ltr');
      await expect(page.locator('tbody tr')).toHaveCount(4);
      const where = `${lang}, ${width} px`;

      const days = await page.locator('td time').evaluateAll((times) =>
        times.map((t) => {
          const line = parseFloat(getComputedStyle(t).lineHeight) || parseFloat(getComputedStyle(t).fontSize) * 1.5;
          return { text: t.textContent, datetime: t.getAttribute('datetime'), dir: t.querySelector('bdi')?.getAttribute('dir'), oneLine: t.getBoundingClientRect().height < line * 1.5 };
        }),
      );
      expect(days.map((d) => d.text).sort(), where).toEqual(['2026-05-01', '2099-01-31', '2099-02-01', '2099-05-01']);
      for (const day of days) {
        expect(day.text, where).toBe(day.datetime);
        expect(day.dir, where).toBe('ltr');
        expect(day.oneLine, `${where}: ${day.text} on one line`).toBe(true);
      }

      await page.goto('/app/subscriptions');
      await page.getByRole('button', { name: lang === 'ar' ? 'إجراءات Hosting' : 'Actions for Hosting' }).click();
      await page.getByRole('menuitem', { name: lang === 'ar' ? 'تجديد Hosting' : 'Renew Hosting' }).click();
      const current = page.locator('[data-field="currentEnd"] time');
      await expect(current).toHaveText('2099-01-31');
      await expect(current.locator('bdi')).toHaveAttribute('dir', 'ltr');
    }
});

// Item 6, DateField. The limit, stated: the browser's own picker is drawn by the browser, outside the page, and a
// headless run cannot drive it. So this proves what the page owns — the keypad's text with its hyphens written, the
// button calling showPicker() (recorded, not opened), and a day set on the date input reaching the text as ISO and
// the saved renewal — and not how any browser draws its picker (OPEN_ITEMS 97: iOS Safari is unverified).
test('3. the date field: typed with its hyphens written, picked through showPicker(), saved as ISO', async ({ page }) => {
  await page.addInitScript(() => {
    const calls: string[] = [];
    (window as unknown as { showPickerCalls: string[] }).showPickerCalls = calls;
    HTMLInputElement.prototype.showPicker = function (this: HTMLInputElement) {
      calls.push(`${this.type}:${this.value}`);
    };
  });
  await page.context().addCookies([{ name: 'lang', value: 'en', url: BASE_URL }]);
  const owner = await organizationWithHistory();
  await signIn(page, owner.username, owner.password);
  await expect(page).toHaveURL(/\/app\/subscriptions$/);

  await page.getByRole('button', { name: 'Actions for Hosting' }).click();
  await page.getByRole('menuitem', { name: 'Renew Hosting' }).click();
  const field = page.getByLabel(/^New end date/);

  // The numeric keypad has no hyphen: the field writes them, and a backspace after one takes the digit before it.
  await expect(field).toHaveAttribute('inputmode', 'numeric');
  await field.pressSequentially('2099');
  await expect(field).toHaveValue('2099-');
  await field.press('Backspace');
  await expect(field).toHaveValue('209');
  await field.pressSequentially('90301');
  await expect(field).toHaveValue('2099-03-01');

  // The button opens the picker on the day typed; the day picked is written as ISO.
  const pick = page.getByRole('button', { name: 'Choose a date' });
  await expect(pick).toHaveCSS('min-block-size', '44px');
  await pick.click();
  expect(await page.evaluate(() => (window as unknown as { showPickerCalls: string[] }).showPickerCalls)).toEqual(['date:2099-03-01']);
  await page.locator('[data-field="endsOn"] input[type="date"]').fill('2099-04-30', { force: true });
  await expect(field).toHaveValue('2099-04-30');

  await page.getByRole('button', { name: 'Renew' }).click();
  await expect(page).toHaveURL(/\/app\/subscriptions$/);
  await expect(page.locator('tbody tr', { hasText: 'Hosting' }).locator('time')).toHaveText('2099-04-30');

  // The same field on the subscription form, in Arabic: named in the language, typed in Arabic-Indic digits.
  await page.context().addCookies([{ name: 'lang', value: 'ar', url: BASE_URL }]);
  await page.goto('/app/subscriptions/new');
  await expect(page.getByRole('button', { name: 'اختيار تاريخ' })).toBeVisible();
  const ends = page.getByLabel(/^ينتهي في/);
  await ends.pressSequentially('٢٠٩٩٠٦٣٠');
  await expect(ends).toHaveValue('2099-06-30');
});
