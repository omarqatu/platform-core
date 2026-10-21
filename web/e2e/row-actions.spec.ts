import { randomUUID } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';
import { api, asMigrator, BASE_URL, ownOrganization, passwordOf, signIn } from './fixtures';

// The lists' actions [B], against the real Api serving the built interface — the project owner's items 1 and 77, by the
// standard of a person looking at the screen.
//   * Item 1: a row's actions sit on one line, in a cell that is its row's — never wrapped, never level with the row
//     above — and in reach without scrolling, in both languages, wide and at phone width, on both lists and in every
//     state a subscription row can be in.
//   * Item 77: a viewer is offered nothing the server would refuse.
// The organization is the test's own; its one client is the fixture's, as migrator (as subscriptions.spec.ts), and its
// four subscriptions go through the real API, renewal and cancellation included. Nothing of the seed is written.

const WIDTHS = [1280, 390] as const;
const LANGS = ['ar', 'en'] as const;

/** An organization with one client and one subscription in each state the list shows. */
async function organizationInEveryState() {
  const owner = await ownOrganization('row-actions');
  const client = randomUUID();
  await asMigrator((db) =>
    db.query(
      'INSERT INTO clients (id, tenant_id, scope_ref_id, name, created_at, email, phone_e164, telegram_chat_id, is_active) ' +
        'VALUES ($1::uuid, $2, $1::uuid, $3, now(), NULL, NULL, NULL, true)',
      [client, owner.tenantId, 'شركة النور'],
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
    await create('Hosting', '2099-01-31'); // active
    await create('Old domain', '2020-03-07'); // active, past its end date
    const renewed = await create('Mail', '2026-05-01');
    expect((await context.post(`/api/subscriptions/${renewed}/renew`, { data: { ends_on: '2099-05-01' } })).status()).toBe(201);
    const cancelled = await create('Backup', '2099-02-01');
    expect((await context.post(`/api/subscriptions/${cancelled}/cancel`)).status()).toBe(204);
  } finally {
    await context.dispose();
  }
  return owner;
}

/** Every actions cell of the list on screen, measured — the page not scrolled. */
function measure(page: Page) {
  return page.evaluate(() => {
    const vw = document.documentElement.clientWidth;
    return [...document.querySelectorAll<HTMLTableRowElement>('.data-table tbody tr')].map((tr) => {
      const td = tr.querySelector<HTMLElement>('td.actions')!;
      const row = tr.getBoundingClientRect();
      const cell = td.getBoundingClientRect();
      // One line: every child's vertical middle within half a line of the others' (a link and a button differ by a
      // pixel, so equal tops would be the wrong test).
      const kids = [...td.querySelectorAll('.row-actions > *')].map((k) => k.getBoundingClientRect());
      const middles = kids.map((k) => (k.top + k.bottom) / 2);
      const line = Math.max(0, ...kids.map((k) => k.height));
      const button = td.querySelector('.row-actions-button')?.getBoundingClientRect();
      return {
        row: tr.textContent,
        sameTop: Math.abs(cell.top - row.top) < 0.5,
        sameBottom: Math.abs(cell.bottom - row.bottom) < 0.5,
        oneLine: kids.length === 0 || Math.max(...middles) - Math.min(...middles) < line / 2,
        buttonOnScreen: button === undefined || (button.left >= 0 && button.right <= vw && button.top >= 0),
        kind: button ? (td.querySelector('[data-note="expired"]') ? 'menu, expired' : 'menu') : 'note',
        scrolled: window.scrollX !== 0 || window.scrollY !== 0,
      };
    });
  });
}

test('1. the actions cell: one line, its own row, in reach — both lists, every state, ar and en, wide and narrow', async ({ page }) => {
  const owner = await organizationInEveryState();
  await signIn(page, owner.username, owner.password);
  await expect(page).toHaveURL(/\/app\/subscriptions$/);

  for (const lang of LANGS)
    for (const width of WIDTHS)
      for (const path of ['/app/subscriptions', '/app/clients']) {
        await page.context().addCookies([{ name: 'lang', value: lang, url: BASE_URL }]);
        await page.setViewportSize({ width, height: 900 });
        await page.goto(path);
        await expect(page.locator('html')).toHaveAttribute('dir', lang === 'ar' ? 'rtl' : 'ltr');
        await expect(page.locator('td.actions').first()).toBeVisible();
        await page.evaluate(() => window.scrollTo(0, 0));

        const cells = await measure(page);
        const where = `${path}, ${lang}, ${width} px`;
        expect(cells.length, where).toBe(path === '/app/subscriptions' ? 5 : 1);
        for (const cell of cells) {
          expect(cell.sameTop && cell.sameBottom, `${where}: the actions cell is its row's — ${cell.row}`).toBe(true);
          expect(cell.oneLine, `${where}: one line — ${cell.row}`).toBe(true);
          expect(cell.buttonOnScreen, `${where}: the button on screen, not scrolled to — ${cell.row}`).toBe(true);
          expect(cell.scrolled, where).toBe(false);
        }
        if (path === '/app/subscriptions')
          // The four states: active (and the renewal's new row), past its end date, renewed, cancelled.
          expect(cells.map((c) => c.kind).sort(), where).toEqual(['menu', 'menu', 'menu, expired', 'note', 'note']);
      }
});

// A viewer reads everything and writes nothing (layla, the seed's viewer with scope all): no way to create, and no
// actions column at all — the server would refuse each of them.
test('2. a viewer is offered nothing to do, on either list', async ({ page }) => {
  await page.context().addCookies([{ name: 'lang', value: 'en', url: BASE_URL }]);
  await signIn(page, 'layla', passwordOf('layla'));
  await expect(page).toHaveURL(/\/app\/subscriptions$/);

  await expect(page.locator('tbody tr').first()).toBeVisible();
  await expect(page.getByRole('link', { name: 'New subscription' })).toHaveCount(0);
  await expect(page.getByRole('columnheader', { name: 'Actions' })).toHaveCount(0);
  await expect(page.locator('.row-actions-button')).toHaveCount(0);

  await page.getByRole('link', { name: 'Clients' }).click();
  await expect(page).toHaveURL(/\/app\/clients$/);
  await expect(page.locator('tbody tr').first()).toBeVisible();
  await expect(page.getByRole('link', { name: 'New client' })).toHaveCount(0);
  await expect(page.getByRole('columnheader', { name: 'Actions' })).toHaveCount(0);
  await expect(page.locator('.row-actions-button')).toHaveCount(0);
});

// OPEN_ITEMS 93: at phone width the page is never wider than the screen — the top bar's controls fit it — so the
// heading and the scope declaration are whole, in both languages, on both lists. The organization is empty: the bar
// is the same whatever the list holds.
test('3. at 390 px the page does not scroll sideways, and the heading and the scope declaration are whole', async ({ page }) => {
  const owner = await ownOrganization('no-sideways');
  await signIn(page, owner.username, owner.password);
  await expect(page).toHaveURL(/\/app\/subscriptions$/);
  await page.setViewportSize({ width: 390, height: 844 });

  for (const lang of LANGS)
    for (const path of ['/app/subscriptions', '/app/clients']) {
      await page.context().addCookies([{ name: 'lang', value: lang, url: BASE_URL }]);
      await page.goto(path);
      await expect(page.locator('html')).toHaveAttribute('dir', lang === 'ar' ? 'rtl' : 'ltr');
      await expect(page.locator('p.scope')).toBeVisible();

      const where = `${path}, ${lang}`;
      const seen = await page.evaluate(() => {
        const inside = (selector: string) => {
          const box = document.querySelector(selector)!.getBoundingClientRect();
          return box.left >= 0 && box.right <= document.documentElement.clientWidth;
        };
        return { scrollWidth: document.documentElement.scrollWidth, heading: inside('.shell-content h1'), scope: inside('p.scope') };
      });
      expect(seen.scrollWidth, `${where}: no sideways scroll`).toBe(390);
      expect(seen.heading, `${where}: the heading whole`).toBe(true);
      expect(seen.scope, `${where}: the scope declaration whole`).toBe(true);
    }
});
