import { defineConfig } from 'vitest/config';
import { playwright } from '@vitest/browser-playwright';

// Which engines the suite runs against. All three by default — CI does not set
// VITEST_BROWSERS, so every build on every branch runs the full matrix. The
// override exists for local iteration (`VITEST_BROWSERS=chromium npm test`)
// when you do not want to wait on firefox and webkit.
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
    typescript: { removeClassFieldsWithoutInitializer: true }
  },
  test: {
    include: ['tests/**/*.test.ts'],
    globals: true,
    setupFiles: ['./tests/_setup.ts'],
    testTimeout: 30000,
    hookTimeout: 30000,
    browser: {
      enabled: true,
      // Pin the browser API to IPv4 so Windows hosts that cannot bind IPv6
      // localhost can execute the browser suite.
      api: {
        host: '127.0.0.1',
        port: 51234
      },
      provider: playwright(),
      headless: true,
      instances: BROWSERS.map(browser => ({ browser }))
    }
  }
});
