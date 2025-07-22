import { css, LitElement } from 'lit';
import '@vaadin/button';
import { customElement, property } from 'lit/decorators.js';
import '../dorc-icon.js';
import { html } from 'lit/html.js';
import { DelegatedUsersApi, UserApiModel } from '../../apis/dorc-api';

@customElement('delegated-user-controls')
export class DelegatedUserControls extends LitElement {
  @property({ type: Object }) user: UserApiModel | undefined;

  @property() envName: string | undefined;

  @property({ type: Number })
  envId = 0;

  @property({ type: Boolean }) private readonly = true;

  static get styles() {
    return css`
      vaadin-button {
        padding: 0px;
        margin: 0px;
      }
      vaadin-button:disabled,
      vaadin-button[disabled] {
        background-color: #dde2e8;
      }
    `;
  }

  render() {
    return html`
      <vaadin-button
        title="Remove Delegation"
        theme="icon"
        @click="${this.detailedResults}"
        ?disabled="${this.readonly}"
      >
        <dorc-icon icon="unlink"></dorc-icon>
      </vaadin-button>
    `;
  }

  detailedResults() {
    const answer = confirm(`Remove Delegation for ${this.user?.DisplayName}?`);
    if (answer && this.user?.Id) {
      const api = new DelegatedUsersApi();
      api
        .delegatedUsersDelete({
          userId: this.user.Id,
          envName: this.envName ?? ''
        })
        .subscribe(
          () => {
            const event = new CustomEvent('delegated-users-changed', {
              detail: {},
              bubbles: true,
              composed: true
            });
            this.dispatchEvent(event);
          },
          (err: any) => {
            console.error(err);
            alert(err.response);
          }
        );
    }
  }
}
