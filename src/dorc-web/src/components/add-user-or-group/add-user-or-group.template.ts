import '@vaadin/button';
import '@vaadin/combo-box';
import '@vaadin/text-field';

import { choose } from 'lit/directives/choose.js';
import { when } from 'lit/directives/when.js';

import { html } from 'lit/html.js';
import { nothing } from 'lit';

import { AddUserOrGroup } from './add-user-or-group';
import { LoginType } from './LoginType';

import './add-windows-user-or-group.ts';
import './add-sql-user-or-group.ts';
import './add-endur-user-or-group.ts';

export function addUserOrGroupTemplate(this: AddUserOrGroup) {
  return html`
    <div class="acc-form">
      <vaadin-vertical-layout>
        <div>
          <span
            >Adding users or groups here is only for app delegation and database
            users and groups</span
          >
          <vaadin-combo-box
            class="acc-form__login-types"
            id="system"
            label="System"
            @value-changed="${this.loginTypeChanged}"
            .items="${this.loginTypes}"
            placeholder="Select System"
            clear-button-visible
          ></vaadin-combo-box>
          <vaadin-combo-box
            class="acc-form__lan-id-types"
            id="system-account-type"
            label="System Account Type"
            @value-changed="${this.lanIdTypeChanged}"
            .items="${this.lanIdTypes}"
            placeholder="Select System Account Type"
            clear-button-visible
          ></vaadin-combo-box>
        </div>
      </vaadin-vertical-layout>

      ${when(
        this.selectedLoginType &&
          this.selectedLoginType.length > 0 &&
          this.selectedLanIdType &&
          this.selectedLanIdType.length > 0,
        () =>
          html`${choose(
            this.selectedLoginType,
            [
              [
                LoginType.Windows,
                () =>
                  html`<add-windows-user-or-group
                    lanIdType="${this.selectedLanIdType}"
                    createdEventName="${this.createdEventName}"
                    @created=${this.userOrGroupCreatedHandler}
                  ></add-windows-user-or-group>`
              ],
              [
                LoginType.Sql,
                () =>
                  html`<add-sql-user-or-group
                    lanIdType="${this.selectedLanIdType}"
                    createdEventName="${this.createdEventName}"
                    @created=${this.userOrGroupCreatedHandler}
                  ></add-sql-user-or-group>`
              ],
              [
                LoginType.Endur,
                () =>
                  html`<add-endur-user-or-group
                    lanIdType="${this.selectedLanIdType}"
                    createdEventName="${this.createdEventName}"
                    @created=${this.userOrGroupCreatedHandler}
                  ></add-endur-user-or-group>`
              ]
            ],
            () => html`${nothing}`
          )}`,
        () => html`${nothing}`
      )}
    </div>
  `;
}
