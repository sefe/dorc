import { defineConfig } from 'vitest/config';
import { playwright } from '@vitest/browser-playwright';

// Which engines the suite runs against. All three by default, so a bare
// `npm test` locally keeps the behaviour it has always had. CI narrows this to
// chromium on PRs and topic branches — three engines plus their Playwright
// download is a poor trade for the feedback loop there — and restores the full
// matrix on develop, main and release/**, which are the branches that ship.
const BROWSERS = (process.env.VITEST_BROWSERS ?? 'chromium,firefox,webkit')
  .split(',')
  .map(name => name.trim())
  .filter(Boolean);

if (BROWSERS.length === 0) {
  // An empty VITEST_BROWSERS would otherwise hand vitest an empty instance
  // list, which reports success without executing a single test.
  throw new Error('VITEST_BROWSERS was set but resolved to no browsers');
}

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
      instances: BROWSERS.map(browser => ({ browser })),
    },
  },
});
