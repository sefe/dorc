/// <reference types="vite/client" />

import { expect } from '../_helpers';

const sourceModules = import.meta.glob('../../src/**/*.ts', {
  eager: true,
  query: '?raw',
  import: 'default'
}) as Record<string, string>;

describe('DOrc API client construction', () => {
  it('does not bypass the shared API origin and authentication configuration', () => {
    const violations: string[] = [];

    for (const [file, source] of Object.entries(sourceModules)) {
      if (file.includes('/src/apis/')) continue;

      const pattern = /new\s+[A-Za-z0-9_]+Api\s*\(\s*([^)]*?)\s*\)/g;
      for (const match of source.matchAll(pattern)) {
        if (match[1] === 'dorcApiConfiguration') continue;
        const line = source.slice(0, match.index).split(/\r?\n/).length;
        violations.push(`${file}:${line}: ${match[0]}`);
      }
    }

    expect(
      violations,
      `API clients missing dorcApiConfiguration:\n${violations.join('\n')}`
    ).to.deep.equal([]);
  });
});
