import { css, LitElement, html } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { styleMap } from 'lit/directives/style-map.js';
import '@vaadin/button';
import '@vaadin/icon';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/vaadin-lumo-styles/icons.js';
import '../../icons/iron-icons.js';
import { ApiBoolResult, DatabaseApiModel, RefDataDatabasesApi } from '../../apis/dorc-api';
import { ErrorNotification } from '../notifications/error-notification';
import { retrieveErrorMessage } from '../../helpers/errorMessage-retriever.js';

@customElement('database-controls')
export class DatabaseControls extends LitElement {
  @property({ type: Object }) databaseDetails?: DatabaseApiModel;

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
        aria-label="Edit database details"
        title="Edit Database Details"
        theme="icon"
        @click=${this.editDatabase}
        ?disabled=${this.readonly}
      >
        <vaadin-icon 
        icon="lumo:edit" 
        style=${styleMap(editStyles)}
        ></vaadin-icon>
      </vaadin-button>

      <vaadin-button
        aria-label="Delete database"
        title="Delete database"
        theme="icon"
        @click=${this.deleteDatabase}
        ?disabled=${this.readonly}
      >
        <vaadin-icon
          icon="icons:delete"
          style=${styleMap(unlinkStyles)}
        ></vaadin-icon>
      </vaadin-button>
    `;
  }

  private showError(message: string) {
    const notification = new ErrorNotification();
    notification.setAttribute('errorMessage', message);
    this.shadowRoot?.appendChild(notification);
    notification.open();
  }

  private deleteDatabase() {
    const name = this.databaseDetails?.Name ?? 'this database';
    const id = this.databaseDetails?.Id;
    const answer = confirm(`Are you sure you want to delete database "${name}"?\nThis cannot be undone.`);
    if (!answer || !id) return;

    const api = new RefDataDatabasesApi();
    api
      .refDataDatabasesDelete({ 
        databaseId: id
       })
      .subscribe({
        next: (result: ApiBoolResult) => {
          if (result?.Result === true) {
            const event = new CustomEvent('database-deleted', {
              composed: true,
              bubbles: true,
              detail: { 
                database: this.databaseDetails }
            });
            this.dispatchEvent(event);
          } else {
            const msg = (result?.Message && String(result.Message).trim()) || 'Failed to delete database';
            this.showError(msg);
            console.error(msg);
          }
        },
        error: (err: any) => {
          const base = 'Failed to delete database';

          // 1) Helper first
          let message = (retrieveErrorMessage(err, base) || '').trim();
          const status = (err?.status ?? err?.xhr?.status) as number | undefined;
          const genericAjaxMsg = status ? `ajax error ${status}` : '';
          const isFallback =
            !message ||
            message === base ||
            message.toLowerCase() === genericAjaxMsg; // e.g., "ajax error 409"

          // 2) If fallback, read our mirrored header (works even with responseType:"json")
          if (isFallback) {
            try {
              const getHeader = err?.xhr?.getResponseHeader as ((name: string) => string | null) | undefined;
              const headerMsg = typeof getHeader === 'function' ? getHeader.call(err.xhr, 'X-Error-Message') : null;

              if (headerMsg && headerMsg.trim()) {
                message = (retrieveErrorMessage(headerMsg, base) || headerMsg || base).trim();
              }
            } catch (e) {
              console.debug('Header extraction failed:', e);
            }
          }

          // 3) Last resort: use HTTP reason phrase to avoid "ajax error 409"
          if (!message || message === base || message.toLowerCase() === genericAjaxMsg) {
            const reason = err?.xhr?.statusText || err?.statusText || '';
            if (reason) message = `${base}${status ? ` (${status})` : ''}: ${reason}`;
          }

          if (!message) message = base;
          this.showError(message);

          console.error('Delete database failed:', err);
          try {
            if (err?.xhr?.getAllResponseHeaders) {
              console.debug('Response headers:\n' + err.xhr.getAllResponseHeaders());
              const dbgHeader = err?.xhr?.getResponseHeader?.('X-Error-Message');
              console.debug('X-Error-Message header =', dbgHeader);
            }
          } catch (e) {
             console.debug('Header dump failed:', e);
          }
        },
        complete: () => console.log(`Deleted database ${name}`)
      });
  }

  private editDatabase() {
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