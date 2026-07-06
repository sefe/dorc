import { expect } from '../_helpers';
import { renderSearchResults } from '../../src/components/add-user-or-group/utilities/addUserOrGroupTemplateHelper';
import type { ComboBoxItemModel } from '@vaadin/combo-box';
import type { UserOrGroupSearchResult } from '../../src/components/add-user-or-group/UserOrGroupSearchResult';

// Regression test for S-021 (finding G-1): backend-controlled strings must be
// rendered as text, not parsed as markup, in the combo-box/grid renderers that
// previously assigned to root.innerHTML.
describe('renderer XSS escaping', () => {
  it('renders a malicious DisplayName as text, not executable markup', () => {
    const root = document.createElement('div');
    const payload = '<img src=x onerror="window.__xss=1">';

    renderSearchResults(
      root,
      document.createElement('div'),
      { item: { DisplayName: payload, FullLogonName: 'DOMAIN\\evil' } } as
        ComboBoxItemModel<UserOrGroupSearchResult>
    );

    // No <img> element should have been created from the payload.
    expect(root.querySelector('img')).to.equal(null);
    // The payload must be present as literal text.
    expect(root.textContent).to.contain('<img src=x onerror=');
    // And the onerror handler must not have fired.
    expect((window as unknown as { __xss?: number }).__xss).to.equal(undefined);
  });
});
