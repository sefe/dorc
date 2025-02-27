import { css } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import { customElement } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PageEnvBase } from './page-env-base';
import '@vaadin/details';
import '../attached-app-users';

@customElement('env-users')
export class EnvUsers extends PageEnvBase {
  static get styles() {
    return css`
      :host {
        width: 100%;
      }
    `;
  }

  render() {
    return html`
      <vaadin-details
        opened
        summary="Application Users"
        style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px; margin: 0px;"
      >
        <attached-app-users
          id="application-users"
          .users="${this.envContent?.EndurUsers ?? []}"
        >
        </attached-app-users>
      </vaadin-details>
    `;
  }

  constructor() {
    super();

    super.loadEnvironmentInfo();
  }
}
