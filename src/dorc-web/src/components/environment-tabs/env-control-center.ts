import { css, PropertyValues } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/vaadin-lumo-styles/icons.js';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { Notification } from '@vaadin/notification';
import {
  AccessControlType,
  DatabaseApiModel,
  RefDataEnvironmentsApi
} from '../../apis/dorc-api';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/details';
import '../make-like-production-dialog';
import '@polymer/paper-dialog';
import '../add-edit-environment';
import { PageEnvBase } from './page-env-base';
import { AddEditAccessControl } from '../add-edit-access-control';
import GlobalCache from '../../global-cache';
import { ResetAppPasswordBehalf } from '../reset-app-password-behalf';
import '../reset-app-password-behalf';
import '../../icons/iron-icons.js';
import { SuccessNotification } from '../notifications/success-notification';
import { MakeLikeProductionDialog } from '../make-like-production-dialog.ts';

@customElement('env-control-center')
export class EnvControlCenter extends PageEnvBase {
  get userRoles(): string[] {
    return this._userRoles;
  }

  set userRoles(value: string[]) {
    this._userRoles = value;
    if (this._userRoles.find(p => p === 'Admin') === undefined) {
      this.isAdmin = false;
      return;
    }
    this.isAdmin = true;
  }

  private _userRoles: string[] = [];

  private envHistoryUriStart = '/env-history?id=';

  @state() private mappedProjects: string[] | undefined;

  @property({ type: String }) secureName = '';

  @state()
  private isEnvOwnerOrDelegate = false;

  @state()
  private isAdmin = false;

  private appDbServer: DatabaseApiModel | undefined;

  static get styles() {
    return css`
      :host {
        display: inline-block;
        width: 100%;
      }

      vaadin-button {
        color: rgba(27, 43, 65, 0.72);
        background-color: #fde3bb;
      }

      vaadin-button:disabled,
      vaadin-button[disabled] {
        background-color: #cccccc;
        color: #666666;
      }

      vaadin-button.not-endur {
        display: none;
      }

      paper-dialog.size-position {
        top: 8px;
        padding: 10px;
      }

      a {
        color: inherit; /* blue colors for links too */
        text-decoration: inherit; /* no underline */
      }

      a.plain {
        text-decoration: underline;
        color: blue;
      }
    `;
  }

  render() {
    return html`
      <reset-app-password-behalf
        id="reset-app-password-behalf"
        .appUsers="${this.envContent?.EndurUsers ?? []}"
        .envFilter="${this.envFilter}"
        .environmentName="${this.environment?.EnvironmentName}"
        .serverName="${this.appDbServer?.ServerName}"
        .databaseName="${this.appDbServer?.Name}"
      ></reset-app-password-behalf>
      <make-like-production-dialog
        id="make-like-prod-dialog"
        .mappedProjects="${this.mappedProjects}"
        .targetEnv="${this.envContent?.EnvironmentName}"
      ></make-like-production-dialog>
      <add-edit-access-control
        id="add-edit-access-control"
        .secureName="${this.secureName}"
      ></add-edit-access-control>
      <vaadin-details
        opened
        style="border-top: 6px solid #ffad33 !important; background-color: #fff5e6; padding-left: 4px; margin: 0px;"
      >
        <vaadin-details-summary slot="summary">
          <vaadin-horizontal-layout>
            <vaadin-icon
              icon="vaadin:automation"
              style="display: table-cell; padding-right: 5px"
            ></vaadin-icon>
            <span> Environment Control Center </span>
          </vaadin-horizontal-layout>
        </vaadin-details-summary>
        <div style="padding-left: 30px">
          <vaadin-button
            title="Delete Environment &amp; Properties"
            @click="${this.deleteEnvironment}"
            ?disabled="${!(this.isAdmin || this.isEnvOwnerOrDelegate)}"
          >
            <vaadin-icon icon="icons:delete" slot="prefix"></vaadin-icon>
            Delete Environment...
          </vaadin-button>
          <vaadin-button
            title="Environment History"
            ?disabled="${this.environment === undefined}"
            @click="${this.openEnvHistory}"
          >
            <vaadin-icon slot="prefix" icon="icons:history"></vaadin-icon>
            Environment History
          </vaadin-button>
          <vaadin-button
            title="Access Control..."
            theme="icon"
            @click="${this.openAccessControl}"
          >
            <vaadin-icon icon="vaadin:lock"></vaadin-icon>
            Environment Access...
          </vaadin-button>
          <vaadin-button
            id="mlp"
            title="Configure with predefined suite of requests"
            @click="${this.makeLikeProd}"
          >
            <vaadin-icon icon="vaadin:compile" slot="prefix"></vaadin-icon>
            Bundle Request...
          </vaadin-button>
          <vaadin-button
            id="reset-others-password"
            title="Reset Password for another user for Database with '${this.environment?.Details?.ThinClient}' tag"
            @click="${this.resetAppPasswordBehalf}"
            ?hidden="${!this.isEndur}"
            .disabled="${this.environment?.EnvironmentIsProd ||
            !(this.isEnvOwnerOrDelegate || this.isAdmin)}"
          >
            <vaadin-icon icon="vaadin:safe" slot="prefix"></vaadin-icon>
            Reset SQL Instance Account Password for...
          </vaadin-button>
        </div>
      </vaadin-details>
    `;
  }

  constructor() {
    super();

    super.loadEnvironmentInfo();
    const gc = GlobalCache.getInstance();
    if (gc.userRoles === undefined) {
      gc.allRolesResp?.subscribe({
        next: (data: string[]) => {
          this.userRoles = data;
        },
        error: (err: string) => console.error(err),
        complete: () => console.log('finished loading user roles')
      });
    } else {
      this.userRoles = gc.userRoles;
    }
  }

  openEnvHistory() {
    window.open(
      this.envHistoryUriStart + (this.environment?.EnvironmentId ?? 0)
    );
  }

  isEnvironmentOwner() {
    const api = new RefDataEnvironmentsApi();
    api
      .refDataEnvironmentsIsEnvironmentOwnerOrDelegateGet({
        envName: this.environment?.EnvironmentName ?? ''
      })
      .subscribe(value => {
        this.isEnvOwnerOrDelegate = value;
      });
  }

  notifyEnvironmentReady() {
    this.isEnvironmentOwner();
  }

  firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener(
      'close-mlp-dialog',
      this.closeMlpDialog as EventListener
    );
  }

  openAccessControl() {
    this.secureName = this.environment?.EnvironmentName ?? '';

    const addEditAccessControl = this.shadowRoot?.getElementById(
      'add-edit-access-control'
    ) as AddEditAccessControl;

    addEditAccessControl.open(this.secureName, AccessControlType.NUMBER_1);
  }

  closeMlpDialog() {
    const dialog = this.shadowRoot?.getElementById(
      'make-like-prod-dialog'
    ) as MakeLikeProductionDialog;
    dialog.closeDialog();
    Notification.show('Completed Queuing Make Like Prod', {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
  }

  deleteEnvironment() {
    const answer = confirm(
      'Are you sure you want to delete your environment and properties?'
    );
    if (answer) {
      if (this.environment !== undefined) {
        const api = new RefDataEnvironmentsApi();
        api
          .refDataEnvironmentsDelete({ environmentApiModel: this.environment })
          .subscribe(
            (data: boolean) => {
              if (data) {
                const message = `The Environment ${
                  this.environment?.EnvironmentName
                } has been deleted from DOrc`;

                const notification = new SuccessNotification();
                notification.setAttribute('successMessage', message);
                this.shadowRoot?.appendChild(notification);
                notification.open();

                const event = new CustomEvent('environment-deleted', {
                  detail: {
                    Environment: this.environment
                  },
                  bubbles: true,
                  composed: true
                });
                this.dispatchEvent(event);
              } else {
                alert('Failed to delete your environment');
              }
            },
            () => {
              alert('Failed to delete your environment');
            }
          );
      }
    }
  }

  resetAppPasswordBehalf() {
    const dialog = this.shadowRoot?.getElementById(
      'reset-app-password-behalf'
    ) as ResetAppPasswordBehalf;
    dialog.open();
  }

  errorAlert(result: any) {
    const event = new CustomEvent('error-alert', {
      detail: { description: 'Failed to reset your password: ', result },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }

  makeLikeProd() {
    const mlp = this.shadowRoot?.getElementById(
      'make-like-prod-dialog'
    ) as MakeLikeProductionDialog;

    mlp.Open();
  }

  notifyEnvironmentContentReady(): void {
    if (
      this.environment?.EnvironmentName?.toLowerCase().indexOf('endur') !== -1
    ) {
      this.isEndur = true;
    } else {
      this.isEndur = false;
    }

    this.mappedProjects = this.envContent?.MappedProjects?.map(
      p => p.ProjectName ?? ''
    );

    // since ThinClient is a DB tag and DB type and environment filter, we can use it to find the app database server
    this.appDbServer = this.envContent?.DbServers?.find(s => s.Type === this.environment?.Details?.ThinClient);
  }
}
