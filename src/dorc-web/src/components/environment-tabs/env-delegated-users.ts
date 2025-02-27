import { css, PropertyValues } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PageEnvBase } from './page-env-base';
import '@vaadin/details';
import '../attached-delegated-users';
import { UserApiModel } from '../../apis/dorc-api';

@customElement('env-delegated-users')
export class EnvDelegatedUsers extends PageEnvBase {
  @property({ type: String }) envName: string | undefined = '';

  @property({ type: Array }) delegatedUsers: Array<UserApiModel> | undefined;

  @state() delegatedUsersLoaded = false;

  static get styles() {
    return css`
      :host {
        width: 100%;
      }
    `;
  }

  render() {
    return html`
      <vaadin-details
        opened
        summary="Application Users with Delegated Privileges"
        style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px; margin: 0px;"
      >
        <attached-delegated-users
          id="delegated-users"
          .readonly="${!this.environment?.UserEditable}"
          .envName="${this.envName}"
          .users="${this.delegatedUsers}"
          .delegatedUsersLoading="${!this.delegatedUsersLoaded}"
        >
        </attached-delegated-users>
      </vaadin-details>
    `;
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

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

  constructor() {
    super();

    super.loadEnvironmentInfo();
  }

  notifyEnvironmentContentReady() {
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
        : undefined;
  }
}
