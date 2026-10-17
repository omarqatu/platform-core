import { describe, expect, it } from 'vitest';
import { EMPTY, fromDetail, isDay, toBody, validate, westernDigits, type FormValues } from './subscriptionForm';

const complete: FormValues = {
  ...EMPTY,
  clientId: 'c1',
  serviceName: ' Hosting ',
  endsOn: '2027-03-31',
  categoryId: 'cat',
  billingCycle: 'yearly',
};

describe('westernDigits', () => {
  it('turns Arabic-Indic and Eastern Arabic-Indic digits, and the Arabic decimal separator, into Western ones', () => {
    expect(westernDigits('٢٠٢٧-٠٣-٣١')).toBe('2027-03-31');
    expect(westernDigits('۱۲٫۵')).toBe('12.5');
    expect(westernDigits('2027-03-31')).toBe('2027-03-31');
  });
});

describe('isDay', () => {
  it('accepts a real calendar day as yyyy-MM-dd and nothing else', () => {
    expect(isDay('2028-02-29')).toBe(true);
    expect(isDay('2027-02-29')).toBe(false);
    expect(isDay('2027-13-01')).toBe(false);
    expect(isDay('31/03/2027')).toBe(false);
    expect(isDay('2027-3-31')).toBe(false);
  });
});

describe('validate', () => {
  it('passes a complete form, the optional fields empty', () => {
    expect(validate(complete, 'create')).toEqual({});
  });

  it('requires the client to create, and not to edit', () => {
    expect(validate({ ...complete, clientId: '' }, 'create')).toEqual({ clientId: 'required' });
    expect(validate({ ...complete, clientId: '' }, 'edit')).toEqual({});
  });

  it('requires the service, the end date, the category and the billing cycle', () => {
    expect(validate({ ...EMPTY, clientId: 'c1', serviceName: '   ' }, 'create')).toEqual({
      serviceName: 'required',
      endsOn: 'required',
      categoryId: 'required',
      billingCycle: 'required',
    });
  });

  it('holds the cost to numeric(14,3), zero included, and a cost to its currency (E5)', () => {
    expect(validate({ ...complete, cost: '0', currency: 'USD' }, 'create')).toEqual({});
    expect(validate({ ...complete, cost: '12.345', currency: 'JOD' }, 'create')).toEqual({});
    expect(validate({ ...complete, cost: '12.3456', currency: 'JOD' }, 'create')).toEqual({ cost: 'cost' });
    expect(validate({ ...complete, cost: '-1', currency: 'JOD' }, 'create')).toEqual({ cost: 'cost' });
    expect(validate({ ...complete, cost: '1,5', currency: 'JOD' }, 'create')).toEqual({ cost: 'cost' });
    expect(validate({ ...complete, cost: '10' }, 'create')).toEqual({ currency: 'costNeedsCurrency' });
    expect(validate({ ...complete, currency: 'usd' }, 'create')).toEqual({ currency: 'currency' });
  });

  it('accepts only a full http or https address as the management link', () => {
    expect(validate({ ...complete, loginUrl: 'https://console.example.com' }, 'create')).toEqual({});
    expect(validate({ ...complete, loginUrl: 'console.example.com' }, 'create')).toEqual({ loginUrl: 'url' });
    expect(validate({ ...complete, loginUrl: 'javascript:alert(1)' }, 'create')).toEqual({ loginUrl: 'url' });
  });
});

describe('the request body', () => {
  it('never turns money into a binary floating-point number', () => {
    // 0.1 + 0.2 is the classic; the largest numeric(14,3) value has more significant digits than a double keeps exactly
    // in every arithmetic, so the text is carried as text.
    const body = toBody({ ...complete, cost: '99999999999.999', currency: 'JOD' }, 'create');
    expect(body.cost).toBe('99999999999.999');
    expect(JSON.stringify(body)).toContain('"cost":"99999999999.999"');
  });

  it('trims, sends an empty optional field as null, and the cost as the decimal string typed', () => {
    expect(toBody({ ...complete, provider: '  ', cost: '12.5', currency: 'JOD', autoRenew: true }, 'create')).toEqual({
      client_id: 'c1',
      service_name: 'Hosting',
      ends_on: '2027-03-31',
      category_id: 'cat',
      subcategory_id: null,
      provider: null,
      cost: '12.5',
      currency: 'JOD',
      billing_cycle: 'yearly',
      auto_renew: true,
      login_url: null,
    });
  });

  it('carries no client when editing: a subscription keeps its client', () => {
    expect(toBody(complete, 'edit')).not.toHaveProperty('client_id');
  });

  it('round-trips a stored subscription', () => {
    const values = fromDetail({
      id: 's1',
      client_id: 'c1',
      service_name: 'VPS',
      ends_on: '2027-03-31',
      reminder_type: 'subscription',
      category_id: 'cat',
      subcategory_id: null,
      provider: 'Hetzner',
      cost: '0.000',
      currency: 'EUR',
      billing_cycle: 'monthly',
      auto_renew: true,
      login_url: null,
    });
    expect(values.cost).toBe('0.000');
    expect(toBody(values, 'edit')).toMatchObject({ cost: '0.000', currency: 'EUR', provider: 'Hetzner', login_url: null });
  });
});
