import { ComboBoxItemModel } from '@vaadin/combo-box';
import { UserOrGroupSearchResult } from '.././UserOrGroupSearchResult';

export function renderSearchResults(
  root: HTMLElement,
  _: HTMLElement,
  { item }: ComboBoxItemModel<UserOrGroupSearchResult>
) {
  const searchResult = item as UserOrGroupSearchResult;
  root.innerHTML =
    '<div><b>' +
    searchResult.DisplayName +
    '</b><br>' +
    searchResult.FullLogonName +
    '</div>';
}
