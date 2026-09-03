import { css, LitElement } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../../icons/hardware-icons.js';
import { EnvironmentApiModel } from '../../apis/dorc-api';
import { AccessControlType } from '../../apis/dorc-api';
import { RefDataEnvironmentsApi } from '../../apis/dorc-api';
import '@vaadin/tooltip';
import { dorcApiConfiguration } from '../../services/dorc-api-configuration';

@customElement('env-controls')
export class EnvControls extends LitElement {
  @property({ type: Object }) envDetails: EnvironmentApiModel | undefined;

  @property({ type: Boolean }) isAdmin = false;

  @property({ type: Boolean }) isPowerUser = false;

  @state() private isOwner = false;

  private ownerCheckDone = false;

  static get styles() {
    return css`
      :host {
        display: inline-flex;
        align-items: center;
        flex-wrap: nowrap;
        gap: var(--lumo-space-xs);
      }
      vaadin-button {
        padding: 0px;
        margin: 0px;
      }
    `;
  }

  protected updated(changedProperties: Map<string, unknown>) {
    super.updated(changedProperties);
    // Reset owner check when environment changes (grid row recycling)
    if (changedProperties.has('envDetails')) {
      this.ownerCheckDone = false;
      this.isOwner = false;
    }
    // Only check ownership if not admin/poweruser and we haven't checked yet
    if (
      !this.isAdmin &&
      !this.isPowerUser &&
      !this.ownerCheckDone &&
      this.envDetails?.EnvironmentName
    ) {
      this.ownerCheckDone = true;
      const api = new RefDataEnvironmentsApi(dorcApiConfiguration);
      api
        .refDataEnvironmentsIsEnvironmentOwnerGet({
          envName: this.envDetails.EnvironmentName
        })
        .subscribe({
          next: (value: boolean) => {
            this.isOwner = value;
          },
          error: (err: unknown) => console.error('Owner check failed:', err)
        });
    }
  }

  render() {
    return html`
      <vaadin-button
        aria-label="Environment Access..."
        theme="icon"
        @click="${this.openAccessControl}"
      >
        <vaadin-tooltip
          slot="tooltip"
          text="Environment Access..."
        ></vaadin-tooltip>
        <vaadin-icon
          icon="vaadin:lock"
          style="color: var(--dorc-link-color)"
        ></vaadin-icon>
      </vaadin-button>
      ${
        this.isAdmin || this.isPowerUser || this.isOwner
          ? html`<vaadin-button
              aria-label="Clone Environment..."
              theme="icon"
              @click="${this.cloneEnvironment}"
            >
              <vaadin-tooltip
                slot="tooltip"
                text="Clone Environment..."
              ></vaadin-tooltip>
              <vaadin-icon
                icon="vaadin:copy-o"
                style="color: var(--dorc-link-color)"
              ></vaadin-icon>
            </vaadin-button>`
          : html``
      }
      <vaadin-button
        aria-label="Environment Details"
        theme="icon"
        @click="${this.openEnvironmentDetails}"
      >
        <vaadin-tooltip
          slot="tooltip"
          text="Environment Details"
        ></vaadin-tooltip>
        <vaadin-icon
          icon="hardware:developer-board"
          style="color: var(--dorc-link-color)"
        ></vaadin-icon>
      </vaadin-button>
    `;
  }

  openAccessControl() {
    const event = new CustomEvent('open-access-control', {
      detail: {
        Name: this.envDetails?.EnvironmentName,
        Type: AccessControlType.Environment
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }

  cloneEnvironment() {
    const event = new CustomEvent('clone-environment', {
      detail: {
        Environment: this.envDetails
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }

  openEnvironmentDetails() {
    const event = new CustomEvent('open-env-detail', {
      detail: {
        Environment: this.envDetails
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }
}
