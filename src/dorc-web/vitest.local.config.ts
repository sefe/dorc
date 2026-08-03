import { defineConfig } from 'vitest/config';
import { playwright } from '@vitest/browser-playwright';
export default defineConfig({
  oxc: { decorator: { legacy: true }, assumptions: { setPublicClassFields: true }, typescript: { removeClassFieldsWithoutInitializer: true } },
  test: { include: ['tests/**/*.test.ts'], globals: true, setupFiles: ['./tests/_setup.ts'], testTimeout: 30000, hookTimeout: 30000,
    browser: { enabled: true, provider: playwright({ launchOptions: { executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' } }), headless: true, instances: [{ browser: 'chromium' }] } }
});
