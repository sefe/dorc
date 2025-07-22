import { css, LitElement } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import { customElement, property } from 'lit/decorators.js';
import './dorc-icon.js';
import { html } from 'lit/html.js';
import { EnvironmentApiModel } from '../apis/dorc-api';
import { RefDataProjectEnvironmentMappingsApi } from '../apis/dorc-api';
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

        <div style="right: 8px; bottom: 8px; position: absolute;">
          <vaadin-vertical-layout style="gap: 8px; align-items: end;">
            <vaadin-horizontal-layout style="gap: 8px;">
              <vaadin-button
                title="Environment Details"
                theme="icon"
                @click="${this.openEnvironmentDetails}"
                style="margin: 0;"
              >
                <dorc-icon icon="environment" color="primary"></dorc-icon>
              </vaadin-button>
              <vaadin-button
                title="Environment History"
                theme="icon"
                ?disabled="${this.environment === undefined}"
                @click="${this.openEnvHistory}"
                style="margin: 0;"
              >
                <dorc-icon icon="history" color="primary"></dorc-icon>
              </vaadin-button>
            </vaadin-horizontal-layout>
            <vaadin-horizontal-layout style="gap: 8px;">
              <vaadin-button
                title="Detach Environment"
                theme="icon"
                @click="${this.removeMapping}"
                .env="${this.environment}"
                style="margin: 0;"
              >
                <dorc-icon icon="unlink" color="#FF3131"></dorc-icon>
              </vaadin-button>
              <vaadin-button
                title="Access Control..."
                theme="icon"
                @click="${this.openAccessControl}"
                style="margin: 0;"
              >
                <dorc-icon icon="lock" color="primary"></dorc-icon>
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
