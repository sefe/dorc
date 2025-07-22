import { css, LitElement } from 'lit';
import '@vaadin/button';
import { customElement, property } from 'lit/decorators.js';
import '../dorc-icon.js';
import { html } from 'lit/html.js';
import { ApiBoolResult, DatabaseApiModel, RefDataDatabasesApi } from '../../apis/dorc-api';
import { ErrorNotification } from '../notifications/error-notification.ts';

@customElement('database-controls')
export class DatabaseControls extends LitElement {
  @property({ type: Object }) databaseDetails: DatabaseApiModel | undefined;

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
        title="Edit Database Details"
        theme="icon"
        @click="${this.editDatabase}"
        ?disabled="${this.readonly}"
      >
        <dorc-icon icon="edit"></dorc-icon>
      </vaadin-button>

      <vaadin-button
        title="Delete database"
        theme="icon"
        @click="${this.deleteDatabase}"
        ?disabled="${this.readonly}"
      >
        <dorc-icon icon="delete"></dorc-icon>
      </vaadin-button>
    `;
  }

  deleteDatabase() {
    const answer = confirm(`Delete database ${this.databaseDetails?.Name}?`);
    if (answer && this.databaseDetails?.Id) {
      const api = new RefDataDatabasesApi();
      api
        .refDataDatabasesDelete({
          databaseId: this.databaseDetails.Id
        })
        .subscribe({
          next: (result: ApiBoolResult) => {
            if (result.Result === true) {
              const database = this.databaseDetails;
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
          error: err => console.error(err),
          complete: () =>
            console.log(`Deleted Server ${this.databaseDetails?.Name}`)
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
