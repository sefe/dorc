import { css, LitElement } from 'lit';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { styleMap } from 'lit/directives/style-map.js';
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
    const unlinkStyles = {
      color: this.readonly ? 'grey' : '#FF3131'
    };
    return html`
      <vaadin-button
        title="Remove Delegation"
        theme="icon"
        @click="${this.detailedResults}"
        ?disabled="${this.readonly}"
      >
        <vaadin-icon
          icon="vaadin:unlink"
          style=${styleMap(unlinkStyles)}
        ></vaadin-icon>
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
