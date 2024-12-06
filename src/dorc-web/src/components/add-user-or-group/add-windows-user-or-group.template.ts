import '@vaadin/button';
import '@vaadin/combo-box';
import '@vaadin/text-field';

import { html } from 'lit/html.js';
import { nothing } from 'lit';

import { AddWindowsUserOrGroup } from './add-windows-user-or-group';

import { renderSearchResults } from './utilities/addUserOrGroupTemplateHelper';

export function addWindowsUserOrGroupTemplate(this: AddWindowsUserOrGroup) {
  return html` <div>
    <vaadin-vertical-layout>
      <table>
        <tr>
          <td class="acc-filter__srch-td">
            <vaadin-text-field
              class="acc-filter__txf"
              id="win-id-filter"
              label="Windows ID Filter"
              @keypress="${this.filterKeypressed}"
              required
              allowed-char-pattern="[a-zA-Z0-9-_.' ()&]"
            ></vaadin-text-field>
          </td>
          <td class="acc-filter__btn-td">
            <vaadin-button
              @click="${this.updateUserOrGroupList}"
              class="acc-filter__btn"
            >
              Filter
            </vaadin-button>
          </td>
          <td class="acc-filter__ldr-td">
            ${this.isUserOrGroupLoadingCompleted
              ? html`${nothing}`
              : html`<div class="acc-filter__small-loader"></div> `}
          </td>
        </tr>
      </table>
      <vaadin-combo-box
        class="acc-form__block"
        id="windows-id"
        label="Windows ID"
        item-value-path="FullLogonName"
        item-label-path="DisplayName"
        .invalid="${this.isSelectedUserOrGroupValid === false}"
        error-message="${this.selectedUserOrGroupErrorMessage}"
        .disabled="${!this.isUserOrGroupListEnabled}"
        .renderer="${renderSearchResults}"
        .items="${this.searchResults}"
        @change="${this.filteredUserOrGroupSelected}"
      ></vaadin-combo-box>
      <vaadin-text-field
        class="acc-form__block"
        id="team"
        label="Team"
        .invalid="${this.isTeamNameValid === false}"
        error-message="${this.teamNameErrorMessage}"
        @value-changed="${this.teamNameChanged}"
      >
      </vaadin-text-field>
    </vaadin-vertical-layout>
    <div>
      <vaadin-button @click="${this.reset}">Clear</vaadin-button>
      <vaadin-button .disabled="${!this.isModelValid}" @click="${this.submit}"
        >Save</vaadin-button
      >
    </div>
    <span class="acc-filter__span">${this.overlayMessage}</span>
  </div>`;
}
