import { cleanup } from '@testing-library/react';
import { afterEach } from 'vitest';

// Without vitest globals, Testing Library does not unmount by itself between tests.
afterEach(cleanup);
