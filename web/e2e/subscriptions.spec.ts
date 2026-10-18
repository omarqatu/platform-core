import { randomUUID } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';
import { asMigrator, BASE_URL, inEnglish, ownOrganization, signIn } from './fixtures';

// Creating and editing a subscription with its category (PLATFORM_CORE Amendment 02 §3.1, §3.2) [B], against the
// real Api: an organization of the test's own (the module has no client screen yet, so its one client is the
// fixture's, as migrator), its owner entering it directly. Nothing of the seed contract is written.

async function organizationWithClient(label: string) {
  const owner = await ownOrganization(label);
  const client = randomUUID();
  await asMigrator((db) =>
    db.query('INSERT INTO clients (id, tenant_id, scope_ref_id, name, created_at) VALUES ($1::uuid, $2, $1::uuid, $3, now())', [
      client,
      owner.tenantId,
      'شركة النور',
    ]),
  );
  return { owner, client };
}

const stored = (tenantId: string) =>
  asMigrator(async (db) => {
    const { rows } = await db.query(
      `SELECT s.service_name, s.ends_on::text, c.code AS category, s.provider, s.cost::text, s.currency, s.billing_cycle,
              s.auto_renew, s.login_url, s.reminder_type
       FROM subscriptions s JOIN categories c ON c.id = s.category_id WHERE s.tenant_id = $1`,
      [tenantId],
    );
    return rows;
  });

async function enter(page: Page, owner: { username: string; password: string }) {
  await signIn(page, owner.username, owner.password);
  await expect(page).toHaveURL(/\/app\/subscriptions$/);
}

test('1. a subscription is created with its category and its fields, then edited, in English', async ({ page }) => {
  await inEnglish(page);
  const { owner } = await organizationWithClient('subscription-form');
  await enter(page, owner);

  await page.getByRole('link', { name: 'New subscription' }).click();
  await expect(page.getByRole('heading', { name: 'New subscription' })).toBeVisible();
  await expect(page.getByLabel(/^Category/).locator('option')).toHaveText([
    'Choose a category',
    'Domains',
    'Hosting',
    'Security',
    'Software',
    'Backup',
    'AI',
    'Other',
  ]);
  // No subcategory is seeded, so the field is not there to choose from.
  await expect(page.getByLabel(/^Subcategory/)).toHaveCount(0);

  // Sent with what is missing: each field says so, and nothing is written.
  await page.getByRole('button', { name: 'Create subscription' }).click();
  await expect(page.getByRole('alert')).toHaveText('Some fields need attention.');
  await expect(page.getByLabel(/^Service/)).toHaveAttribute('aria-invalid', 'true');
  expect(await stored(owner.tenantId)).toEqual([]);

  await page.getByLabel(/^Client/).selectOption({ label: 'شركة النور' });
  await page.getByLabel(/^Service/).fill('VPS');
  await page.getByLabel(/^Ends on/).fill('2027-03-31');
  await page.getByLabel(/^Category/).selectOption({ label: 'Hosting' });
  await page.getByLabel(/^Provider/).fill('Hetzner');
  await page.getByLabel(/^Cost/).fill('12.5');
  await page.getByLabel(/^Currency/).fill('jod');
  await page.getByLabel(/^Billing cycle/).selectOption({ label: 'Monthly' });
  await page.getByLabel('Renews automatically').check();
  await page.getByLabel(/^Management link/).fill('https://console.example.com/');
  await page.getByRole('button', { name: 'Create subscription' }).click();

  await expect(page).toHaveURL(/\/app\/subscriptions$/);
  await expect(page.getByRole('cell', { name: 'VPS', exact: true })).toBeVisible();
  expect(await stored(owner.tenantId)).toEqual([
    {
      service_name: 'VPS',
      ends_on: '2027-03-31',
      category: 'hosting',
      provider: 'Hetzner',
      cost: '12.500',
      currency: 'JOD',
      billing_cycle: 'monthly',
      auto_renew: true,
      login_url: 'https://console.example.com/',
      reminder_type: 'subscription',
    },
  ]);

  await page.getByRole('link', { name: 'Edit VPS' }).click();
  await expect(page.getByRole('heading', { name: 'Edit subscription' })).toBeVisible();
  await expect(page.getByLabel(/^Service/)).toHaveValue('VPS');
  await expect(page.getByLabel(/^Category/)).toHaveValue(/.+/);
  await expect(page.getByLabel(/^Cost/)).toHaveValue('12.500'); // as stored: numeric(14,3), carried as text
  await page.getByLabel(/^Service/).fill('VPS — renamed');
  await page.getByLabel(/^Category/).selectOption({ label: 'Software' });
  await page.getByLabel(/^Cost/).fill('0');
  await page.getByLabel(/^Billing cycle/).selectOption({ label: 'Yearly' });
  await page.getByLabel('Renews automatically').uncheck();
  await page.getByLabel(/^Management link/).fill('');
  await page.getByRole('button', { name: 'Save changes' }).click();

  await expect(page).toHaveURL(/\/app\/subscriptions$/);
  await expect(page.getByRole('cell', { name: 'VPS — renamed', exact: true })).toBeVisible();
  expect(await stored(owner.tenantId)).toEqual([
    expect.objectContaining({
      service_name: 'VPS — renamed',
      category: 'software',
      cost: '0.000',
      currency: 'JOD',
      billing_cycle: 'yearly',
      auto_renew: false,
      login_url: null,
    }),
  ]);
});

test('2. the form in Arabic: right-to-left, the catalog named in Arabic, Western digits', async ({ page }) => {
  await inEnglish(page); // the sign-in helper reads the English labels
  const { owner } = await organizationWithClient('subscription-form-ar');
  await enter(page, owner);
  await page.context().addCookies([{ name: 'lang', value: 'ar', url: BASE_URL }]);
  await page.reload();

  await page.getByRole('link', { name: 'اشتراك جديد' }).click();
  await expect(page.getByRole('heading', { name: 'اشتراك جديد' })).toBeVisible();
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.getByLabel(/^الفئة/).locator('option')).toHaveText([
    'اختر فئة',
    'النطاقات',
    'الاستضافة',
    'الحماية',
    'البرمجيات',
    'النسخ الاحتياطي',
    'الذكاء الاصطناعي',
    'أخرى',
  ]);

  await page.getByLabel(/^العميل/).selectOption({ label: 'شركة النور' });
  await page.getByLabel(/^الخدمة/).fill('نطاق الشركة');
  // Typed on an Arabic keyboard: the field shows, and the server receives, Western digits.
  await page.getByLabel(/^ينتهي في/).fill('٢٠٢٧-٠٦-٠١');
  await expect(page.getByLabel(/^ينتهي في/)).toHaveValue('2027-06-01');
  await page.getByLabel(/^الفئة/).selectOption({ label: 'النطاقات' });
  await page.getByLabel(/^دورة الفوترة/).selectOption({ label: 'سنوية' });
  await page.getByRole('button', { name: 'إنشاء الاشتراك' }).click();

  await expect(page).toHaveURL(/\/app\/subscriptions$/);
  await expect(page.getByRole('cell', { name: 'نطاق الشركة', exact: true })).toBeVisible();
  expect(await page.locator('main').textContent()).not.toMatch(/[٠-٩۰-۹]/);
  expect(await stored(owner.tenantId)).toEqual([
    expect.objectContaining({ service_name: 'نطاق الشركة', ends_on: '2027-06-01', category: 'domains', billing_cycle: 'yearly', cost: null }),
  ]);
});
