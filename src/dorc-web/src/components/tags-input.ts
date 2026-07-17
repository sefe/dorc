import { css, LitElement, PropertyValues } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '../icons/iron-icons.js';
import '@yaireo/tagify';

@customElement('tags-input')
export class TagsInput extends LitElement {
  @property({ type: Array })
  get tags(): string[] {
    if (this.tagify !== undefined) {
      return this.tagify.value.map(tag => tag.value);
    }
    return [];
  }

  set tags(value: string[]) {
    const oldValue = this._tags;
    if (this.tagify !== undefined) {
      this.tagify.removeAllTags();
      this.tagify.addTags(value);
    }
    this._tags = value;
    this.requestUpdate('tags', oldValue);
  }

  @property({ type: String }) label = 'Tags';

  /** Optional per-tag pattern; invalid entries are rejected by Tagify. */
  @property({ type: String }) pattern: string | undefined;

  private _tags: string[] = [];

  tagify: Tagify | undefined;

  static get styles() {
    return css`
      :host {
        display: block;
      }
      :host[hidden] {
        display: none !important;
      }
      input {
        height: 36px;
        width: auto !important;
        padding-left: 0.5em;
      }
    `;
  }

  render() {
    return html`
      <link
        href="https://cdn.jsdelivr.net/npm/@yaireo/tagify/dist/tagify.css"
        rel="stylesheet"
        type="text/css"
      />

      <input />
    `;
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    const input = this.shadowRoot?.querySelector('input') as HTMLInputElement;
    this.tagify = new window.Tagify(
      input,
      this.pattern ? { pattern: new RegExp(this.pattern) } : {}
    );
    if (this.tagify !== null && this._tags.length > 0) {
      this.tagify.removeAllTags();
      this.tagify.addTags(this._tags);
    }
    // Surface chip edits to hosts that validate as the user types. Guarded:
    // test shims may not implement Tagify's event API.
    const notify = () =>
      this.dispatchEvent(
        new CustomEvent('tags-changed', { detail: { tags: this.tags } })
      );
    (this.tagify as any).on?.('add', notify);
    (this.tagify as any).on?.('remove', notify);
  }
}
