import '@polymer/paper-toggle-button';
import '@vaadin/details';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../attached-env-tenants';
import '../add-env-tenant';
import { PageEnvBase } from './page-env-base.ts';
import { css } from 'lit';

@customElement('env-tenants')
export class EnvTenants extends PageEnvBase {
  @property({ type: Boolean }) private envReadOnly = false;
  @property({ type: Boolean }) addTenant = false;
  
  static get styles() {
    return css`
      :host {
        width: 100%;
      }
      .inline {
        display: inline-block;
        vertical-align: middle;
      }
      .buttons {
        font-size: 10px;
        color: cornflowerblue;
        padding: 2px;
      }
    `;
  }

  constructor() {
    super();
    this.addEventListener('request-environment-update', this.forceLoadEnvironmentInfo);
    super.loadEnvironmentInfo();
  }

  _addTenant() {
    this.addTenant = !this.addTenant;
  }

  render() {
    return html`
      <vaadin-details
        opened
        summary="Environment tenants"
        style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px"
      >
        <div class="inline">
          <paper-toggle-button
            class="buttons"
            id="addTenant"
            .checked="${this.addTenant}"
            @click="${this._addTenant}"
            .disabled="${this.envReadOnly}"
            >ATTACH
          </paper-toggle-button>
        </div>
        ${this.addTenant ? html`
          <add-env-tenant .parentEnvironment="${this.environment}"></add-env-tenant>
        `: html``}
        <attached-env-tenants 
          .childEnvironments="${this.environment?.ChildEnvironments}"
          .readonly="${this.envReadOnly}"
        ></attached-env-tenants>
      </vaadin-details>
    `;
  }

  override notifyEnvironmentReady() {
    this.envReadOnly = !this.environment?.UserEditable;
  }
}