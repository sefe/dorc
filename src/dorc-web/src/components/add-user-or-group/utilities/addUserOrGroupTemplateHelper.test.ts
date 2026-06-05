import { describe, it, expect } from 'vitest';
import { renderSearchResults } from './addUserOrGroupTemplateHelper';

describe('renderSearchResults', () => {
  it('renders display name and logon name as text', () => {
    const root = document.createElement('div');

    renderSearchResults(root, document.createElement('div'), {
      item: { DisplayName: 'Jane Doe', FullLogonName: 'DOMAIN\\jdoe' }
    } as any);

    const text = root.textContent ?? '';
    expect(text).toContain('Jane Doe');
    expect(text).toContain('DOMAIN\\jdoe');
  });

  it('escapes HTML in directory values instead of injecting it (no XSS)', () => {
    const root = document.createElement('div');
    const payload = '<img src=x onerror="window.__xss=1">';

    renderSearchResults(root, document.createElement('div'), {
      item: { DisplayName: payload, FullLogonName: '<script>alert(1)</script>' }
    } as any);

    // The payload must not become live DOM...
    expect(root.querySelector('img')).toBeNull();
    expect(root.querySelector('script')).toBeNull();
    // ...and must survive as inert text.
    expect(root.textContent).toContain('<img src=x onerror=');
    expect(root.textContent).toContain('<script>alert(1)</script>');
  });
});
