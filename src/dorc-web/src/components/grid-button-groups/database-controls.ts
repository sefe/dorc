import { css, LitElement, html } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { styleMap } from 'lit/directives/style-map.js';
import '@vaadin/button';
import '@vaadin/icon';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/vaadin-lumo-styles/icons.js';
import '../../icons/iron-icons.js';
import {ApiBoolResult, DatabaseApiModel,RefDataDatabasesApi } from '../../apis/dorc-api';
import { ErrorNotification } from '../notifications/error-notification';
import { retrieveErrorMessage } from '../../helpers/errorMessage-retriever.js';
import type { AjaxError } from 'rxjs/ajax';

type ProblemDetails = {
  title?: string;
  detail?: string;
  status?: number;
  Extensions?: Record<string, unknown>;
  Message?: string;
  ExceptionMessage?: string;
  correlationId?: string;
};

function isRecord(x: unknown): x is Record<string, unknown> {
  return typeof x === 'object' && x !== null;
}

function isAjaxError(x: unknown): x is AjaxError {
  if (!isRecord(x)) return false;
  const anyX = x as any;
  const hasStatus = typeof anyX.status === 'number';
  const hasResponse = 'response' in anyX;
  const hasTransport = 'xhr' in anyX || 'request' in anyX;
  const isNamedAjaxError = anyX.name === 'AjaxError';
  return (hasStatus && hasResponse && hasTransport) || isNamedAjaxError;
}

function extractProblemDetails(err: unknown): ProblemDetails {
  if (isRecord(err)) {
    const detail = err['detail'];
    if (isRecord(detail)) {
      const response = detail['response'];
      if (isRecord(response)) return response as ProblemDetails;
    }
    const response = err['response'];
    if (isRecord(response)) return response as ProblemDetails;
    if (
      typeof err['title'] === 'string' ||
      typeof err['detail'] === 'string' ||
      typeof err['status'] === 'number'
    ) {
      return err as ProblemDetails;
    }
    return {};
  }
  return {};
}

function buildUserMessageFromError(err: unknown): string {
  const pd = extractProblemDetails(err);
  const title = typeof pd?.title === 'string' ? pd.title : undefined;
  const detail = typeof pd?.detail === 'string' ? pd.detail : undefined;
  const msg = typeof pd?.Message === 'string' ? pd.Message : undefined;
  const exMsg =
    typeof pd?.ExceptionMessage === 'string' ? pd.ExceptionMessage : undefined;

  const correlationId =
    pd?.correlationId ??
    (isRecord(pd?.Extensions)
      ? (pd!.Extensions as Record<string, unknown>)['correlationId'] as string | undefined
      : undefined);
  if (correlationId) {
    console.warn(`CorrelationId for last error: ${correlationId}`);
  }

  let message: string | undefined = detail ?? title ?? msg ?? exMsg;
  if (!message) {
    let fallback: string | undefined;
    if (typeof err === 'string') {
      fallback = retrieveErrorMessage(err, '');
    } else if (isAjaxError(err)) {
      fallback = retrieveErrorMessage(err, '');
    } else {
      fallback = undefined;
    }
    message = fallback ?? 'Failed to delete database';
  }
  return message;
}

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
    api.
      refDataDatabasesDelete({
         databaseId: id 
        })
         .subscribe({
      next: (result: ApiBoolResult) => {
        if (result?.Result === true) {
          const event = new CustomEvent('database-deleted', {
            composed: true,
            bubbles: true,
            detail: { 
              database: 
              this.databaseDetails 
            }
          });
          this.dispatchEvent(event);
        } else {
          this.showError(result?.Message ?? 'Failed to delete database');
          console.error(result?.Message);
        }
      },
      error: (err: unknown) => {
        const message = buildUserMessageFromError(err);
        this.showError(message);
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