import globals from 'globals';
import pluginJs from '@eslint/js';
import tseslint from 'typescript-eslint';
import eslintConfigPrettier from 'eslint-config-prettier';

export default [
  { files: ['**/*.{js,mjs,cjs,ts}'] },
  { languageOptions: { globals: globals.browser } },
  { ignores: ['src/apis/**', 'node_modules/**', 'dist/**', 'public/**'] },
  pluginJs.configs.recommended,
  ...tseslint.configs.recommended,
  eslintConfigPrettier,
  { rules: { '@typescript-eslint/no-explicit-any': 0 } },
  {
    files: ['src/**/*.test.ts', 'src/test-setup.ts'],
    rules: {
      '@typescript-eslint/no-unused-vars': 'off',
      '@typescript-eslint/no-unsafe-function-type': 'off',
      'no-unused-vars': 'off'
    }
  }
];
