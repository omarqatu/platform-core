import { describe, expect, it } from 'vitest';
import exported from '../../../checks/local/api-error-codes.json';
import { apiErrors, errorMessage } from './apiErrors';

describe('errorMessage', () => {
  it('returns the entry of every code the backend declares', () => {
    for (const code of exported.codes) expect(errorMessage(code).id).toBe(`errors.${code}`);
  });
  it('keys every entry by its own code', () => {
    for (const [key, descriptor] of Object.entries(apiErrors)) expect(descriptor.id).toBe(`errors.${key}`);
  });
  it('returns unknown for a code it does not know, or none', () => {
    for (const code of ['no_such_code', 'toString', '__proto__', '', null, undefined])
      expect(errorMessage(code).id).toBe('errors.unknown');
  });
});
