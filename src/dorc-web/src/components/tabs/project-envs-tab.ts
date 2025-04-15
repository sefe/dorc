import { css, LitElement } from 'lit';
import '@vaadin/icons';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { ProjectApiModel } from '../../apis/dorc-api';
import { urlForName } from '../../router/router';
import '@vaadin/icon';

@customElement('project-envs-tab')
export class ProjectEnvsTab extends LitElement {
  @property({ type: Object }) public project: ProjectApiModel | undefined;

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
          href="${urlForName('project-envs', {
            id: String(this.project?.ProjectName)
          })}"
        >
          <vaadin-icon icon="vaadin:records"></vaadin-icon>
          ${this.project?.ProjectName}
        </a>
        <vaadin-icon
          style="color: lightblue; float: right;  position: absolute; right: 5px; top: 5px; min-height: var(--lumo-size-xs);"
          icon="vaadin:close-small"
          theme="small"
          @click="${this.removeProjEnvs}"
        ></vaadin-icon>
      </div>
    </div>`;
  }

  removeProjEnvs() {
    const event = new CustomEvent('close-project-envs', {
      detail: {
        Project: this.project
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }
}
