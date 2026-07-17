import { css, LitElement } from 'lit';
import './tags-input';
import { customElement, property, query } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { Notification } from '@vaadin/notification';
import { TagsInput } from './tags-input';
import { RefDataDatabasesApi } from '../apis/dorc-api';
import { DatabaseApiModel } from '../apis/dorc-api';
import { splitTags, joinTags } from '../helpers/tag-parser';
import { MAX_TAG_STRING_LENGTH } from '../helpers/tag-limits';

/**
 * Chip-style tag editing for a database's tags (stored in `ArrayName` — verified a
 * pure pass-through field; docs/tag-capacity-expansion U-3). Mirrors server-tags.ts,
 * including the joined-string capacity guard.
 */
@customElement('database-tags')
export class DatabaseTags extends LitElement {
  @property({ type: Object })
  get database(): DatabaseApiModel | undefined {
    return this._database;
  }

  set database(value: DatabaseApiModel | undefined) {
    const oldVal = this._database;
    this._database = value;
    this.setTags(value ?? {});
    this.requestUpdate('database', oldVal);
  }

  private _database: DatabaseApiModel | undefined;

  @property({ type: Array }) private tags: string[] = [];

  @query('#tag-input')
  private tagsInput: TagsInput | undefined;

  static get styles() {
    return css``;
  }

  render() {
    return html`
      <tags-input id="tag-input" .tags="${this.tags}"></tags-input>
      <vaadin-button @click="${this.save}">Save</vaadin-button>
    `;
  }

  public setTags(database: DatabaseApiModel) {
    this._database = database;
    this.tags = splitTags(this._database?.ArrayName);
  }

  public save() {
    if (this._database !== undefined) {
      const tags = this.tagsInput?.tags;
      const joined = joinTags(tags);
      if (joined.length > MAX_TAG_STRING_LENGTH) {
        Notification.show(
          `Tags must be at most ${MAX_TAG_STRING_LENGTH} characters when joined (currently ${joined.length})`,
          { theme: 'error', position: 'bottom-start', duration: 5000 }
        );
        return;
      }
      this._database.ArrayName = joined;

      const api = new RefDataDatabasesApi();
      const database: DatabaseApiModel = {
        Id: this._database.Id,
        Name: this._database.Name,
        Type: this._database.Type,
        ServerName: this._database.ServerName,
        AdGroup: this._database.AdGroup,
        ArrayName: this._database.ArrayName
      };

      api
        .refDataDatabasesPut({
          id: this._database.Id ?? 0,
          databaseApiModel: database
        })
        .subscribe({
          next: () => {
            Notification.show(`Updated tags for database ${database.Name}`, {
              theme: 'success',
              position: 'bottom-start',
              duration: 5000
            });
          },
          error: (err: any) => console.error(err),
          complete: () => {
            this.dispatchEvent(
              new CustomEvent('database-tags-updated', {
                detail: {},
                bubbles: true,
                composed: true
              })
            );
          }
        });
    }
  }
}
