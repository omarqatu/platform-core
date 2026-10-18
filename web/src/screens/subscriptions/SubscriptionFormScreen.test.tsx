import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { I18nProvider } from '../../i18n/I18nProvider';
import type { Lang } from '../../i18n/locale';
import { SubscriptionFormView, type FormData, type Mode } from './SubscriptionFormScreen';
import { EMPTY } from './subscriptionForm';

const ARABIC_INDIC_DIGITS = /[٠-٩۰-۹]/;

const data: FormData = {
  categories: [
    { id: 'k-domains', code: 'domains', name_ar: 'النطاقات', name_en: 'Domains', subcategories: [] },
    {
      id: 'k-hosting',
      code: 'hosting',
      name_ar: 'الاستضافة',
      name_en: 'Hosting',
      subcategories: [{ id: 'k-vps', code: 'vps', name_ar: 'خادم افتراضي', name_en: 'VPS' }],
    },
  ],
  clients: [
    { id: 'c1', name: 'Client A', is_active: true },
    { id: 'c2', name: 'شركة النور', is_active: true },
  ],
  values: EMPTY,
};

interface Call {
  method: string;
  path: string;
  body: unknown;
}

function api(answer: () => Response) {
  const calls: Call[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      calls.push({
        method: init?.method ?? 'GET',
        path: String(input).replace(/^\/api/, ''),
        body: init?.body === undefined ? undefined : JSON.parse(String(init.body)),
      });
      return answer();
    }),
  );
  return calls;
}

function Where() {
  return <output data-testid="went">{useLocation().pathname}</output>;
}

function show(mode: Mode, formData: FormData = data, lang: Lang = 'en') {
  return render(
    <I18nProvider initial={lang}>
      <MemoryRouter initialEntries={['/form']}>
        <Routes>
          <Route path="/form" element={<SubscriptionFormView mode={mode} state={{ status: 'ready', data: formData }} />} />
          <Route path="/app/subscriptions" element={<Where />} />
        </Routes>
      </MemoryRouter>
    </I18nProvider>,
  );
}

const change = (label: RegExp, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } });

afterEach(() => {
  vi.unstubAllGlobals();
  document.cookie = 'lang=; Path=/; Max-Age=0';
});

describe('creating a subscription', () => {
  it('sends every field, and returns to the list', async () => {
    const calls = api(() => Response.json({ id: 'new' }, { status: 201 }));
    show('create');
    change(/^Client/, 'c2');
    change(/^Service/, 'VPS');
    change(/^Ends on/, '٢٠٢٧-٠٣-٣١'); // typed on an Arabic keyboard
    change(/^Category/, 'k-hosting');
    change(/^Subcategory/, 'k-vps');
    change(/^Provider/, 'Hetzner');
    change(/^Cost/, '12.5');
    change(/^Currency/, 'jod');
    change(/^Billing cycle/, 'monthly');
    fireEvent.click(screen.getByLabelText('Renews automatically'));
    change(/^Management link/, 'https://console.example.com');
    fireEvent.click(screen.getByRole('button', { name: 'Create subscription' }));

    await waitFor(() => expect(screen.getByTestId('went').textContent).toBe('/app/subscriptions'));
    expect(calls).toEqual([
      {
        method: 'POST',
        path: '/subscriptions',
        body: {
          client_id: 'c2',
          service_name: 'VPS',
          ends_on: '2027-03-31',
          category_id: 'k-hosting',
          subcategory_id: 'k-vps',
          provider: 'Hetzner',
          cost: '12.5',
          currency: 'JOD',
          billing_cycle: 'monthly',
          auto_renew: true,
          login_url: 'https://console.example.com',
        },
      },
    ]);
  });

  it('shows the subcategory only for a category that has some, and clears it when the category changes', () => {
    show('create');
    expect(screen.queryByLabelText(/^Subcategory/)).toBeNull();
    change(/^Category/, 'k-hosting');
    change(/^Subcategory/, 'k-vps');
    change(/^Category/, 'k-domains');
    expect(screen.queryByLabelText(/^Subcategory/)).toBeNull();
    change(/^Category/, 'k-hosting');
    expect((screen.getByLabelText(/^Subcategory/) as HTMLSelectElement).value).toBe('');
  });

  it('says what is wrong beside each field, and sends nothing', () => {
    const calls = api(() => Response.json({ id: 'never' }, { status: 201 }));
    show('create');
    change(/^Ends on/, '31/03/2027');
    change(/^Cost/, '10');
    fireEvent.click(screen.getByRole('button', { name: 'Create subscription' }));

    expect(screen.getByRole('alert').textContent).toBe('Some fields need attention.');
    const service = screen.getByLabelText(/^Service/);
    expect(service.getAttribute('aria-invalid')).toBe('true');
    expect(document.getElementById(service.getAttribute('aria-describedby')!)!.textContent).toBe('This field is required.');
    const endsOn = screen.getByLabelText(/^Ends on/);
    expect(endsOn.getAttribute('aria-describedby')!.split(' ').map((id) => document.getElementById(id)!.textContent)).toEqual([
      'Year-month-day, such as 2027-03-31.',
      'Write a real date as year-month-day, such as 2027-03-31.',
    ]);
    expect(screen.getByText('A cost needs its currency.')).toBeTruthy();
    expect(calls).toEqual([]);
  });

  it('shows a refusal by its code, and stays', async () => {
    api(() => Response.json({ error: 'not_permitted' }, { status: 403 }));
    show('create', { ...data, values: { ...EMPTY, clientId: 'c1', serviceName: 'x', endsOn: '2027-01-01', categoryId: 'k-domains', billingCycle: 'yearly' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create subscription' }));
    await waitFor(() => expect(screen.getByRole('alert').textContent).toBe('You do not have permission to do this.'));
    expect(screen.queryByTestId('went')).toBeNull();
  });
});

describe('editing a subscription', () => {
  const stored: FormData = {
    ...data,
    id: 's1',
    values: { ...EMPTY, clientId: 'c2', serviceName: 'VPS', endsOn: '2027-03-31', categoryId: 'k-hosting', billingCycle: 'yearly' },
  };

  it('shows the client and does not change it, and sends the fields to the subscription', async () => {
    const calls = api(() => new Response(null, { status: 204 }));
    show('edit', stored);
    expect(screen.getByRole('heading').textContent).toBe('Edit subscription');
    expect(screen.queryByRole('combobox', { name: /^Client/ })).toBeNull();
    expect(screen.getByText('شركة النور').closest('bdi')!.getAttribute('dir')).toBe('auto');
    change(/^Service/, 'VPS 2');
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));

    await waitFor(() => expect(screen.getByTestId('went')).toBeTruthy());
    expect(calls[0]!.method).toBe('PUT');
    expect(calls[0]!.path).toBe('/subscriptions/s1');
    expect(calls[0]!.body).toMatchObject({ service_name: 'VPS 2', category_id: 'k-hosting', billing_cycle: 'yearly' });
    expect(calls[0]!.body).not.toHaveProperty('client_id');
  });
});

describe('in Arabic, right-to-left', () => {
  it('names the catalog in Arabic, the cycles and the labels too, with Western digits', () => {
    show('create', data, 'ar');
    expect(document.documentElement.dir).toBe('rtl');
    expect(screen.getByRole('heading').textContent).toBe('اشتراك جديد');
    const categories = [...(screen.getByLabelText(/^الفئة/) as HTMLSelectElement).options].map((o) => o.textContent);
    expect(categories).toEqual(['اختر فئة', 'النطاقات', 'الاستضافة']);
    const cycles = [...(screen.getByLabelText(/^دورة الفوترة/) as HTMLSelectElement).options].map((o) => o.textContent);
    expect(cycles).toEqual(['اختر الدورة', 'شهرية', 'سنوية']);
    // The fields that take digits are written left-to-right, and their example is in Western digits.
    expect(screen.getByLabelText(/^ينتهي في/).getAttribute('dir')).toBe('ltr');
    expect(screen.getByLabelText(/^الكلفة/).getAttribute('dir')).toBe('ltr');
    expect(document.body.textContent).toContain('2027-03-31');
    expect(document.body.textContent).not.toMatch(ARABIC_INDIC_DIGITS);
    expect(document.body.textContent).not.toContain('مستأجر');
  });
});
