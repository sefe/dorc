import { css, PropertyValues } from 'lit';
import '../dorc-spinner';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PageEnvBase } from './page-env-base';
import '../add-edit-environment';
import './env-control-center';
import type { EnvControlCenter } from './env-control-center';

@customElement('env-metadata')
export class EnvMetadata extends PageEnvBase {
  @property({ type: Boolean }) loading = true;

  static get styles() {
    return css`
      :host {
        width: 100%;
        overflow-y: auto;
      }
      }
    `;
  }

  render() {
    return html`
      <dorc-spinner ?hidden="${!this.loading}"></dorc-spinner>
      <env-control-center ?hidden="${this.loading}"></env-control-center>
      <add-edit-environment
        .readonly="${!this.environment?.UserEditable}"
        ?hidden="${this.loading}"
        .environment="${this.environment}"
        .addMode="${false}"
      ></add-edit-environment>
    `;
  }

  notifyEnvironmentReady() {
    const cc = this.shadowRoot?.querySelector(
      'env-control-center'
    ) as EnvControlCenter | null;
    if (cc) cc.notifyEnvironmentReady();
  }

  notifyEnvironmentContentReady() {
    this.loading = false;
    const cc = this.shadowRoot?.querySelector(
      'env-control-center'
    ) as EnvControlCenter | null;
    if (cc) cc.notifyEnvironmentContentReady();
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener(
      'environment-details-updated',
      this.environmentDetailsUpdated as EventListener
    );

    super.loadEnvironmentInfo();
  }

  environmentDetailsUpdated() {
    this.forceLoadEnvironmentInfo();
  }

  connectedCallback() {
    super.connectedCallback?.();
  }
}
