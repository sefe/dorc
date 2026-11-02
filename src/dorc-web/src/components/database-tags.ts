import { LitElement, css } from 'lit';
import { customElement, property, query } from 'lit/decorators.js';
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
    this.requestUpdate('database', oldValue);
  }

  private _database: DatabaseApiModel | undefined;

  @property({ type: Array }) tags: string[] = [];

  @query('#tag-input')
  private tagsInput: TagsInput | undefined;

  static get styles() {
    return css``;
  }

  render() {
    return html`
      <tags-input id="tag-input" label="Tags" .tags="${this.tags}"></tags-input>
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
