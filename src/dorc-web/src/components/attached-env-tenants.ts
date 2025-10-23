import '@vaadin/button';
import '@vaadin/grid';
import '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import { css, LitElement, render } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@polymer/paper-dialog';
import { Notification } from '@vaadin/notification';
import {
  ApiBoolResult,
  EnvironmentApiModel,
  RefDataEnvironmentsDetailsApi
} from '../apis/dorc-api';
import { styleMap } from 'lit/directives/style-map.js';
import { EnvPageTabNames } from '../pages/page-environment';

@customElement('attached-env-tenants')
export class AttachedEnvTenants extends LitElement {
  @property({ type: Array })
  childEnvironments: Array<EnvironmentApiModel> | null | undefined = [];

  @property({ type: Boolean })
  readonly: boolean = false;

  static get styles() {
    return css`
      vaadin-grid {
        width: 100%;
        height: auto;
      }
    `;
  }

  render() {
    return html`
      <vaadin-grid
        .items=${this.childEnvironments}
        theme="compact row-stripes no-row-borders no-border"
        all-rows-visible
      >
        <vaadin-grid-column
          path="EnvironmentName"
          header="Tenant Environment Name"
        ></vaadin-grid-column>
        <vaadin-grid-column
          header="Actions"
          .renderer="${this.environmentActionsRenderer}"
        ></vaadin-grid-column>
      </vaadin-grid>
    `;
  }

  private environmentActionsRenderer = (
    root: HTMLElement,
    _: HTMLElement,
    model: { item: EnvironmentApiModel }
  ) => {
    const unlinkStyles = {
      color: this.readonly ? 'grey' : '#FF3131'
    };
    const environment = model.item;

    render(
      html`
        <vaadin-button
          title="Open Environment Details for ${environment?.EnvironmentName}"
          theme="icon"
          @click="${() => this.openEnvironmentDetails(environment)}"
        >
          <vaadin-icon
            icon="hardware:developer-board"
            style="color: cornflowerblue"
          ></vaadin-icon>
        </vaadin-button>
        <vaadin-button
          title="Detach tenant"
          theme="icon"
          @click="${() => this.detachTenant(environment?.EnvironmentId)}"
          ?disabled="${this.readonly}"
        >
          <vaadin-icon
            icon="vaadin:unlink"
            style=${styleMap(unlinkStyles)}
          ></vaadin-icon>
        </vaadin-button>
      `,
      root
    );
  };

  detachTenant(envId: number | undefined) {
    const answer = confirm('Detach tenant?');
    if (answer && envId) {
      const api = new RefDataEnvironmentsDetailsApi();
      api
        .refDataEnvironmentsDetailsSetParentForEnvironmentPut({
          childEnvId: envId,
          parentEnvId: undefined
        })
        .subscribe({
          next: (data: ApiBoolResult) => {
            if (data.Result) {
              this.dispatchEvent(
                new CustomEvent('request-environment-update', {
                  bubbles: true,
                  composed: true
                })
              );

              Notification.show(
                `Tenant environment with ID ${envId} has beed detached.`,
                {
                  theme: 'success',
                  position: 'bottom-start',
                  duration: 3000
                }
              );
            } else {
              this.onError(
                `Detach environment with ID ${envId} from parent has failed: ${data.Message}`
              );
            }
          },
          error: (err: string) => {
            this.onError(`Unable to set parent for environment: ${err}`);
          }
        });
    }
  }

  openEnvironmentDetails(environment: EnvironmentApiModel) {
    const event = new CustomEvent('open-env-detail', {
      detail: {
        Environment: environment,
        Tab: EnvPageTabNames.Tenants
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }

  private onError(message: string) {
    const event = new CustomEvent('error-alert', {
      detail: {
        description: message
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);

    console.error(message);
  }
}
