import { css, LitElement } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/combo-box';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import { GridItemModel } from '@vaadin/grid';
import '@polymer/paper-dialog';
import { ComboBox } from '@vaadin/combo-box';
import '@vaadin/text-field';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import {
  RefDataDatabasesApi,
  RefDataEnvironmentsDetailsApi,
  RefDataGroupsApi
} from '../apis/dorc-api';
import {
  ApiBoolResult,
  DatabaseApiModel,
  GroupApiModel
} from '../apis/dorc-api';

@customElement('add-edit-database')
export class AddEditDatabase extends LitElement {
  @property({ type: Object })
  get database(): DatabaseApiModel {
    return this._database;
  }

  set database(value: DatabaseApiModel) {
    if (value === undefined) return;

    const oldVal = this._database;

    this._database = JSON.parse(JSON.stringify(value));

    this.DatabaseName = this._database.Name ?? '';
    this.DatabaseType = this._database.Type ?? '';
    this.DbServerName = this._database.ServerName ?? '';
    this.AdGroup = this._database.AdGroup ?? '';
    this.ArrayName = this._database.ArrayName ?? '';

    // Clear any previous error messages when setting new database
    this.ErrorMessage = '';
    this.infoMessage = '';

    const adGroupCombo = this.shadowRoot?.getElementById('active-dir-groups') as ComboBox;
    if (adGroupCombo) {
      const group = this.groups.find(t => t.GroupName === this._database.AdGroup);
      if (group !== undefined) adGroupCombo.selectedItem = group;
      else adGroupCombo.selectedItem = undefined;
    }

    console.log(`Database instance: ${this.DbServerName} name: ${this.DatabaseName}`);
    this.requestUpdate('srv', oldVal);
  }

  private _database: DatabaseApiModel = this.getEmptyDatabase();

  @property({ type: String })
  public DatabaseName = '';

  @property({ type: String })
  public DatabaseType = '';

  @property({ type: String })
  public DbServerName = '';

  @property({ type: String })
  public ArrayName = '';

  @property({ type: String })
  public AdGroup = '';

  private isNameValid = false;

  @property({ type: Boolean })
  private canSubmit = false;

  @property() ErrorMessage = '';

  @property({ type: Array })
  private groups: GroupApiModel[] = [];

  private adGroupsMap: Map<number | undefined, GroupApiModel> | undefined;

  @property({ type: String })
  private infoMessage: any;

  @property({ type: Boolean })
  private attach = false;

  private dbId = 0;

  @property({ type: Number })
  private envId = 0;

  constructor() {
    super();

    const api = new RefDataGroupsApi();
    api.refDataGroupsGet().subscribe(
      (data: GroupApiModel[]) => {
        this.setADGroups(data);
      },
      (err: any) => console.error(err),
      () => console.log('done loading AD Groups')
    );
  }

  connectedCallback() {
    super.connectedCallback();
    // Reset all validation state when component is connected/shown
    this._reset();
  }

  static get styles() {
    return css`
      .block {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 500px;
      }
    `;
  }

  render() {
    return html`
      <div style="padding: 10px; width:500px">
        <vaadin-vertical-layout>
          <vaadin-text-field
            class="block"
            label="Database"
            pattern="^[a-zA-Z0-9_]{1,128}?$"
            required
            auto-validate
            @input="${this._dbNameValueChanged}"
            .value="${this.DatabaseName}"
          ></vaadin-text-field>
          <vaadin-text-field
            class="block"
            label="Application Tag"
            pattern="^[a-zA-Z0-9&.\\- ]+$"
            required
            auto-validate
            @input="${this._dbTypeValueChanged}"
            .value="${this.DatabaseType}"
          ></vaadin-text-field>
          <vaadin-text-field
            class="block"
            pattern="^[a-zA-Z0-9_\\-]{1,128}(\\\\[a-zA-Z0-9_\\-]{1,128})?$"
            label="Instance"
            required
            auto-validate
            @input="${this._sqlServerValueChanged}"
            .value="${this.DbServerName}"
          ></vaadin-text-field>
          <vaadin-text-field
            class="block"
            label="Array Name (leave blank if unknown)"
            @input="${this._dbaArrayNameValueChanged}"
            auto-validate
            .value="${this.ArrayName}"
          ></vaadin-text-field>
          <vaadin-combo-box
            class="block"
            id="active-dir-groups"
            label="Citrix Active Directory Group"
            item-value-path="GroupId"
            item-label-path="GroupName"
            @value-changed="${this.setSelectedADGroup}"
            .items="${this.groups}"
            filter-property="GroupName"
            .renderer="${this._boundADGroupsRenderer}"
            placeholder="Select Permission"
            style="width: 300px"
            helper-text="(Advanced: Use 'OTHER' if not applicable/unknown)"
            clear-button-visible
          ></vaadin-combo-box>
        </vaadin-vertical-layout>
        <vaadin-button
          .disabled="${!this.canSubmit}"
          @click="${this.saveDatabase}"
          >Save</vaadin-button
        >
        <vaadin-button @click="${this._reset}">Clear</vaadin-button>
        <paper-item style="color: darkred">${this.infoMessage}</paper-item>
        <div style="color: #FF3131">${this.ErrorMessage}</div>
      </div>
    `;
  }

  setSelectedADGroup(data: any) {
    if (data.currentTarget.value > 0) {
      this.AdGroup =
        this.adGroupsMap?.get(data.currentTarget.value)?.GroupName || '';
      this.checkDBExists();
    }
  }

  _boundADGroupsRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<GroupApiModel>
  ) {
    // only render the checkbox once, to avoid re-creating during subsequent calls
    const groupApiModel = model.item as GroupApiModel;
    root.innerHTML = `<paper-item>${groupApiModel.GroupName}</paper-item>`;
  }

  _reset() {
    const activeDirectoryGroups = this.shadowRoot?.getElementById(
      'active-dir-groups'
    ) as ComboBox;

    if (activeDirectoryGroups) activeDirectoryGroups.clear();

    this.DatabaseName = '';
    this.DatabaseType = '';
    this.DbServerName = '';
    this.ArrayName = '';
    this.AdGroup = '';
    this.ErrorMessage = '';
    this.infoMessage = '';
    this.isNameValid = false;
    this.canSubmit = false;
  }

  saveDatabase() {
    if (this._database.Id !== undefined && this._database.Id > 0) {
      const api = new RefDataDatabasesApi();
      api.refDataDatabasesPut({
        id: this._database.Id ?? 0,
        databaseApiModel: {
          Id: this._database.Id,
          ServerName: this.DbServerName,
          Name: this.DatabaseName,
          Type: this.DatabaseType,
          AdGroup: this.AdGroup,
          ArrayName: this.ArrayName
        }
      })
      .subscribe({
        next: (data: DatabaseApiModel) => {
          this.fireDatabaseChangedEvent(data);
          this._reset();
        },
        error: (err: any) => {
          console.error(err.response);
          this.ErrorMessage = err.response;
        },
        complete: () => console.log('done adding DB')
      });
    }
    else {
      const api = new RefDataDatabasesApi();
      api
        .refDataDatabasesPost({
          databaseApiModel: {
            ServerName: this.DbServerName,
            Name: this.DatabaseName,
            Type: this.DatabaseType,
            AdGroup: this.AdGroup,
            ArrayName: this.ArrayName
          }
        })
        .subscribe({
          next: (data: DatabaseApiModel) => {
            this.attachDb(data);
            const id = data.Id || 0;
            if (id > 0) {
              this.databaseAddComplete(data);
              this._reset();
            }
          },
          error: (err: any) => {
            console.error(err.response);
            this.ErrorMessage = err.response;
          },
          complete: () => console.log('done adding DB')
        });
    }
  }

  private fireDatabaseChangedEvent(data: DatabaseApiModel) {
    const event = new CustomEvent('database-updated', {
      bubbles: true,
      composed: true,
      detail: {
        data
      }
    });
    this.dispatchEvent(event);
  }

  _dbaArrayNameValueChanged(data: any) {
    this.ArrayName = data.currentTarget.value;
    this.checkDBExists();
  }

  _dbTypeValueChanged(data: any) {
    this.DatabaseType = data.currentTarget.value.trim();
    this.checkDBExists();
  }

  _sqlServerValueChanged(data: any) {
    this.DbServerName = data.currentTarget.value.trim();
    this.checkDBExists();
  }

  _dbNameValueChanged(data: any) {
    this.DatabaseName = data.currentTarget.value.trim();
    this.checkDBExists();
  }

  _setGroups(data: any) {
    this.groups = data.detail.response;
  }

  attachDb(data: DatabaseApiModel) {
    const id = data.Id || 0;
    if (id > 0) {
      if (this.attach) {
        this.dbId = id;
        const api = new RefDataEnvironmentsDetailsApi();
        api
          .refDataEnvironmentsDetailsPut({
            action: 'attach',
            envId: this.envId,
            component: 'database',
            componentId: this.dbId
          })
          .subscribe({
            next: (apiBoolResult: ApiBoolResult) => {
              this._attachedDb(apiBoolResult);
            },
            error: (err: any) => {
              this.errorAlert(data);
              console.error(err);
            },
            complete: () => {
              this.databaseAttachComplete();
              this._reset();
              console.log('done attaching DB');
            }
          });
      }
    }
  }

  databaseAddComplete(data: DatabaseApiModel) {
    const event = new CustomEvent('database-created', {
      detail: {
        message: 'Database created successfully!',
        data: data
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }

  databaseAttachComplete() {
    const event = new CustomEvent('database-attached', {
      detail: {
        message: 'Database attached successfully!'
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }

  _attachedDb(data: ApiBoolResult) {
    if (data.Result) {
      this.databaseAttachComplete();
      this._reset();
      this.canSubmit = false;
    } else {
      this.errorAlert(data);
    }
  }

  private checkDBExists() {
    const api = new RefDataDatabasesApi();
    api
      .refDataDatabasesGet({
        name: this.DatabaseName,
        server: this.DbServerName
      })
      .subscribe(
        (data: DatabaseApiModel[]) => {
          this.checkDbValid(data);
        },
        (err: any) => console.error(err),
        () => console.log("done loading existing DB's")
      );
  }

  errorAlert(result: any) {
    const event = new CustomEvent('error-alert', {
      detail: {
        description: 'Unable to attach database: ',
        result
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
    console.log(result);
  }

  private setADGroups(data: GroupApiModel[]) {
    this.adGroupsMap = new Map(data.map(obj => [obj.GroupId, obj]));
    this.groups = data;
  }

  hasWhiteSpace(s: string) {
    return /\s/g.test(s);
  }


  private checkDbValid(dbs: DatabaseApiModel[]) {
    const foundDatabase = dbs?.[0];
        
    if (
      foundDatabase &&
      foundDatabase.Id === this._database.Id &&
      this.checkDatabaseComplete({ServerName: this.DbServerName, Name: this.DatabaseName, Type: this.DatabaseType, AdGroup: this.AdGroup})
    ) {
      this.isNameValid = true;
      this.infoMessage = '';

    } else if (foundDatabase && foundDatabase.Id !== this._database.Id) {
      this.isNameValid = false;
      this.infoMessage = 'Database already exists';

    } else if (
      !foundDatabase &&
      this.checkDatabaseComplete({ServerName: this.DbServerName, Name: this.DatabaseName, Type: this.DatabaseType, AdGroup: this.AdGroup})
    ) {
      this.isNameValid = true;
      this.infoMessage = '';

    } else {
      this.isNameValid = false;
      // Clear the "Database already exists" message if the database doesn't exist
      if (!foundDatabase) {
        this.infoMessage = '';
      }
    }
    this.canSubmit = this.isNameValid;

    console.log(`isNameValid: ${this.isNameValid}`);
  }

  checkDatabaseComplete(db: DatabaseApiModel){
    let nameValid = false;
    let instanceValid = false;
    let typeValid = false;

    if (
    db.Name &&
    db.Name?.length > 0 &&
    !this.hasWhiteSpace(db.Name ?? ''))
    {
      nameValid = true;
    }
    if (
      db.ServerName &&
      db.ServerName?.length > 0 &&
      !this.hasWhiteSpace(db.ServerName ?? ''))
    {
      instanceValid = true;
    }
    if (
      db.Type &&
      db.Type?.length > 0)
    {
      typeValid = true;
    }

    return nameValid && instanceValid && typeValid;
  }

  getEmptyDatabase(): DatabaseApiModel {
    return {
      ArrayName: '',
      Name: '',
      AdGroup: '',
      Type: '',
      EnvironmentNames: [],
      Id : 0,
      UserEditable: false
    };
  }
}
