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

@customElement('env-controls')
export class EnvControls extends LitElement {
  @property({ type: Object }) envDetails: EnvironmentApiModel | undefined;

  @property({ type: Boolean }) isAdmin = false;

  @property({ type: Boolean }) isPowerUser = false;

  @state() private isOwner = false;

  private ownerCheckDone = false;

  static get styles() {
    return css`
      vaadin-button {
        padding: 0px;
        margin: 0px;
      }
    `;
  }

  protected updated(changedProperties: Map<string, unknown>) {
    super.updated(changedProperties);
    // Only check ownership if not admin/poweruser and we haven't checked yet
    if (
      !this.isAdmin &&
      !this.isPowerUser &&
      !this.ownerCheckDone &&
      this.envDetails?.EnvironmentName
    ) {
      this.ownerCheckDone = true;
      const api = new RefDataEnvironmentsApi();
      api
        .refDataEnvironmentsIsEnvironmentOwnerOrDelegateGet({
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
        title="Environment Access..."
        theme="icon"
        @click="${this.openAccessControl}"
      >
        <vaadin-icon
          icon="vaadin:lock"
          style="color: cornflowerblue"
        ></vaadin-icon>
      </vaadin-button>
      ${this.isAdmin || this.isPowerUser || this.isOwner
        ? html`<vaadin-button
            title="Clone Environment..."
            theme="icon"
            @click="${this.cloneEnvironment}"
          >
            <vaadin-icon
              icon="vaadin:copy-o"
              style="color: cornflowerblue"
            ></vaadin-icon>
          </vaadin-button>`
        : html``}
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
    `;
  }

  openAccessControl() {
    const event = new CustomEvent('open-access-control', {
      detail: {
        Name: this.envDetails?.EnvironmentName,
        Type: AccessControlType.NUMBER_1
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
