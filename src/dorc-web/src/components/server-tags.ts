import { css, LitElement } from 'lit';
import './tags-input';
import { customElement, property, query } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { Notification } from '@vaadin/notification';
import { TagsInput } from './tags-input';
import { RefDataServersApi } from '../apis/dorc-api';
import { ServerApiModel } from '../apis/dorc-api';
import { splitTags, joinTags } from '../utils/tag-utils';

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

  @property({ type: Array }) private tags: string[] = [];

  @query('#tag-input')
  private tagsInput: TagsInput | undefined;

  @property({ type: Number })
  envId = 0;

  static get styles() {
    return css``;
  }

  render() {
    return html`
      <tags-input id="tag-input" .tags="${this.tags}"></tags-input>
      <vaadin-button @click="${this.save}">Save</vaadin-button>
    `;
  }

  public setTags(server: ServerApiModel) {
    this._server = server;
    this.tags = splitTags(this._server?.ApplicationTags);
  }

  public save() {
    if (this._server !== undefined) {
      const tags = this.tagsInput?.tags;
      this._server.ApplicationTags = joinTags(tags);

      const api = new RefDataServersApi();
      const server: ServerApiModel = {};
      server.ApplicationTags = this._server.ApplicationTags;
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
            const oldTags = this.tags;
            const newTags = splitTags(server.ApplicationTags);
            const removed = oldTags?.filter(x => !newTags?.includes(x));
            let removedTags = '';
            if (removed.length > 0) {
              removedTags = `; Removed - ${removed?.join(', ')}`;
            }
            console.log(removedTags);

            const added = newTags?.filter(x => !oldTags.includes(x)) ?? [];
            let addedTags = '';
            if (added.length > 0) {
              addedTags = `; Added - ${added?.join(', ')}`;
            }
            console.log(addedTags);

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
            this.dispatchEvent(
              new CustomEvent('server-tags-updated', {
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
