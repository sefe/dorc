import '@vaadin/button';
import '@vaadin/combo-box';
import '@vaadin/text-field';
import { html } from 'lit/html.js';

import { AddSqlUserOrGroup } from './add-sql-user-or-group';

export function addSqlUserOrGroupTemplate(this: AddSqlUserOrGroup) {
  return html` <div>
    <vaadin-vertical-layout>
      <vaadin-text-field
        class="acc-form__block"
        id="system-account-id"
        label="System Account Identifier"
        value="${this.lanId}"
        .invalid="${this.isLanIdValid === false}"
        error-message="${this.lanIdErrorMessage}"
        @value-changed="${this.lanIdChanged}"
      >
      </vaadin-text-field>
      <vaadin-text-field
        class="acc-form__block"
        id="displayName"
        label="Display Name"
        value="${this.displayName}"
        @value-changed="${this.displayNameChanged}"
        .invalid="${this.isDisplayNameValid === false}"
        error-message="${this.displayNameErrorMessage}"
      >
      </vaadin-text-field>
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
      <vaadin-button .disabled="${!this.isModelValid}" @click="${this.submit}"
        >Save</vaadin-button
      >
      <vaadin-button @click="${this.reset}">Clear</vaadin-button>
    </div>
    <span class="acc-filter__span">${this.overlayMessage}</span>
  </div>`;
}
