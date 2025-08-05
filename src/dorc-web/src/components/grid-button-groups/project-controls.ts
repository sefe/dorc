import { css, LitElement } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/button';
import '@vaadin/icons';
import '@vaadin/vaadin-lumo-styles/icons.js';
import '../../icons/iron-icons.js';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { ProjectApiModel } from '../../apis/dorc-api';

@customElement('project-controls')
export class ProjectControls extends LitElement {
  @property({ type: Object }) project: ProjectApiModel | undefined;
  @property({ type: Boolean }) deleteHidden: boolean = true;

  static get styles() {
    return css`
      vaadin-button {
        padding: 0px;
        margin: 0px;
      }
    `;
  }

  render() {
    return html`
      <vaadin-button
        title="Edit Metadata..."
        theme="icon"
        @click="${this.openProjectMetadata}"
      >
        <vaadin-icon
          icon="lumo:edit"
          style="color: cornflowerblue"
        ></vaadin-icon>
      </vaadin-button>
      <vaadin-button
        title="Project Access..."
        theme="icon"
        @click="${this.openAccessControl}"
      >
        <vaadin-icon
          icon="vaadin:lock"
          style="color: cornflowerblue"
        ></vaadin-icon>
      </vaadin-button>
      <vaadin-button
        title="Environments"
        theme="icon"
        @click="${this.openEnvironmentDetails}"
      >
        <vaadin-icon
          icon="vaadin:records"
          style="color: cornflowerblue"
        ></vaadin-icon>
      </vaadin-button>
      <vaadin-button
        title="Reference Data"
        theme="icon"
        @click="${this.openRefData}"
      >
        <vaadin-icon
          icon="vaadin:curly-brackets"
          style="color: cornflowerblue"
        ></vaadin-icon>
      </vaadin-button>
      <vaadin-button
        title="Audit"
        theme="icon"
        @click="${this.openAuditData}"
      >
        <vaadin-icon
          icon="vaadin:list"
          style="color: cornflowerblue"
        ></vaadin-icon>
      </vaadin-button>
      <vaadin-button
        title="Delete Project"
        theme="icon"
        @click="${this.deleteProject}"
        ?hidden="${this.deleteHidden}"
      >
        <vaadin-icon
          icon="icons:delete"
          style="color: red"
        ></vaadin-icon>
      </vaadin-button>
    `;
  }

  openAccessControl() {
    const event = new CustomEvent('open-access-control', {
      detail: {
        Name: this.project?.ProjectName
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }

  openEnvironmentDetails() {
    const event = new CustomEvent('open-project-envs', {
      detail: {
        Project: this.project
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }

  openProjectMetadata() {
    const event = new CustomEvent('open-project-metadata', {
      detail: {
        Project: this.project
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }

  openRefData() {
    const event = new CustomEvent('open-project-ref-data', {
      detail: {
        Project: this.project
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }

  openAuditData() {
    const event = new CustomEvent('open-project-audit-data', {
      detail: {
        Project: this.project
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }

  deleteProject() {
    const event = new CustomEvent('delete-project', {
      detail: {
        Project: this.project
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }
}
