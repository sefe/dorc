import globals from 'globals';
import pluginJs from '@eslint/js';
import tseslint from 'typescript-eslint';
import eslintConfigPrettier from 'eslint-config-prettier';
import pluginLit from 'eslint-plugin-lit';
import pluginWc from 'eslint-plugin-wc';
import pluginLitA11y from 'eslint-plugin-lit-a11y';

// eslint-plugin-lit-a11y ships an eslintrc-style config only, so its
// recommended rule set is applied through a flat config block here.
const litA11yRecommended = pluginLitA11y.configs.recommended.rules;

export default [
  { files: ['**/*.{js,mjs,cjs,ts}'] },
  { languageOptions: { globals: globals.browser } },
  { ignores: ['src/apis', 'node_modules', 'dist', 'public'] },
  pluginJs.configs.recommended,
  ...tseslint.configs.recommended,
  pluginWc.configs['flat/recommended'],
  pluginLit.configs['flat/recommended'],
  {
    files: ['src/**/*.ts', 'tests/**/*.ts'],
    plugins: { 'lit-a11y': pluginLitA11y },
    rules: litA11yRecommended
  },
  eslintConfigPrettier,
  { rules: { '@typescript-eslint/no-explicit-any': 0 } },
  {
    // Build/analysis scripts run under Node, not the browser.
    files: ['tools/**/*.mjs', 'scripts/**/*.mjs', '*.config.js', '*.config.ts'],
    languageOptions: { globals: globals.node }
  },
  {
    files: ['tests/**/*.ts'],
    rules: {
      '@typescript-eslint/no-unused-expressions': 0
    }
  }
];
