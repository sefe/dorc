import { css, LitElement } from 'lit';
import { ComboBoxItemModel } from '@vaadin/combo-box';
import { ComboBox } from '@vaadin/combo-box/src/vaadin-combo-box';
import '@vaadin/number-field';
import '@vaadin/text-field';
import { NumberField } from '@vaadin/number-field';
import '@vaadin/button';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { Notification } from '@vaadin/notification';
import type { SqlPortApiModel } from '../apis/dorc-api';
import { RefDataDatabasesApi, RefDataSqlPortsApi } from '../apis/dorc-api';

@customElement('add-sql-port')
export class AddSqlPort extends LitElement {
  @property({ type: Array }) databases: string[] = [];

  @property() private database: string = "";

  @property() private portNumber = '';

  @property({ type: Boolean }) private portNumberValid = false;

  @property({ type: Boolean }) private databaseValid = false;

  @property({ type: Boolean }) private valid = false;


  @property() private overlayMessage: any;
  @property() private errorMessage: any;

  static get styles() {
    return css`
      .small-loader {
        border: 2px solid #f3f3f3; /* Light grey */
        border-top: 2px solid #3498db; /* Blue */
        border-radius: 50%;
        width: 12px;
        height: 12px;
        animation: spin 2s linear infinite;
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
  
  constructor() {
      super();
  
      const api = new RefDataDatabasesApi();

      api.refDataDatabasesGetDatabasServerNameslistGet().subscribe(
        (data: string[]) => {
          this.setDatabases(data);
        },
  
        (err: any) => console.error(err),
        () => console.log('done loading projects')
      );
  }
  
  setDatabases(databases: string[]) {
    this.databases = databases;
  }

  render() {
    return html`
      <div style="width:50%;">
        <vaadin-vertical-layout>
              <vaadin-combo-box
                id="databases-combobox"
                @value-changed="${this._databaseValueChanged}"
                .items="${this.databases}"
                .renderer="${this._databasesRenderer}"
                placeholder="Select Database"
                label="Database"
                style="width: 600px; display: flex; padding-left: 10px"
                clear-button-visible
              ></vaadin-combo-box>
          <vaadin-number-field
            class="block"
            style="width: 600px; display: flex; padding-left: 10px"
            id="port-number"
            label="Port Number"
            value=""
            auto-validate
            @input="${this._portNumberValueChanged}"
            .value="${this.portNumber}"
          ></vaadin-number-field>
        </vaadin-vertical-layout>
        <div style="padding-left: 10px">
          <vaadin-button .disabled="${!this.valid}" @click="${this._submit}"
            >Save</vaadin-button
          >
            <vaadin-button @click="${this.reset}">Clear</vaadin-button>
        </div>
      </div>
      <div>
        <span style="color: darkred">${this.overlayMessage}</span>
      </div>
      <div style="width: 400px">
        <span style="color: darkred">${this.errorMessage}</span>
      </div>
    `;
  } 

  _databasesRenderer(
    root: HTMLElement,
    _comboBox: ComboBox,
    model: ComboBoxItemModel<string>
  ) {
    const template = model.item as string;
    root.innerHTML = `<div>${template}</div>`;
  }

  _databaseValueChanged(data: any) {
      const ServerName = data.target.value as string;
      const db = this.databases?.find(value => value === ServerName);
      if (db) {
        this.database = db;
        this.databaseValid = true;
      }
      else
      {
        this.database = "";
        this.databaseValid = false;
      }
  
      if (this.database !== undefined) {   
        
      const api = new RefDataDatabasesApi();
      //const params = new GridDataProviderParams<DatabaseApiModel>
      api.refDataDatabasesGetDatabasServerNameslistGet()
      .subscribe(
        (data: string[]) => {
          this.setDatabases(data);
        },
  
        (err: any) => console.error(err),
        () => console.log('done loading projects')
      );
      }
    }

  _portNumberValueChanged(data: any) {
    this.portNumber = data.currentTarget.value;
    this.portNumberValid =  Number(this.portNumber) > 0;
    this.validate();
  }

  validate() {
    if (this.database !== undefined && this.database != "") {
      if (this.portNumberValid && this.databaseValid) {
        this.valid = true;
      } else {
        this.valid = false;
      }
    }
  }

  _submit() {
    const api = new RefDataSqlPortsApi();
    const sqlPortModel: SqlPortApiModel = {InstanceName: this.database, SqlPort: this.portNumber};

    api.refDataSqlPortsPost({ sqlPortApiModel : sqlPortModel }).subscribe({
      next: () => {
        this._addSqlPort(sqlPortModel);
      },
      error: (err: any) => {
        this.overlayMessage = 'Error creating SQL port!';
        if (err?.response)
          this.errorMessage =  err.response;
        console.error(err);
      },
      complete: () => {
        console.log('done adding permission');
        this.reset();
        Notification.show(`SQL port added successfully`, {
                      theme: 'success',
                      position: 'bottom-start',
                      duration: 3000
                    });
      }
    });
  }

  _addSqlPort(data: SqlPortApiModel) {
    if (Number(data.SqlPort) > 0) {
      const event = new CustomEvent('sqlport-created', {
        detail: {
          daemon: data
        },
        bubbles: true,
        composed: true
      });
      this.dispatchEvent(event);
    } else {
      this.overlayMessage = 'Error adding SQL port!';
    }
  }

  clearNumberField(name: string) {
    const field = this.shadowRoot?.getElementById(name) as NumberField;
    if (field) {
      field.value = '0';
    }
  }
  
    private clearComboboxSelectedItem(comboName: string) {
      const combo = this.shadowRoot?.getElementById(comboName) as ComboBox;
      if (combo) combo.selectedItem = undefined;
    }

  reset() {
    this.clearNumberField('port-number');
    this.clearComboboxSelectedItem('databases-combobox');

    this.database ="";
    this.databaseValid = false;
    this.portNumberValid = false;

    this.valid = false;
    this.overlayMessage = '';
    this.errorMessage = '';
  }
}
