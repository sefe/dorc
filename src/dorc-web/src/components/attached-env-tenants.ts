import { confirmPrompt } from './confirm-prompt';
import '@vaadin/button';
import '@vaadin/grid';
import '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import { columnBodyRenderer } from '@vaadin/grid/lit';
import { css, LitElement } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { Notification } from '@vaadin/notification';
import { ApiBoolResult, EnvironmentApiModel, RefDataEnvironmentsDetailsApi } from '../apis/dorc-api';
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
          ${columnBodyRenderer(this.environmentActionsRenderer, [this.readonly])}
        ></vaadin-grid-column>
      </vaadin-grid>
    `;
  }

  /**
   * Reads `readonly`, so it is declared as a dependency. Under the old plain
   * `.renderer` binding the function reference never changed, so Grid had no
   * way to know the closure had gone stale and the Detach button stayed
   * enabled after the environment resolved as read-only.
   */
  private environmentActionsRenderer = (environment: EnvironmentApiModel) => {
    const unlinkStyles = {
      color: this.readonly
        ? 'var(--dorc-text-secondary)'
        : 'var(--dorc-error-color)'
    };

    return html`
        <vaadin-button
          title="Open Environment Details for ${environment?.EnvironmentName}"
          aria-label="Open Environment Details for ${environment?.EnvironmentName}"
          theme="icon"
          @click="${() => this.openEnvironmentDetails(environment)}"
        >
          <vaadin-icon
            icon="hardware:developer-board"
            style="color: var(--dorc-link-color)"
          ></vaadin-icon>
        </vaadin-button>
        <vaadin-button
          title="Detach tenant"
          aria-label="Detach tenant"
          theme="icon"
          @click="${() => this.detachTenant(environment?.EnvironmentId)}"
          ?disabled="${this.readonly}"
        >
          <vaadin-icon
            icon="vaadin:unlink"
            style=${styleMap(unlinkStyles)}
          ></vaadin-icon>
        </vaadin-button>
    `;
  };

  async detachTenant(envId: number | undefined) {
    const answer = await confirmPrompt('Detach tenant?');
    if (answer && envId) {
      const api = new RefDataEnvironmentsDetailsApi();
      api.refDataEnvironmentsDetailsSetParentForEnvironmentPut({
        childEnvId: envId,
        parentEnvId: undefined
      })
        .subscribe({
          next: (data: ApiBoolResult) => {
            if (data.Result) {
              this.dispatchEvent(new CustomEvent('request-environment-update', {
                bubbles: true,
                composed: true
              }));

              Notification.show(`Tenant environment with ID ${envId} has beed detached.`, {
                theme: 'success',
                position: 'bottom-start',
                duration: 3000
              });
            }
            else {
              this.onError(`Detach environment with ID ${envId} from parent has failed: ${data.Message}`);
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