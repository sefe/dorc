import { css, LitElement } from 'lit';
import '@vaadin/icons';
import '@vaadin/icon';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { EnvironmentApiModel } from '../../apis/dorc-api';
import { urlForName } from '../../router/router';
import '../../icons/hardware-icons.js';

@customElement('env-detail-tab')
export class EnvDetailTab extends LitElement {
  @property({ type: Object }) public env: EnvironmentApiModel | undefined;

  static get styles() {
    return css`
      a {
        color: inherit; /* blue colors for links too */
        text-decoration: inherit; /* no underline */
        display: block;
        width: 100%;
      }
      vaadin-icon {
        width: var(--lumo-icon-size-s);
        height: var(--lumo-icon-size-s);
        font-size: var(--lumo-font-size-s);
      }
    `;
  }

  render() {
    return html` <div>
      <div style="margin-left: 20px; width: 270px">
        <a
          style="float:left"
          href="${urlForName('environment', {
            id: String(this.env?.EnvironmentName)
          })}"
        >
          <vaadin-icon
            icon="hardware:developer-board"
            theme="small"
          ></vaadin-icon>
          ${this.env?.EnvironmentName}
        </a>
        <vaadin-icon
          style="color: lightblue; float: right;  position: absolute; right: 5px; top: 5px;"
          icon="vaadin:close-small"
          theme="small"
          @click="${this.removeEnvDetail}"
        ></vaadin-icon>
      </div>
    </div>`;
  }

  removeEnvDetail() {
    const event = new CustomEvent('close-env-detail', {
      detail: {
        Environment: this.env
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }
}
