import { css, LitElement } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { EnvironmentApiModel } from '../apis/dorc-api';
import { RefDataProjectEnvironmentMappingsApi } from '../apis/dorc-api';
import '../icons/hardware-icons.js';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '../icons/iron-icons.js';
import { AccessControlType } from '../apis/dorc-api';

@customElement('environment-card')
export class EnvironmentCard extends LitElement {
  @property({ type: Object }) environment: EnvironmentApiModel | undefined;

  @property({ type: String })
  project: string | undefined;

  private envHistoryUriStart = '/env-history?id=';

  static get styles() {
    return css`
      .card-element {
        padding: 10px;
        box-shadow: 1px 2px 3px rgba(0, 0, 0, 0.2);
        width: 300px;
        height: 126px;
        position: relative;
      }
      .card-element__heading {
        color: #ff3131;
      }
      .card-element__text {
        color: gray;
        width: 200px;
        word-wrap: break-word;
        display: block;
        font-size: small;
      }
      .statistics-cards {
        max-width: 500px;
        display: flex;
        flex-wrap: wrap;
      }
      .statistics-cards__item {
        margin: 5px;
        flex-shrink: 0;
      }
      a {
        color: blue;
        text-decoration: none; /* no underline */
      }
      vaadin-button {
        margin: 4px;
      }
    `;
  }

  render() {
    return html`
      <div class="statistics-cards__item card-element">
        <div style="position: absolute; left: 10px; max-width: 250px">
          <h3 style="margin: 0px">${this.environment?.EnvironmentName}</h3>
          <span class="card-element__text"
            >${this.environment?.Details?.Description}</span
          >
          <span class="card-element__text"
            >${this.environment?.Details?.EnvironmentOwner}</span
          >
        </div>

        <div style="left: 200px; top: 40px; position: relative">
          <vaadin-vertical-layout>
            <vaadin-horizontal-layout>
              <vaadin-button
                title="Environment Details"
                theme="icon"
                @click="${this.openEnvironmentDetails}"
              >
                <vaadin-icon
                  icon="hardware:developer-board"
                  style="color: cornflowerblue"
                ></vaadin-icon>
              </vaadin-button>
              <vaadin-button
                title="Environment History"
                theme="icon"
                ?disabled="${this.environment === undefined}"
                @click="${this.openEnvHistory}"
              >
                <vaadin-icon
                  icon="icons:history"
                  style="color: cornflowerblue"
                ></vaadin-icon>
              </vaadin-button>
            </vaadin-horizontal-layout>
            <vaadin-horizontal-layout>
              <vaadin-button
                title="Detach Environment"
                theme="icon"
                @click="${this.removeMapping}"
                .env="${this.environment}"
              >
                <vaadin-icon
                  icon="vaadin:unlink"
                  style="color: #FF3131"
                ></vaadin-icon>
              </vaadin-button>
              <vaadin-button
                title="Access Control..."
                theme="icon"
                @click="${this.openAccessControl}"
              >
                <vaadin-icon
                  icon="vaadin:lock"
                  style="color: cornflowerblue"
                ></vaadin-icon>
              </vaadin-button>
            </vaadin-horizontal-layout>
          </vaadin-vertical-layout>
        </div>
      </div>
    `;
  }

  openEnvHistory() {
    window.open(
      this.envHistoryUriStart + (this.environment?.EnvironmentId ?? 0)
    );
  }

  openAccessControl() {
    const event = new CustomEvent('open-access-control', {
      detail: {
        Name: this.environment?.EnvironmentName,
        Type: AccessControlType.NUMBER_1
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }

  openEnvironmentDetails() {
    const event = new CustomEvent('open-env-detail', {
      detail: {
        Environment: this.environment
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }

  removeMapping(data: any) {
    const env = data.currentTarget.env as EnvironmentApiModel;

    const api = new RefDataProjectEnvironmentMappingsApi();
    api
      .refDataProjectEnvironmentMappingsDelete({
        environment: env.EnvironmentName || '',
        project: this.project || ''
      })
      .subscribe(
        () => {
          const event = new CustomEvent('envs-changed', {
            detail: {
              message: 'Detached Environment successfully!'
            },
            bubbles: true,
            composed: true
          });
          this.dispatchEvent(event);
        },
        (err: any) => console.error(err),
        () => console.log('done removing environment')
      );
  }
}
