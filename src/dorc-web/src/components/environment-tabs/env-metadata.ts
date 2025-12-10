import { css, PropertyValues } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/details';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PageEnvBase } from './page-env-base';
import '../add-edit-environment';
import '../attached-delegated-users';
import { UserApiModel } from '../../apis/dorc-api';

@customElement('env-metadata')
export class EnvMetadata extends PageEnvBase {
  @property({ type: Boolean }) loading = true;
  @property({ type: String }) envName: string | undefined = '';
  @property({ type: Array }) delegatedUsers: Array<UserApiModel> | undefined;
  @state() delegatedUsersLoaded = false;

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
      
      <!-- Delegated Users Section -->
      <vaadin-details ?hidden="${this.loading}" ?opened="${true}">
        <vaadin-details-summary slot="summary">
          <div style="font-weight: bold;">Delegated Users</div>
        </vaadin-details-summary>
        <attached-delegated-users
          id="delegated-users"
          .readonly="${!this.environment?.UserEditable}"
          .envName="${this.envName}"
          .users="${this.delegatedUsers}"
          .delegatedUsersLoading="${!this.delegatedUsersLoaded}"
          style="height: 300px; margin-top: 10px;"
        ></attached-delegated-users>
      </vaadin-details>
    `;
  }

  notifyEnvironmentContentReady() {
    this.loading = false;
    this.delegatedUsers = this.envContent?.DelegatedUsers?.sort(this.sortEnvs);
    this.delegatedUsersLoaded = true;
  }

  sortEnvs(a: UserApiModel, b: UserApiModel): number {
    if (String(a.DisplayName) > String(b.DisplayName)) return 1;
    return -1;
  }

  notifyEnvironmentReady() {
    this.envName =
      this.environment?.EnvironmentName !== null
        ? this.environment?.EnvironmentName
        : '';
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener(
      'environment-details-updated',
      this.environmentDetailsUpdated as EventListener
    );
    
    this.addEventListener(
      'delegated-users-changed',
      this.delegatedUsersChanged as EventListener
    );
  }

  delegatedUsersChanged() {
    if (this.environment) {
      this.refreshEnvDetails(this.environment);
    }
  }

  environmentDetailsUpdated() {
    this.forceLoadEnvironmentInfo();
  }

  connectedCallback() {
    super.connectedCallback?.();

    super.loadEnvironmentInfo();
  }
}
