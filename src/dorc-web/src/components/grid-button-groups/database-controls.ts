import { confirmPrompt } from '../confirm-prompt';
import { css, LitElement } from 'lit';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { styleMap } from 'lit/directives/style-map.js';
import '../../icons/iron-icons.js';
import '@vaadin/vaadin-lumo-styles/icons.js';
import { ApiBoolResult, DatabaseApiModel, RefDataDatabasesApi } from '../../apis/dorc-api';
import { ErrorNotification } from '../notifications/error-notification';
import { retrieveErrorMessage } from '../../helpers/errorMessage-retriever.js';

@customElement('database-controls')
export class DatabaseControls extends LitElement {
  @property({ type: Object }) databaseDetails: DatabaseApiModel | undefined;

  @property({ type: Number })
  envId = 0;

  @property({ type: Boolean }) private readonly = true;

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
      color: this.readonly ? 'var(--dorc-text-secondary)' : 'var(--dorc-error-color)'
    };
    const editStyles = {
      color: this.readonly ? 'var(--dorc-text-secondary)' : 'var(--dorc-link-color)'
    };
    return html`
      <vaadin-button
        title="Edit Database Details"
        aria-label="Edit Database Details"
        theme="icon"
        @click="${this.editDatabase}"
        ?disabled="${this.readonly}"
      >
        <vaadin-icon
          icon="lumo:edit"
          style=${styleMap(editStyles)}
        ></vaadin-icon>
      </vaadin-button>

      <vaadin-button
        title="Delete database"
        aria-label="Delete database"
        theme="icon"
        @click="${this.deleteDatabase}"
        ?disabled="${this.readonly}"
      >
        <vaadin-icon
          icon="icons:delete"
          style=${styleMap(unlinkStyles)}
        ></vaadin-icon>
      </vaadin-button>
    `;
  }

  async deleteDatabase() {
    // Snapshot before awaiting: this control sits in a recycled grid cell, so
    // `this.databaseDetails` can belong to a different row by the time the user answers.
    const database = this.databaseDetails;
    const answer = await confirmPrompt(`Delete database ${database?.Name}?`);
    if (answer && database?.Id) {
      const api = new RefDataDatabasesApi();
      api
        .refDataDatabasesDelete({
          databaseId: database.Id
        })
        .subscribe({
          next: (result: ApiBoolResult) => {
            if (result.Result === true) {
              // The snapshot, not a fresh read: the cell can be recycled
              // during the network round trip, and the delete itself
              // refreshes the grid.
              const event = new CustomEvent('database-deleted', {
                composed: true,
                bubbles: true,
                detail: {
                  database
                }
              });
              this.dispatchEvent(event);
            } else {
              const notification = new ErrorNotification();
              notification.setAttribute('errorMessage', result.Message ?? '');
              this.shadowRoot?.appendChild(notification);
              notification.open();
              console.error(result.Message);
            }
          },
          error: (err: any) => {
            console.error(err);
            const notification = new ErrorNotification();
            const errorMessage = retrieveErrorMessage(err, 'Failed to delete database');
            
            notification.setAttribute('errorMessage', errorMessage);
            this.shadowRoot?.appendChild(notification);
            notification.open();
          },
          complete: () =>
            console.log(`Deleted Database ${database?.Name}`)
        });
    }
  }

  editDatabase() {
    const event = new CustomEvent('edit-database', {
      bubbles: true,
      composed: true,
      detail: {
        database: this.databaseDetails
      }
    });
    this.dispatchEvent(event);
  }
}