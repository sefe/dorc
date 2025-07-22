import { css, LitElement } from 'lit';
import '@vaadin/icons';
import { customElement, property } from 'lit/decorators.js';
import '../dorc-icon.js';
import { html } from 'lit/html.js';
import { EnvironmentApiModel } from '../../apis/dorc-api';
import { urlForName } from '../../router/router';

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
          <dorc-icon icon="environment"></dorc-icon>
          ${this.env?.EnvironmentName}
        </a>
        <dorc-icon icon="close-small" color="lightblue"></dorc-icon>
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
