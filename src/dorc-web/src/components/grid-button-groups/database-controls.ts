import { css, LitElement } from 'lit';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { styleMap } from 'lit/directives/style-map.js';
import '../../icons/iron-icons.js';
import '@vaadin/vaadin-lumo-styles/icons.js';
import {
  ApiBoolResult,
  DatabaseApiModel,
  RefDataDatabasesApi
} from '../../apis/dorc-api';
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
      vaadin-button {
        padding: 0px;
        margin: 0px;
      }
    `;
  }

  render() {
    const unlinkStyles = {
      color: this.readonly ? 'grey' : '#FF3131'
    };
    const editStyles = {
      color: this.readonly ? 'grey' : 'cornflowerblue'
    };
    return html`
      <vaadin-button
        title="Edit Database Details"
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
          error: (err: any) => {
            console.error(err);
            const notification = new ErrorNotification();
            const errorMessage = retrieveErrorMessage(
              err,
              'Failed to delete database'
            );

            notification.setAttribute('errorMessage', errorMessage);
            this.shadowRoot?.appendChild(notification);
            notification.open();
          },
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
