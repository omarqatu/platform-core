import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

// In development the API runs beside Vite (Api on http://127.0.0.1:5080 by default). Requests to /api/* are forwarded
// with the prefix removed, so the session cookie is same-origin as the API requires (SameSite=Strict).
const api = process.env.API_URL ?? 'http://127.0.0.1:5080';

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': { target: api, changeOrigin: true, rewrite: (path) => path.replace(/^\/api/, '') },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['src/test-setup.ts'],
  },
});
