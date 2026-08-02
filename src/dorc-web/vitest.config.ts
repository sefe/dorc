import { defineConfig } from 'vitest/config';
import { playwright } from '@vitest/browser-playwright';

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
      instances: [
        { browser: 'chromium' },
        { browser: 'firefox' },
        { browser: 'webkit' },
      ],
    },
  },
});
