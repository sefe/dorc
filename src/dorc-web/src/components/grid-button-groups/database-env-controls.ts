import { confirmPrompt } from '../confirm-prompt';
import { css, LitElement } from 'lit';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { styleMap } from 'lit/directives/style-map.js';
import {
  DatabaseApiModel,
  RefDataEnvironmentsDetailsApi
} from '../../apis/dorc-api';
import '../../icons/social-icons.js';
import '@vaadin/tooltip';

@customElement('database-env-controls')
export class DatabaseEnvControls extends LitElement {
  @property({ type: Object }) dbDetails: DatabaseApiModel | undefined;

  @property({ type: Number })
  envId = 0;

  @property({ type: Boolean }) readonly = true;

  static get styles() {
    return css`
      :host {
        display: inline-flex;
        align-items: center;
        flex-wrap: nowrap;
        gap: var(--lumo-space-xs);
      }
      vaadin-button {
        padding: 0px;
        margin: 0px;
      }
    `;
  }

  render() {
    const unlinkStyles = {
      color: this.readonly
        ? 'var(--dorc-text-secondary)'
        : 'var(--dorc-error-color)'
    };
    const editStyles = {
      color: this.readonly
        ? 'var(--dorc-text-secondary)'
        : 'var(--dorc-link-color)'
    };
    return html`
      <vaadin-button
        aria-label="Detach database"
        theme="icon"
        @click="${this.detailedResults}"
        ?disabled="${this.readonly}"
      >
        <vaadin-tooltip slot="tooltip" text="Detach database"></vaadin-tooltip>
        <vaadin-icon
          icon="vaadin:unlink"
          style=${styleMap(unlinkStyles)}
        ></vaadin-icon>
      </vaadin-button>
      <vaadin-button
        aria-label="Manage permissions"
        theme="icon"
        @click="${this.manage}"
        ?disabled="${this.readonly}"
      >
        <vaadin-tooltip
          slot="tooltip"
          text="Manage permissions"
        ></vaadin-tooltip>
        <vaadin-icon
          icon="social:group-add"
          style=${styleMap(editStyles)}
        ></vaadin-icon>
      </vaadin-button>
      <vaadin-button
        aria-label="View database permissions"
        theme="icon"
        @click="${this.view}"
      >
        <vaadin-tooltip
          slot="tooltip"
          text="View database permissions"
        ></vaadin-tooltip>
        <vaadin-icon
          icon="social:group"
          style="color: var(--dorc-link-color)"
        ></vaadin-icon>
      </vaadin-button>
    `;
  }

  async detailedResults() {
    // Snapshot before awaiting: this control sits in a recycled grid cell, so
    // `this.dbDetails` can belong to a different row by the time the user answers.
    const database = this.dbDetails;
    const envId = this.envId;
    const answer = await confirmPrompt('Detach database?');
    if (answer && database?.Id) {
      const api = new RefDataEnvironmentsDetailsApi();
      api
        .refDataEnvironmentsDetailsPut({
          componentId: database.Id,
          component: 'database',
          action: 'detach',
          envId
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
