import '@polymer/paper-toggle-button';
import '@vaadin/details';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import { css } from 'lit';
import { customElement } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../application-daemons';
import { PageEnvBase } from './page-env-base';
import '../project-card';

@customElement('env-projects')
export class EnvProjects extends PageEnvBase {
  static get styles() {
    return css`
      :host {
        width: 100%;
      }
      vaadin-details {
        overflow: auto;
        width: calc(100% - 4px);
        height: calc(100vh - 180px);
      }
    `;
  }

  render() {
    return html`
      <vaadin-details
        opened
        summary="Mapped Projects"
        style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px; margin: 0px;"
      >
        ${this.envContent?.MappedProjects?.map(
          proj => html`<project-card .project="${proj}"></project-card>`
        )}
      </vaadin-details>
    `;
  }

  constructor() {
    super();

    super.loadEnvironmentInfo();
  }
}
