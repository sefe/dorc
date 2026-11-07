import { LitElement, css } from 'lit';
import { customElement, property, query } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@vaadin/button';
import { Notification } from '@vaadin/notification';
import { normaliseTags } from '../helpers/tag-parser';
import { MAX_TAG_LENGTH } from '../helpers/tag-limits';
import './tags-input';
import { TagsInput } from './tags-input';

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
    this.requestUpdate('tags', oldValue);
  }

  private _tags: string[] = [];

  @query('#tag-input')
  private tagsInput: TagsInput | undefined;

  static get styles() {
    return css``;
  }

  render() {
    return html`
      <tags-input
        id="tag-input"
        label="Tags"
        .tags="${this._tags}"
      ></tags-input>
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
