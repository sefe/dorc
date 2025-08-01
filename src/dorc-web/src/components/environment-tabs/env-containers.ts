import { css } from 'lit';
import { customElement } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PageEnvBase } from './page-env-base';

@customElement('env-containers')
export class EnvContainers extends PageEnvBase {

  static get styles() {
    return css`
      :host {
        width: 100%;
        height: 100%;
        display: flex;
        flex-direction: column;
        padding: 20px;
      }
      .placeholder {
        text-align: center;
        color: #666;
        font-style: italic;
        margin: 50px 0;
      }
    `;
  }

  render() {
    return html`
      <h3>Containers</h3>
      <div class="placeholder">
        Container management functionality will be implemented here.
      </div>
    `;
  }
}