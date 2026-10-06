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
      vaadin-details {
        overflow: hidden;
        width: calc(100% - 4px);
        flex: 0 0 auto;
        min-height: 0;
        display: flex;
        flex-direction: column;
      }
      vaadin-details[opened] {
        flex: 1 1 auto;
      }
      vaadin-details::part(content) {
        flex: 1;
        min-height: 0;
        display: flex;
        flex-direction: column;
        overflow: hidden;
      }
      attached-app-users {
        display: block;
        flex: 1;
        min-height: 0;
      }
    `;
  }

  render() {
    return html`
      <vaadin-details
        opened
        summary="Application Users"
        style="border-top: 6px solid var(--dorc-link-color); background-color: var(--dorc-bg-secondary); padding-left: 4px; margin: 0px;"
      >
        <attached-app-users
          id="application-users"
          .users="${this.envContent?.EndurUsers ?? []}"
          style="width: 100%; height: 100%;"
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
