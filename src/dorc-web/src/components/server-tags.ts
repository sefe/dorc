import { css, LitElement } from 'lit';
import './tags-input';
import { customElement, property, query, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { Notification } from '@vaadin/notification';
import { TagsInput } from './tags-input';
import { RefDataServersApi, ServerApiModel } from '../apis/dorc-api';
import { splitTags, normaliseTags } from '../helpers/tag-parser';
import { MAX_TAG_LENGTH } from '../helpers/tag-limits';
import '@vaadin/button';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';

export function formatServerTagToken(tag: string): string {
  const normalized = tag.trim().replace(/ /g, '_');
  return `$ServerNames_${normalized}$`;
}

@customElement('server-tags')
export class ServerTags extends LitElement {
  @property({ type: Object })
  get server(): ServerApiModel | undefined {
    return this._server;
  }

  set server(value: ServerApiModel | undefined) {
    const oldVal = this._server;
    this._server = value;
    this.setTags(value ?? {});
    this.requestUpdate('server', oldVal);
  }

  private _server: ServerApiModel | undefined;

  @property({ type: Array }) tags: string[] = [];

  @state() private liveTags: string[] = [];

  private hasLiveTagsEdit = false;

  @query('#tag-input')
  private tagsInput: TagsInput | undefined;

  @property({ type: Number })
  envId = 0;

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

  private saveGeneration = 0;

  private get effectiveTags(): string[] {
    const source = this.hasLiveTagsEdit ? this.liveTags : this.tags;
    return normaliseTags(source);
  }

  private onTagsChanged(event: CustomEvent<{ tags?: string[] }>) {
    this.saveGeneration++;
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
    const tokens = Array.from(
      new Set(this.effectiveTags.map(tag => formatServerTagToken(tag)))
    );
    return html`
      <div
        class="token-hints"
        role="region"
        aria-label="Exposed server tag tokens"
      >
        <span class="token-hints-label">
          Deployment variable token${tokens.length === 1 ? '' : 's'} exposed by
          server tags:
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
                >Add a tag (e.g. <code>STP Strategies</code>) to expose
                <code>$ServerNames_STP_Strategies$</code> in environment
                variables.</span
              >`
        }
      </div>
    `;
  }

  render() {
    return html`
      <tags-input
        id="tag-input"
        .tags="${this.hasLiveTagsEdit ? this.liveTags : this.tags}"
        @tags-changed="${this.onTagsChanged}"
      ></tags-input>
      ${this.renderTokenHints()}
      <vaadin-button @click="${this.save}">Save</vaadin-button>
    `;
  }

  public setTags(server: ServerApiModel) {
    this.saveGeneration++;
    this._server = server;
    this.tags = splitTags(this._server?.Tags);
    this.liveTags = [...this.tags];
    this.hasLiveTagsEdit = false;
  }

  public save() {
    if (this._server !== undefined) {
      const tags = this.tagsInput?.tags;
      const normalised = normaliseTags(tags);
      const overLong = normalised.filter(t => t.length > MAX_TAG_LENGTH);
      if (overLong.length > 0) {
        Notification.show(
          `Each tag must be at most ${MAX_TAG_LENGTH} characters (too long: '${overLong[0]}')`,
          { theme: 'error', position: 'bottom-start', duration: 5000 }
        );
        return;
      }
      this._server.Tags = normalised;
      const submittedGeneration = ++this.saveGeneration;
      const submittedServerId = this._server.ServerId;

      const api = new RefDataServersApi(dorcApiConfiguration);
      const server: ServerApiModel = {};
      server.Tags = this._server.Tags;
      server.ServerId = this._server.ServerId;
      server.Name = this._server.Name;
      server.OsName = this._server.OsName;

      api
        .refDataServersPut({
          id: this._server.ServerId ?? 0,
          serverApiModel: server
        })
        .subscribe({
          next: () => {
            if (
              this._server?.ServerId !== submittedServerId ||
              this.saveGeneration !== submittedGeneration
            ) {
              return;
            }
            const oldTags = this.tags;
            const newTags = splitTags(server.Tags);
            const removed = oldTags.filter(x => !newTags.includes(x));
            let removedTags = '';
            if (removed.length > 0) {
              removedTags = `; Removed - ${removed.join(', ')}`;
            }
            console.log(removedTags);

            const added = newTags.filter(x => !oldTags.includes(x));
            let addedTags = '';
            if (added.length > 0) {
              addedTags = `; Added - ${added.join(', ')}`;
            }
            console.log(addedTags);
            this.tags = newTags;
            this.liveTags = [...newTags];
            this.hasLiveTagsEdit = false;

            Notification.show(
              `Updated Tags for server ${server.Name}${
                removedTags
              }${addedTags}`,
              {
                theme: 'success',
                position: 'bottom-start',
                duration: 5000
              }
            );
          },
          error: (err: any) => console.error(err),
          complete: () => {
            if (
              this._server?.ServerId === submittedServerId &&
              this.saveGeneration === submittedGeneration &&
              !this.hasLiveTagsEdit
            ) {
              this.dispatchEvent(
                new CustomEvent('server-tags-updated', {
                  detail: {},
                  bubbles: true,
                  composed: true
                })
              );
            }
          }
        });
    }
  }
}
