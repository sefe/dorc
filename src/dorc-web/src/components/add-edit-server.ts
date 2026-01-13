import '@polymer/paper-toggle-button';
import { ComboBox } from '@vaadin/combo-box/src/vaadin-combo-box';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/combo-box';
import '@vaadin/text-field';
import { css, LitElement } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { TextField } from '@vaadin/text-field';
import { ApiBoolResult } from '../apis/dorc-api';
import type { ServerApiModel } from '../apis/dorc-api';
import {
  RefDataEnvironmentsDetailsApi,
  RefDataServersApi
} from '../apis/dorc-api';
import { WarningNotification } from './notifications/warning-notification';
import { ErrorNotification } from './notifications/error-notification';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';

@customElement('add-edit-server')
export class AddEditServer extends LitElement {
  @property({ type: Object })
  get srv(): ServerApiModel {
    return this._srv;
  }

  set srv(value: ServerApiModel) {
    if (value === undefined) return;

    const oldVal = this._srv;

    this._srv = JSON.parse(JSON.stringify(value));
    this.serverName = this.srv.Name ?? '';
    const text = this.shadowRoot?.getElementById('serverName') as TextField;
    if (text) text.value = this.serverName;
    const osNameCombo = this.shadowRoot?.getElementById('OsName') as ComboBox;
    if (osNameCombo) {
      const os = this.templates.find(t => t === this._srv?.OsName);
      if (os !== undefined) osNameCombo.selectedItem = os;
      else osNameCombo.selectedItem = undefined;
    }
    if (this._srv.ServerId !== undefined && this._srv.ServerId > 0) {
      this.serverInfoHelp = '';
    }
    console.log(`Server name: ${this.serverName}OS: ${this._srv.OsName}`);
    this.requestUpdate('srv', oldVal);
  }

  private _srv: ServerApiModel = this.getEmptyVirtualMachine();

  @property({ type: Number })
  srvId = 0;

  @property({ type: Boolean })
  srvValid = false;

  @property({ type: Boolean })
  isNameValid = false;

  @property({ type: Boolean })
  canSubmit = false;

  @property({ type: Number })
  envId = 0;

  @property({ type: Boolean })
  attach = false;

  @property({ type: Boolean })
  loadingOS = false;

  @property({ type: String })
  serverInfoHelp = '';

  @property({ type: String })
  serverName = '';

  @property({ type: Array })
  templates = [
    'Windows Server 2008 R2 Enterprise',
    'Windows Server 2008 R2 Standard',
    'Windows Server 2012 Standard',
    'Windows Server 2012 R2 Standard',
    'Windows Server 2016 Standard',
    'Windows Server 2019 Standard',
    'Windows Server 2022 Standard',
    'CentOS72',
    'RedHat67',
    'RedHat7'
  ];

  static get styles() {
    return css`
      .block {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 500px;
      }

      .small-loader {
        border: 2px solid #f3f3f3; /* Light grey */
        border-top: 2px solid #3498db; /* Blue */
        border-radius: 50%;
        width: 12px;
        height: 12px;
        animation: spin 2s linear infinite;
      }

      .button-container {
        display: flex;
        align-items: center;
        gap: 12px;
        margin-top: 10px;
      }

      @keyframes spin {
        0% {
          transform: rotate(0deg);
        }
        100% {
          transform: rotate(360deg);
        }
      }
    `;
  }

  render() {
    return html`
      <div style="padding: 10px; width:500px">
        <vaadin-vertical-layout>
          <vaadin-text-field
            id="serverName"
            label="Server Name"
            class="block"
            required
            auto-validate
            .value="${this.serverName}"
            @value-changed="${this._serverNameValueChanged}"
            helper-text="${this.serverInfoHelp}"
          ></vaadin-text-field>
          <vaadin-combo-box
            id="OsName"
            label="Operating System"
            class="block"
            @value-changed="${this._operatingSystemValueChanged}"
            .items="${this.templates}"
            placeholder="Select Operating System"
            style="width: 300px"
            clear-button-visible
          ></vaadin-combo-box>
        <div class="button-container">
          <vaadin-button @click="${this.lookupOSFromTarget}">
            Lookup OS
          </vaadin-button>
          ${this.loadingOS ? html`<div class="small-loader"></div>` : html``}
        </div>
        </vaadin-vertical-layout>
        <vaadin-button .disabled="${!this.canSubmit}" @click="${this.save}"
          >Save
        </vaadin-button>
        <vaadin-button @click="${this.reset}">Clear</vaadin-button>
      </div>
    `;
  }

  lookupOSFromTarget() {
    this.loadingOS = true;
    const api = new RefDataServersApi();
    api
      .refDataServersGetServerOperatingFromTargetGet({
        serverName: this._srv.Name ?? ''
      })
      .subscribe({
        next: value => {
          const osNameCombo = this.shadowRoot?.getElementById(
            'OsName'
          ) as ComboBox;
          if (osNameCombo) {
            osNameCombo.selectedItem = this.templates.find(
              t => t === value.ProductName
            );
            this._srv.OsName = value.ProductName;
            this.doesServerExist();
          }
          this.loadingOS = false;
        },
        error: err => {
          const notification = new WarningNotification();
          notification.setAttribute(
            'warningMessage',
            `Unable to read Server Information from Target: ${
              err.response.ExceptionMessage
            } Just select your OS manually to save.`
          );
          this.shadowRoot?.appendChild(notification);
          notification.open();
          this.loadingOS = false;
          console.error(err);
        },
        complete: () => console.log('Completed getting OS Name')
      });
  }

  getEmptyVirtualMachine(): ServerApiModel {
    return {
      ApplicationTags: '',
      Name: '',
      OsName: ''
    };
  }

  reset() {
    this._srv = this.getEmptyVirtualMachine();
    this.serverInfoHelp = '';

    const serverNameText = this.shadowRoot?.getElementById(
      'serverName'
    ) as TextField;
    if (serverNameText) {
      serverNameText.value = '';
    }

    const osNameCombo = this.shadowRoot?.getElementById('OsName') as ComboBox;
    if (osNameCombo) {
      osNameCombo.selectedItem = '';
    }

    this.canSubmit = false;
  }

  _serverNameValueChanged(data: CustomEvent) {
    if (this._srv === undefined) this._srv = this.getEmptyVirtualMachine();
    if (this._srv) {
      const text = data.target as TextField;
      this._srv.Name = text.value.trim();
      if (this._srv.Name !== '' && !this.hasWhiteSpace(this._srv.Name)) {
        this.doesServerExist();
      } else {
        this._checkServer([]);
      }
    }
  }

  private doesServerExist() {
    const api = new RefDataServersApi();
    api.refDataServersServerGet({ server: this._srv.Name ?? '' }).subscribe(
      (data: ServerApiModel) => {
        this._checkServer([data]);
      },
      (err: any) => console.error(err),
      () => console.log('done checking server name')
    );
  }

  hasWhiteSpace(s: string) {
    return /\s/g.test(s);
  }

  _operatingSystemValueChanged(data: any) {
    this._srv.OsName = data.target.value as string;
    this.checkServerValidity();
  }

  _canSubmit() {
    this.canSubmit = this.srvValid && this.isNameValid;
  }

  _checkServer(data: ServerApiModel[]) {
    const foundServer = data?.[0];
    if (
      foundServer &&
      /* editing */ foundServer.ServerId === this._srv.ServerId &&
      this._srv.Name &&
      this._srv.Name?.length > 0 &&
      !this.hasWhiteSpace(this._srv.Name ?? '')
    ) {
      this.isNameValid = true;
      this.serverInfoHelp = '';
    } else if (foundServer && foundServer.ServerId !== this._srv.ServerId) {
      this.isNameValid = false;
      this.serverInfoHelp = 'Server Name already exists';
    } else if (
      !foundServer &&
      this._srv.Name &&
      this._srv.Name?.length > 0 &&
      !this.hasWhiteSpace(this._srv.Name ?? '')
    ) {
      this.isNameValid = true;
      this.serverInfoHelp = '';
    } else {
      if (this._srv.Name === '') {
        this.serverInfoHelp = 'Server Name cannot be blank';
      } else if (this.hasWhiteSpace(this._srv.Name ?? '')) {
        this.serverInfoHelp = 'Server Name cannot contain whitespace';
      }
      this.isNameValid = false;
    }
    this._canSubmit();
  }

  save() {
    if (this._srv.ServerId !== undefined && this._srv.ServerId > 0) {
      const api = new RefDataServersApi();
      api
        .refDataServersPut({
          id: this._srv.ServerId ?? 0,
          serverApiModel: this._srv
        })
        .subscribe({
          next: (data: ServerApiModel) => {
            this.fireServerChangedEvent(data);
          },
          error: (err: any) => {
            this.showError(err);
          },
          complete: () => console.log('done updating server')
        });
    } else {
      const api = new RefDataServersApi();
      api.refDataServersPost({ serverApiModel: this._srv }).subscribe({
        next: (data: ServerApiModel) => {
          this.fireServerCreatedEvent(data);
          this.attachServerToEnvironment(data);
        },
        error: (err: any) => {
          this.showError(err);
        },
        complete: () => console.log('done adding server')
      });
    }
  }

  private showError(err: any) {
    const notification = new ErrorNotification();
    
    const errorMessage = retrieveErrorMessage(err, 'Failed to save server');
    
    notification.setAttribute('errorMessage', errorMessage);
    this.shadowRoot?.appendChild(notification);
    notification.open();
    console.error(err);
  }

  private fireServerCreatedEvent(data: ServerApiModel) {
    const event = new CustomEvent('server-created', {
      bubbles: true,
      composed: true,
      detail: {
        data
      }
    });
    this.dispatchEvent(event);
  }

  private fireServerChangedEvent(data: ServerApiModel) {
    const event = new CustomEvent('server-updated', {
      bubbles: true,
      composed: true,
      detail: {
        data
      }
    });
    this.dispatchEvent(event);
  }

  attachServerToEnvironment(data: ServerApiModel) {
    const id = data.ServerId as number;
    if (id > 0) {
      if (this.attach) {
        this.srvId = id;
        const api = new RefDataEnvironmentsDetailsApi();

        api
          .refDataEnvironmentsDetailsPut({
            envId: this.envId,
            componentId: this.srvId,
            action: 'attach',
            component: 'server'
          })
          .subscribe(
            (apiBoolResult: ApiBoolResult) => {
              this.attachServer(apiBoolResult);
            },
            (err: any) => {
              this.errorAlert(err);
            },
            () => console.log('done adding server')
          );
      }
    }
  }

  attachServer(result: ApiBoolResult) {
    const data = this.srv;
    const id = result.Result as boolean;
    if (id) {
      this.reset();
      this.canSubmit = false;
      const event = new CustomEvent('server-attached', {
        bubbles: true,
        composed: true,
        detail: {
          message: `Server ${data.Name} created & added successfully`,
          data
        }
      });
      this.dispatchEvent(event);
    } else {
      this.errorAlert(result);
    }
  }

  _toggleChanged() {
    this._canSubmit();
  }

  private checkServerValidity() {
    let result = true;
    if (this._srv.OsName?.length === 0) {
      result = false;
    }
    this.srvValid = result;
    this._canSubmit();
  }

  errorAlert(result: any) {
    const event = new CustomEvent('error-alert', {
      detail: {
        description: 'Unable to attach server: ',
        result
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
    console.log(result);
  }
}
