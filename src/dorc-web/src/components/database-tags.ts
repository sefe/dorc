import { LitElement, css } from 'lit';
import { customElement, property, query, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/button';
import { Notification } from '@vaadin/notification';
import { DatabaseApiModel, RefDataDatabasesApi } from '../apis/dorc-api';
import { normaliseTags } from '../helpers/tag-parser';
import { MAX_TAG_LENGTH } from '../helpers/tag-limits';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';
import { ErrorNotification } from './notifications/error-notification';
import './tags-input';
import { TagsInput } from './tags-input';

export function formatDatabaseTagTokens(tag: string): string[] {
  const trimmed = tag.trim();
  if (!trimmed) return [];
  const normalized = trimmed.replace(/ /g, '_');
  const tokens: string[] = [];
  if (/^[A-Za-z0-9_.-]+$/.test(normalized)) {
    tokens.push(`$DbServer_${normalized}$`, `$DbName_${normalized}$`);
  }
  const lower = trimmed.toLowerCase();
  if (lower === 'endur') {
    tokens.push('$EndurDatabaseServer$', '$EndurDatabaseName$');
  } else if (lower === 'endur reporting') {
    tokens.push(
      '$ReportingDatabaseServer$',
      '$ReportingDatabaseName$',
      '$SsisPackageServer$'
    );
  } else if (lower === 'endur external') {
    tokens.push('$ExternalDatabaseServer$', '$ExternalDatabaseName$');
  }
  return tokens;
}

export function resolveDatabaseTagTokens(tags: string[]): {
  tokens: string[];
  conflicts: string[];
} {
  const tagsByNorm = new Map<string, string[]>();
  for (const tag of tags) {
    const norm = tag.trim().replace(/ /g, '_').toLowerCase();
    if (!norm) continue;
    tagsByNorm.set(norm, [...(tagsByNorm.get(norm) ?? []), tag]);
  }
  const conflictingNorms = new Set<string>();
  const conflicts: string[] = [];
  for (const [norm, group] of tagsByNorm.entries()) {
    if (group.length > 1) {
      conflictingNorms.add(norm);
      conflicts.push(group.map(t => `'${t}'`).join(' and '));
    }
  }
  const tokens = Array.from(
    new Set(
      tags
        .filter(
          tag =>
            !conflictingNorms.has(tag.trim().replace(/ /g, '_').toLowerCase())
        )
        .flatMap(tag => formatDatabaseTagTokens(tag))
    )
  );
  return { tokens, conflicts };
}
@customElement('database-tags')
export class DatabaseTags extends LitElement {
  @property({ type: Object })
  get database(): DatabaseApiModel | undefined {
    return this._database;
  }

  set database(value: DatabaseApiModel | undefined) {
    const oldValue = this._database;
    this._database = value;
    this.tags = normaliseTags(value?.Tags);
    this.liveTags = [...this.tags];
    this.hasLiveTagsEdit = false;
    this.requestUpdate('database', oldValue);
  }

  private _database: DatabaseApiModel | undefined;

  @property({ type: Array }) tags: string[] = [];

  @state() private liveTags: string[] = [];

  private hasLiveTagsEdit = false;

  @query('#tag-input')
  private tagsInput: TagsInput | undefined;

  static get styles() {
    return css`
      .token-hints {
        margin: var(--lumo-space-xs, 4px) 0;
        padding: 6px 10px;
        border-radius: var(--lumo-border-radius-m, 6px);
        background: var(--dorc-bg-secondary, rgba(128, 128, 128, 0.08));
        border: 1px solid var(--dorc-border-color, rgba(128, 128, 128, 0.2));
        font-size: var(--lumo-font-size-xs, 0.78rem);
        color: var(--lumo-secondary-text-color);
      }
      .token-hints-label {
        display: block;
        font-weight: 600;
        margin-bottom: 4px;
        color: var(--lumo-body-text-color);
      }
      .token-chip-list {
        display: flex;
        flex-wrap: wrap;
        gap: 6px;
        align-items: center;
      }
      .token-chip {
        display: inline-flex;
        align-items: center;
        padding: 2px 8px;
        border-radius: 999px;
        border: 1px solid var(--dorc-border-color, rgba(128, 128, 128, 0.28));
        background: var(--dorc-chip-bg, rgba(59, 130, 246, 0.12));
        color: var(--dorc-chip-text, inherit);
        font-family: var(--lumo-font-family-monospace, monospace);
        font-size: var(--lumo-font-size-xs, 0.78rem);
        cursor: pointer;
      }
      .token-chip:hover {
        filter: brightness(0.95);
      }
    `;
  }

  private get effectiveTags(): string[] {
    const source = this.hasLiveTagsEdit ? this.liveTags : this.tags;
    return normaliseTags(source);
  }

  private onTagsChanged(event: CustomEvent<{ tags?: string[] }>) {
    this.hasLiveTagsEdit = true;
    this.liveTags = normaliseTags(event.detail?.tags);
  }

  private async copyToken(token: string) {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(token);
      Notification.show(`Copied ${token} to clipboard`, {
        theme: 'contrast',
        position: 'bottom-start',
        duration: 2500
      });
    }
  }

  private renderTokenHints() {
    const { tokens, conflicts } = resolveDatabaseTagTokens(this.effectiveTags);
    return html`
      <div
        class="token-hints"
        role="region"
        aria-label="Exposed database tag tokens"
      >
        <span class="token-hints-label">
          Built-in variable token${tokens.length === 1 ? '' : 's'} exposed by
          database tags (for use in Variable Values):
        </span>
        ${
          tokens.length > 0
            ? html`
                <div class="token-chip-list">
                  ${tokens.map(
                    token => html`
                      <button
                        type="button"
                        class="token-chip"
                        title="Click to copy ${token}"
                        @click="${() => this.copyToken(token)}"
                      >
                        <code>${token}</code>
                      </button>
                    `
                  )}
                </div>
              `
            : html`<span class="token-empty-hint"
                >Add a tag (e.g. <code>Audit</code>) to expose
                <code>$DbServer_Audit$</code> and
                <code>$DbName_Audit$</code> in environment variables.</span
              >`
        }
        ${
          conflicts.length > 0
            ? html`<div class="token-conflict-hint" role="alert">
                Conflicting tags normalize to the same variable token:
                ${conflicts.join('; ')}. Rename one so the token can resolve.
              </div>`
            : ''
        }
      </div>
    `;
  }

  render() {
    return html`
      <tags-input
        id="tag-input"
        label="Tags"
        .tags="${this.hasLiveTagsEdit ? this.liveTags : this.tags}"
        @tags-changed="${this.onTagsChanged}"
      ></tags-input>
      ${this.renderTokenHints()}
      <vaadin-button @click="${this.save}">Save</vaadin-button>
    `;
  }

  public save() {
    if (!this._database) return;

    const tags = normaliseTags(this.tagsInput?.tags);
    const overLongTag = tags.find(tag => tag.length > MAX_TAG_LENGTH);
    if (overLongTag) {
      Notification.show(
        `Each tag must be at most ${MAX_TAG_LENGTH} characters (too long: '${overLongTag}')`,
        { theme: 'error', position: 'bottom-start', duration: 5000 }
      );
      return;
    }

    const database: DatabaseApiModel = {
      Id: this._database.Id,
      Name: this._database.Name,
      ServerName: this._database.ServerName,
      AdGroup: this._database.AdGroup,
      ArrayName: this._database.ArrayName,
      Tags: tags
    };
    const api = new RefDataDatabasesApi(dorcApiConfiguration);

    api
      .refDataDatabasesPut({
        id: database.Id ?? 0,
        databaseApiModel: database
      })
      .subscribe({
        next: updatedDatabase => {
          Notification.show(`Updated tags for database ${database.Name}`, {
            theme: 'success',
            position: 'bottom-start',
            duration: 5000
          });
          this.dispatchEvent(
            new CustomEvent('database-tags-updated', {
              detail: { data: updatedDatabase },
              bubbles: true,
              composed: true
            })
          );
        },
        error: (err: any) => {
          console.error(err);
          const notification = new ErrorNotification();
          notification.setAttribute(
            'errorMessage',
            retrieveErrorMessage(err, 'Failed to update database tags')
          );
          this.shadowRoot?.appendChild(notification);
          notification.open();
        }
      });
  }
}
