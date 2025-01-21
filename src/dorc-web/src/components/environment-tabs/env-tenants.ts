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
      .card-element__text {
        color: gray;
        margin: 4px;
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
      ${this.environment?.ParentEnvironment ? html`
        <h4 class="card-element__text">
          Parent Environment: ${this.environment?.ParentEnvironment?.EnvironmentName}
          <vaadin-button
              title="Open Environment Details for ${this.environment?.ParentEnvironment?.EnvironmentName}"
              theme="icon"
              @click="${this.openEnvironmentDetails}"
            >
              <vaadin-icon
                icon="hardware:developer-board"
                style="color: cornflowerblue"
              ></vaadin-icon>
            </vaadin-button>
          </h4>
      `: html``}
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

  private openEnvironmentDetails() {
    const event = new CustomEvent('open-env-detail', {
      detail: {
        Environment: this.environment?.ParentEnvironment,
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }
}