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

function extractProblemDetails(err: unknown): ProblemDetails {
  if (isRecord(err)) {
    const anyErr = err as any;
    const resp = anyErr.response ?? anyErr?.detail?.response;
    if (isRecord(resp)) return resp as ProblemDetails;

    const looksLikePd =
      typeof anyErr.title === 'string' ||
      typeof anyErr.detail === 'string' ||
      typeof anyErr.status === 'number';
    if (looksLikePd) return anyErr as ProblemDetails;
  }
  return {};
}

function getExtensions(pd: ProblemDetails): Record<string, unknown> | undefined {
  return (pd.extensions as any) || (pd.Extensions as any);
}

function blockersToLines(ext?: Record<string, unknown>): string[] {
  if (!ext || typeof ext !== 'object') return [];
  const blockers = (ext as any).blockers as Array<{ type: string; items: string[] }> | undefined;
  if (!Array.isArray(blockers)) return [];
  return blockers.flatMap(b => (b?.items ?? []).map(it => `• ${b.type}: ${it}`));
}

function buildUserMessageFromError(err: unknown, action: string): string {
  const pd = extractProblemDetails(err);

  const message =
    (pd.detail && String(pd.detail)) ||
    (pd.title && String(pd.title)) ||
    retrieveErrorMessage(err as any, `Failed to ${action}`);

  const ext = getExtensions(pd);
  const lines = blockersToLines(ext);
  const suffix = lines.length ? `\n\nBlocked by:\n${lines.join('\n')}` : '';

  const correlationId =
    pd.correlationId ?? (ext && typeof ext === 'object' ? (ext as any)['correlationId'] : undefined);
  if (correlationId) console.warn(`CorrelationId for last error: ${String(correlationId)}`);

  return message + suffix;
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
        const message = buildUserMessageFromError(err, 'delete database');
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