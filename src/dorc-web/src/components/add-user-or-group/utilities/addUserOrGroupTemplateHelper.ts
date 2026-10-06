import { html } from 'lit';
import { UserOrGroupSearchResult } from '.././UserOrGroupSearchResult';

/**
 * Combo-box item for a directory search hit.
 *
 * Shared by the Windows and Endur variants, so it stays a free function
 * rather than a component method — neither reads component state.
 */
export function renderSearchResults(searchResult: UserOrGroupSearchResult) {
  return html`<div>
    <b>${searchResult.DisplayName}</b><br />
    ${searchResult.FullLogonName}
  </div>`;
}
