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
  // Vite 8 transforms with oxc, which ignores the `esbuild` option. Test files
  // live outside tsconfig.json's `include`, so oxc's tsconfig discovery does not
  // apply the project's decorator settings to them — they must be set here.
  // `setPublicClassFields` + `removeClassFieldsWithoutInitializer` is oxc's
  // equivalent of TypeScript's `useDefineForClassFields: false`, which Lit's
  // `@property` decorators depend on.
  oxc: {
    decorator: { legacy: true },
    assumptions: { setPublicClassFields: true },
    typescript: { removeClassFieldsWithoutInitializer: true },
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
