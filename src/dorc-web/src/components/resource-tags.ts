import { LitElement, css } from 'lit';
import { customElement, property, query, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/button';
import { Notification } from '@vaadin/notification';
import { normaliseTags } from '../helpers/tag-parser';
import { MAX_TAG_LENGTH } from '../helpers/tag-limits';
import './tags-input';
import { TagsInput } from './tags-input';

export function formatResourceTagToken(prefix: string, tag: string): string {
  const trimmed = tag.trim();
  if (!trimmed) return '';
  const normalized = trimmed.replace(/ /g, '_');
  if (!/^[A-Za-z0-9_.-]+$/.test(normalized)) return '';
  return `$${prefix}${normalized}$`;
}

export function resolveResourceTagTokens(
  prefix: string,
  tags: string[]
): { tokens: string[]; conflicts: string[] } {
  const tagsByToken = new Map<string, string[]>();
  for (const tag of tags) {
    const token = formatResourceTagToken(prefix, tag);
    if (!token) continue;
    const key = token.toLowerCase();
    tagsByToken.set(key, [...(tagsByToken.get(key) ?? []), tag]);
  }
  const tokens: string[] = [];
  const conflicts: string[] = [];
  for (const group of tagsByToken.values()) {
    if (group.length > 1) {
      conflicts.push(group.map(t => `'${t}'`).join(' and '));
    } else {
      tokens.push(formatResourceTagToken(prefix, group[0]));
    }
  }
  return { tokens, conflicts };
}

/**
 * Generic tag editor for environment component resources (APIs, containers,
 * cloud resources). Mirrors the UX of `database-tags`, but leaves persistence
 * to the host: on Save it validates the tag set and fires `resource-tags-save`
 * with `detail.tags` (normalised string array).
 */
@customElement('resource-tags')
export class ResourceTags extends LitElement {
  @property({ attribute: false })
  get tags(): string[] {
    return this._tags;
  }

  set tags(value: string[] | string | null | undefined) {
    const oldValue = this._tags;
    this._tags = normaliseTags(value);
    this.liveTags = [...this._tags];
    this.hasLiveTagsEdit = false;
    this.requestUpdate('tags', oldValue);
  }

  private _tags: string[] = [];

  @property({ type: String }) tokenPrefix = '';

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
    const source = this.hasLiveTagsEdit ? this.liveTags : this._tags;
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
    if (!this.tokenPrefix) return '';
    const { tokens, conflicts } = resolveResourceTagTokens(
      this.tokenPrefix,
      this.effectiveTags
    );
    return html`
      <div
        class="token-hints"
        role="region"
        aria-label="Exposed component tag tokens"
      >
        <span class="token-hints-label">
          Built-in variable token${tokens.length === 1 ? '' : 's'} exposed by
          tags (for use in Variable Values):
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
                >Add a tag (e.g. <code>edge</code>) to expose
                <code>$${this.tokenPrefix}edge$</code> in environment
                variables.</span
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
        .tags="${this.hasLiveTagsEdit ? this.liveTags : this._tags}"
        @tags-changed="${this.onTagsChanged}"
      ></tags-input>
      ${this.renderTokenHints()}
      <vaadin-button @click="${this.save}">Save</vaadin-button>
    `;
  }

  public save() {
    const tags = normaliseTags(this.tagsInput?.tags);
    const overLongTag = tags.find(tag => tag.length > MAX_TAG_LENGTH);
    if (overLongTag) {
      Notification.show(
        `Each tag must be at most ${MAX_TAG_LENGTH} characters (too long: '${overLongTag}')`,
        { theme: 'error', position: 'bottom-start', duration: 5000 }
      );
      return;
    }

    this.dispatchEvent(
      new CustomEvent('resource-tags-save', {
        detail: { tags },
        bubbles: true,
        composed: true
      })
    );
  }
}
