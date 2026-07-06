import { ComboBoxItemModel } from '@vaadin/combo-box';
import { html, render } from 'lit';
import { UserOrGroupSearchResult } from '.././UserOrGroupSearchResult';

export function renderSearchResults(
  root: HTMLElement,
  _: HTMLElement,
  { item }: ComboBoxItemModel<UserOrGroupSearchResult>
) {
  const searchResult = item as UserOrGroupSearchResult;
  render(
    html`<div>
      <b>${searchResult.DisplayName}</b><br />${searchResult.FullLogonName}
    </div>`,
    root
  );
}
