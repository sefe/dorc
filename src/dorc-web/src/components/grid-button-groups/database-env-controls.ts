import { confirmPrompt } from '../confirm-prompt';
import { css, LitElement, nothing } from 'lit';
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
import '@vaadin/tooltip';
import { dorcApiConfiguration } from '../../services/dorc-api-configuration';
import type { DatabaseAccessView } from '../database-access-panel';

@customElement('database-env-controls')
export class DatabaseEnvControls extends LitElement {
  @property({ type: Object }) dbDetails: DatabaseApiModel | undefined;

  @property({ type: Number })
  envId = 0;

  @property({ type: Boolean }) readonly = true;

  @property({ type: Boolean }) canManageDatabaseAccess = false;

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
    const databaseAccessStyles = {
      color: 'var(--dorc-link-color)'
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
        aria-label="Edit Database Tags"
        theme="icon"
        @click="${this.manageDatabaseTags}"
        ?disabled="${this.readonly}"
      >
        <vaadin-tooltip
          slot="tooltip"
          text="Edit Database Tags"
        ></vaadin-tooltip>
        <vaadin-icon
          icon="vaadin:tags"
          style=${styleMap(editStyles)}
        ></vaadin-icon>
      </vaadin-button>
      ${
        this.canManageDatabaseAccess
          ? html`
              ${this.renderDatabaseAccessButton(
                'permissions',
                'Assign database permissions',
                'vaadin:key',
                databaseAccessStyles
              )}
              ${this.renderDatabaseAccessButton(
                'audit',
                'Database access audit',
                'vaadin:clipboard-text',
                databaseAccessStyles
              )}
            `
          : nothing
      }
    `;
  }

  private renderDatabaseAccessButton(
    view: DatabaseAccessView,
    label: string,
    icon: string,
    styles: Record<string, string>
  ) {
    return html`
      <vaadin-button
        aria-label="${label}"
        theme="icon"
        @click="${() => this.manageDatabaseAccess(view)}"
      >
        <vaadin-tooltip slot="tooltip" text="${label}"></vaadin-tooltip>
        <vaadin-icon icon="${icon}" style=${styleMap(styles)}></vaadin-icon>
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
      const api = new RefDataEnvironmentsDetailsApi(dorcApiConfiguration);
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

  manageDatabaseAccess(view: DatabaseAccessView) {
    this.dispatchEvent(
      new CustomEvent('manage-database-access', { detail: { view } })
    );
  }

  private fireDbDetachedEvent() {
    const event = new CustomEvent('database-detached', {
      detail: {
        message: 'Database detached successfully!'
      }
    });
    this.dispatchEvent(event);
  }

  private manageDatabaseTags() {
    this.dispatchEvent(
      new CustomEvent('manage-database-tags', {
        bubbles: true,
        composed: true,
        detail: {
          database: this.dbDetails
        }
      })
    );
  }
}