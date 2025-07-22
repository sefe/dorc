import { css, LitElement } from 'lit';
import '@vaadin/button';
import { customElement, property } from 'lit/decorators.js';
import '../dorc-icon.js';
import { html } from 'lit/html.js';
import {
  DatabaseApiModel,
  RefDataEnvironmentsDetailsApi
} from '../../apis/dorc-api';

@customElement('database-env-controls')
export class DatabaseEnvControls extends LitElement {
  @property({ type: Object }) dbDetails: DatabaseApiModel | undefined;

  @property({ type: Number })
  envId = 0;

  @property({ type: Boolean }) private readonly = true;

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
        title="Detach database"
        theme="icon"
        @click="${this.detailedResults}"
        ?disabled="${this.readonly}"
      >
        <dorc-icon icon="unlink"></dorc-icon>
      </vaadin-button>
      <vaadin-button
        title="Manage permissions"
        theme="icon"
        @click="${this.manage}"
        ?disabled="${this.readonly}"
      >
        <dorc-icon icon="group-add"></dorc-icon>
      </vaadin-button>
      <vaadin-button
        title="View database permissions"
        theme="icon"
        @click="${this.view}"
      >
        <dorc-icon icon="group" color="primary"></dorc-icon>
      </vaadin-button>
    `;
  }

  detailedResults() {
    const answer = confirm('Detach database?');
    if (answer && this.dbDetails?.Id) {
      const api = new RefDataEnvironmentsDetailsApi();
      api
        .refDataEnvironmentsDetailsPut({
          componentId: this.dbDetails.Id,
          component: 'database',
          action: 'detach',
          envId: this.envId
        })
        .subscribe(() => {
          this.fireDbDetachedEvent();
        });
    }
  }

  manage() {
    this.fireManageDbPerms();
  }

  view() {
    this.fireViewDbPerms();
  }

  private fireDbDetachedEvent() {
    const event = new CustomEvent('database-detached', {
      detail: {
        message: 'Database detached successfully!'
      }
    });
    this.dispatchEvent(event);
  }

  private fireManageDbPerms() {
    const event = new CustomEvent('manage-database-perms', {
      detail: {
        message: ''
      }
    });
    this.dispatchEvent(event);
  }

  private fireViewDbPerms() {
    const event = new CustomEvent('view-database-perms', {
      detail: {
        message: ''
      }
    });
    this.dispatchEvent(event);
  }
}
