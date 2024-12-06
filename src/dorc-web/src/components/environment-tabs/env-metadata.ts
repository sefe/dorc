import { css, PropertyValues } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PageEnvBase } from './page-env-base';
import '../add-edit-environment';

@customElement('env-metadata')
export class EnvMetadata extends PageEnvBase {
  @property({ type: Boolean }) loading = true;

  static get styles() {
    return css`
      :host {
        width: 100%;
        overflow: hidden;
      }
      .overlay {
        width: 100%;
        height: 100%;
        position: fixed;
      }
      .overlay__inner {
        width: 100%;
        height: 100%;
        position: absolute;
      }
      .overlay__content {
        left: 20%;
        position: absolute;
        top: 20%;
        transform: translate(-50%, -50%);
      }
      .spinner {
        width: 75px;
        height: 75px;
        display: inline-block;
        border-width: 2px;
        border-color: rgba(255, 255, 255, 0.05);
        border-top-color: cornflowerblue;
        animation: spin 1s infinite linear;
        border-radius: 100%;
        border-style: solid;
      }
      @keyframes spin {
        100% {
          transform: rotate(360deg);
        }
      }
    `;
  }

  render() {
    return html`
      <div class="overlay" ?hidden="${!this.loading}">
        <div class="overlay__inner">
          <div class="overlay__content">
            <span class="spinner"></span>
          </div>
        </div>
      </div>
      <add-edit-environment
        .readonly="${!this.environment?.UserEditable}"
        ?hidden="${this.loading}"
        .environment="${this.environment}"
        .addMode="${false}"
      ></add-edit-environment>
    `;
  }

  notifyEnvironmentContentReady() {
    this.loading = false;
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener(
      'environment-details-updated',
      this.environmentDetailsUpdated as EventListener
    );
  }

  environmentDetailsUpdated() {
    this.forceLoadEnvironmentInfo();
  }

  connectedCallback() {
    super.connectedCallback?.();

    super.loadEnvironmentInfo();
  }
}
