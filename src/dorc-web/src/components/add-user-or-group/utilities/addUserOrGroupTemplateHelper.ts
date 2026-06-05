import { ComboBoxItemModel } from '@vaadin/combo-box';
import { render } from 'lit';
import { html } from 'lit/html.js';
import { UserOrGroupSearchResult } from '.././UserOrGroupSearchResult';

export function renderSearchResults(
  root: HTMLElement,
  _: HTMLElement,
  { item }: ComboBoxItemModel<UserOrGroupSearchResult>
) {
  const searchResult = item as UserOrGroupSearchResult;
  // Render via Lit so directory-sourced values are escaped as text, not HTML
  // (DisplayName / FullLogonName come from AD search results — untrusted).
  render(
    html`<div><b>${searchResult.DisplayName}</b><br />${searchResult.FullLogonName}</div>`,
    root
  );
}
