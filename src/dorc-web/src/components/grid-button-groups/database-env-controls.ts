import { css, LitElement } from 'lit';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { styleMap } from 'lit/directives/style-map.js';
import {
  DatabaseApiModel,
  RefDataEnvironmentsDetailsApi
} from '../../apis/dorc-api';
import '../../icons/social-icons.js';

@customElement('database-env-controls')
export class DatabaseEnvControls extends LitElement {
  @property({ type: Object }) dbDetails: DatabaseApiModel | undefined;

  @property({ type: Number })
  envId = 0;

  @property({ type: Boolean }) private readonly = true;

  static get styles() {
    return css`
      vaadin-button {
        padding: 0px;
        margin: 0px;
      }
    `;
  }

  render() {
    const unlinkStyles = {
      color: this.readonly ? 'grey' : '#FF3131'
    };
    const editStyles = {
      color: this.readonly ? 'grey' : 'cornflowerblue'
    };
    return html`
      <vaadin-button
        title="Detach database"
        theme="icon"
        @click="${this.detailedResults}"
        ?disabled="${this.readonly}"
      >
        <vaadin-icon
          icon="vaadin:unlink"
          style=${styleMap(unlinkStyles)}
        ></vaadin-icon>
      </vaadin-button>
      <vaadin-button
        title="Manage permissions"
        theme="icon"
        @click="${this.manage}"
        ?disabled="${this.readonly}"
      >
        <vaadin-icon
          icon="social:group-add"
          style=${styleMap(editStyles)}
        ></vaadin-icon>
      </vaadin-button>
      <vaadin-button
        title="View database permissions"
        theme="icon"
        @click="${this.view}"
      >
        <vaadin-icon
          icon="social:group"
          style="color: cornflowerblue"
        ></vaadin-icon>
      </vaadin-button>
    `;
  }

  detailedResults() {
    const answer = confirm('Detach database?');
    if (answer && this.dbDetails?.Id) {
      const api = new RefDataEnvironmentsDetailsApi();
      api
        .refDataEnvironmentsDetailsPut({
          componentId: this.dbDetails.Id,
          component: 'database',
          action: 'detach',
          envId: this.envId
        })
        .subscribe(() => {
          this.fireDbDetachedEvent();
        });
    }
  }

  manage() {
    this.fireManageDbPerms();
  }

  view() {
    this.fireViewDbPerms();
  }

  private fireDbDetachedEvent() {
    const event = new CustomEvent('database-detached', {
      detail: {
        message: 'Database detached successfully!'
      }
    });
    this.dispatchEvent(event);
  }

  private fireManageDbPerms() {
    const event = new CustomEvent('manage-database-perms', {
      detail: {
        message: ''
      }
    });
    this.dispatchEvent(event);
  }

  private fireViewDbPerms() {
    const event = new CustomEvent('view-database-perms', {
      detail: {
        message: ''
      }
    });
    this.dispatchEvent(event);
  }
}
