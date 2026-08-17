import globals from 'globals';
import pluginJs from '@eslint/js';
import tseslint from 'typescript-eslint';
import eslintConfigPrettier from 'eslint-config-prettier';

export default [
  { files: ['**/*.{js,mjs,cjs,ts}'] },
  { languageOptions: { globals: globals.browser } },
  { ignores: ['src/apis', 'node_modules', 'dist', 'public'] },
  pluginJs.configs.recommended,
  ...tseslint.configs.recommended,
  eslintConfigPrettier,
  { rules: { '@typescript-eslint/no-explicit-any': 0 } },
  {
    // Build tooling under scripts/ runs in Node and needs process. Flat
    // config merges globals across matching blocks rather than replacing
    // them, so these files end up with the browser globals from the block
    // above as well — this widens what is allowed, it does not restrict it.
    files: ['scripts/**/*.{js,mjs}'],
    languageOptions: { globals: globals.node },
  },
  {
    files: ['tests/**/*.ts'],
    rules: {
      '@typescript-eslint/no-unused-expressions': 0,
    },
  },
];
