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
        display: flex;
        width: 100%;
        height: 100%;
        flex-direction: column;
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
      </vaadin-details>
      <attached-app-users
        id="application-users"
        .users="${this.envContent?.EndurUsers ?? []}"
        style="width: 100%; height: 100%;"
      >
      </attached-app-users>
    `;
  }

  constructor() {
    super();

    super.loadEnvironmentInfo();
  }
}
