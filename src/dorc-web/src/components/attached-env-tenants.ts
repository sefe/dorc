import '@vaadin/button';
import '@vaadin/grid';
import '@vaadin/grid/vaadin-grid-column';
import '@vaadin/grid/vaadin-grid-sort-column';
import { css, LitElement, render } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '@polymer/paper-dialog';
import { EnvironmentApiModel } from '../apis/dorc-api';
import { styleMap } from 'lit/directives/style-map.js';

@customElement('attached-env-tenants')
export class AttachedEnvTenants extends LitElement {
  @property({ type: Array })
  childEnvironments: Array<EnvironmentApiModel> | undefined = [];

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

  private environmentActionsRenderer = (root: HTMLElement, _: HTMLElement, model: { item: EnvironmentApiModel }) => {
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
      // const api = new RefDataEnvironmentsDetailsApi();
      // api
      //   .refDataEnvironmentsDetailsPut({
      //     componentId: this.dbDetails.Id,
      //     component: 'database',
      //     action: 'detach',
      //     envId: this.envId
      //   })
      //   .subscribe(() => {
      //     this.fireDbDetachedEvent();
      //   });
    }
  }

  openEnvironmentDetails(environment: EnvironmentApiModel) {
    const event = new CustomEvent('open-env-detail', {
      detail: {
        Environment: environment
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }
}