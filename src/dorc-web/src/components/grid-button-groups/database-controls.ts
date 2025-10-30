import { css, LitElement, html } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { styleMap } from 'lit/directives/style-map.js';

import '@vaadin/button';
import '@vaadin/icon';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/vaadin-lumo-styles/icons.js';
import '../../icons/iron-icons.js';

import {
  ApiBoolResult,
  DatabaseApiModel,
  RefDataDatabasesApi
} from '../../apis/dorc-api';

import { ErrorNotification } from '../notifications/error-notification';
import { retrieveErrorMessage } from '../../helpers/errorMessage-retriever.js';

// --- Types ---
type ProblemDetails = {
  title?: string;
  detail?: string;
  status?: number;
  type?: string;
  instance?: string;
  blockers?: unknown;
  Extensions?: Record<string, unknown>;
  Message?: string;
  ExceptionMessage?: string;
  correlationId?: string;
};

// --- Helpers ---
function isRecord(x: unknown): x is Record<string, unknown> {
  return typeof x === 'object' && x !== null;
}

function isStringArray(x: unknown): x is string[] {
  return Array.isArray(x) && x.every(i => typeof i === 'string');
}

function toTitleCase(s: string): string {
  return s ? s[0].toUpperCase() + s.slice(1) : s;
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
    return err as ProblemDetails;
  }
  return {};
}

function formatBlockers(raw: unknown): string {
  if (raw == null) return '';
  const lines: string[] = [];

  const pushLine = (label: string, items: string[]) => {
    if (items.length > 0) lines.push(`- ${label}: ${items.join(', ')}`);
  };

  if (Array.isArray(raw)) {
    for (const entry of raw) {
      if (isRecord(entry)) {
        const type =
          typeof entry['type'] === 'string' ? entry['type'] : 'reference';
        const items = isStringArray(entry['items']) ? entry['items'] : [];
        pushLine(toTitleCase(type), items);
      }
    }
  } else if (isRecord(raw)) {
    for (const [key, value] of Object.entries(raw)) {
      const label = toTitleCase(key.replace(/_/g, ' ').trim());
      const items = isStringArray(value) ? value : [];
      pushLine(label, items);
    }
  }

  return lines.length > 0 ? `\n\nReasons:\n${lines.join('\n')}` : '';
}

function defaultMessageForStatus(status?: number): string | undefined {
  const messages: Record<number, string> = {
    0: 'Network error. Check your connection and try again.',
    400: 'Invalid request. Please check the input and try again.',
    401: 'You need to sign in to perform this action.',
    403: 'You don’t have permission to perform this action.',
    404: 'The database could not be found.',
    405: 'This action isn’t allowed for the requested resource.',
    408: 'The request timed out. Please try again.',
    409: 'Delete blocked by references. Remove references and try again.',
    410: 'The requested resource is no longer available.',
    415: 'Unsupported request format.',
    422: 'The request could not be processed. Please review the input.',
    429: 'Too many requests. Please wait a moment and try again.',
    500: 'Something went wrong on our side. Please try again.',
    502: 'Bad gateway. Please try again.',
    503: 'The service is temporarily unavailable. Please try again shortly.',
    504: 'The server took too long to respond. Please try again.'
  };
  return messages[status ?? -1];
}

function buildUserMessageFromError(err: unknown): string {
  const pd = extractProblemDetails(err);
  const status = (err as any)?.status;
  const title = typeof pd?.title === 'string' ? pd.title : undefined;
  const detail = typeof pd?.detail === 'string' ? pd.detail : undefined;
  const msg =
    typeof (pd as any)?.Message === 'string' ? (pd as any).Message : undefined;
  const exMsg =
    typeof (pd as any)?.ExceptionMessage === 'string'
      ? (pd as any).ExceptionMessage
      : undefined;

  const looksLikeEFSaveError =
    (exMsg &&
      /saving the entity changes.*see the inner exception/i.test(exMsg)) ||
    (msg && /DbUpdateException/i.test(msg));

  const blockers = pd?.blockers ?? pd?.Extensions?.blockers;
  const correlationId = pd?.correlationId ?? pd?.Extensions?.correlationId;

  if (correlationId) {
    console.warn(`CorrelationId for last error: ${correlationId}`);
  }

  let message: string | undefined;

  if (status === 409) {
    message = 'Delete blocked by references. Remove references and try again.';
  } else if (detail || title) {
    message = detail ?? title;
  } else if (msg || exMsg) {
    message = msg ?? exMsg;
  } else if (looksLikeEFSaveError) {
    message = 'Delete blocked by references. Remove references and try again.';
  } else {
    message = defaultMessageForStatus(status);
  }

  if (!message) {
    const fallback = retrieveErrorMessage(err as any, '');
    message = fallback ?? 'Failed to delete database';
  }

  message += formatBlockers(blockers);
  return message;
}

// --- Component ---
@customElement('database-controls')
export class DatabaseControls extends LitElement {
  @property({ type: Object }) databaseDetails?: DatabaseApiModel;
  @property({ type: Number }) envId = 0;
  @property({ type: Boolean }) private readonly = true;

  static styles = css`
    vaadin-button {
      padding: 0px;
      margin: 0px;
    }
  `;

  render() {
    const unlinkStyles = { color: this.readonly ? 'grey' : '#FF3131' };
    const editStyles = { color: this.readonly ? 'grey' : 'cornflowerblue' };

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

  private editDatabase() {
    const event = new CustomEvent('edit-database', {
      bubbles: true,
      composed: true,
      detail: { database: this.databaseDetails }
    });
    this.dispatchEvent(event);
  }

  private deleteDatabase() {
    const name = this.databaseDetails?.Name ?? 'this database';
    const id = this.databaseDetails?.Id;
    const answer = confirm(
      `Are you sure you want to delete database "${name}"?\nThis cannot be undone.`
    );

    if (!answer || !id) return;

    const api = new RefDataDatabasesApi();
    api.refDataDatabasesDelete({ databaseId: id }).subscribe({
      next: (result: ApiBoolResult) => {
        if (result?.Result === true) {
          const event = new CustomEvent('database-deleted', {
            composed: true,
            bubbles: true,
            detail: { database: this.databaseDetails }
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
}
