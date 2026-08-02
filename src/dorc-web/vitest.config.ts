import { defineConfig } from 'vitest/config';
import { playwright } from '@vitest/browser-playwright';

export default defineConfig({
  esbuild: {
    tsconfigRaw: JSON.stringify({
      compilerOptions: {
        experimentalDecorators: true,
        useDefineForClassFields: false,
      },
    }),
  },
  test: {
    include: ['tests/**/*.test.ts'],
    globals: true,
    setupFiles: ['./tests/_setup.ts'],
    testTimeout: 30000,
    hookTimeout: 30000,
    browser: {
      enabled: true,
      provider: playwright(),
      headless: true,
      instances: [
        { browser: 'chromium' },
        { browser: 'firefox' },
        { browser: 'webkit' },
      ],
    },
  },
});
