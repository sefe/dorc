import { css, LitElement } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/button';
import '@vaadin/icons';
import '@vaadin/vaadin-lumo-styles/icons.js';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { ProjectApiModel } from '../../apis/dorc-api';

@customElement('project-controls')
export class ProjectControls extends LitElement {
  @property({ type: Object }) project: ProjectApiModel | undefined;

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
        <dorc-icon icon="edit" color="primary"></dorc-icon>
      </vaadin-button>
      <vaadin-button
        title="Project Access..."
        theme="icon"
        @click="${this.openAccessControl}"
      >
        <dorc-icon icon="lock" color="primary"></dorc-icon>
      </vaadin-button>
      <vaadin-button
        title="Environments"
        theme="icon"
        @click="${this.openEnvironmentDetails}"
      >
        <dorc-icon icon="list" color="primary"></dorc-icon>
      </vaadin-button>
      <vaadin-button
        title="Reference Data"
        theme="icon"
        @click="${this.openRefData}"
      >
        <dorc-icon icon="code" color="primary"></dorc-icon>
      </vaadin-button>
      <vaadin-button
        title="Audit"
        theme="icon"
        @click="${this.openAuditData}"
      >
        <dorc-icon icon="list" color="primary"></dorc-icon>
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
}
