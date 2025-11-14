import { css, LitElement } from 'lit';
import '@vaadin/combo-box';
import '@vaadin/confirm-dialog';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import { GridItemModel } from '@vaadin/grid';
import '@polymer/paper-dialog';
import { ComboBox } from '@vaadin/combo-box';
import '@vaadin/button';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { ApiBoolResult, DatabaseApiModel } from '../apis/dorc-api';
import {
  RefDataDatabasesApi,
  RefDataEnvironmentsDetailsApi
} from '../apis/dorc-api';
import { Notification } from '@vaadin/notification';

@customElement('attach-database')
export class AttachDatabase extends LitElement {
  @property({ type: Array })
  public existingDatabases: Array<DatabaseApiModel> | undefined = [];

  @property({ type: Object })
  private selectedDatabase: DatabaseApiModel | undefined;

  @property({ type: Array })
  private databases: DatabaseApiModel[] | undefined;

  @property({ type: Boolean })
  private canSubmit = false;

  @property({ type: Number })
  private envId = 0;

  @property({ type: Object })
  private databaseMap: Map<number | undefined, DatabaseApiModel> | undefined;
  
  @property({ type: Boolean })
  private showSameTagWarning: boolean = false;

  @property({ type: Object })
  private existingDatabaseWithSameTag: DatabaseApiModel | undefined;

  constructor() {
    super();

    const api = new RefDataDatabasesApi();
    api.refDataDatabasesGet({}).subscribe({
      next: (data: DatabaseApiModel[]) => {
        this.setDatabases(data);
      },
      error: (err: any) => console.error(err),
      complete: () => console.log('done loading databases')
    });
  }

  static get styles() {
    return css`
      .warning-box {
        background-color: #fff3cd;
        border: 1px solid #ffeaa7;
        border-radius: 4px;
        padding: 12px;
        margin: 10px 0;
        color: #856404;
      }
      .warning-title {
        font-weight: bold;
        margin-bottom: 8px;
      }
    `;
  }

  render() {
    return html`
      <div style="padding: 10px;">
        <div class="inline">
          <vaadin-combo-box
            id="databases"
            label="Databases"
            item-value-path="Id"
            item-label-path="Name"
            @value-changed="${this.setSelectedDatabase}"
            .items="${this.databases}"
            filter-property="Name"
            .renderer="${this._boundDatabasesRenderer}"
            placeholder="Select Database"
            style="width: 300px"
            clear-button-visible
          ></vaadin-combo-box>
        </div>
        <div>
          <h3>
            Database:
            <span style="color: blue"
              >${this.selectedDatabase?.Name}</span
            >
          </h3>
          <h3>
            Application Tag:
            <span style="color: blue"
              >${this.selectedDatabase?.Type}</span
            >
          </h3>
          <h3>
            Instance:
            <span style="color: blue"
              >${this.selectedDatabase?.ServerName}</span
            >
          </h3>
          <h3>
            Citrix AD Group:
            <span style="color: blue">${this.selectedDatabase?.AdGroup}</span>
          </h3>
        </div>

        ${this.showSameTagWarning ? html`
          <div class="warning-box">
            <div class="warning-title">⚠️ Warning - Duplicate Application Tag</div>
            <div>
              A database with the tag '<strong>${this.selectedDatabase?.Type}</strong>' is already attached to this environment:
              <br><strong>${this.existingDatabaseWithSameTag?.Name}</strong> on ${this.existingDatabaseWithSameTag?.ServerName}
            </div>
          </div>
        ` : ''}
          <vaadin-horizontal-layout style="margin-right: 30px">
            <vaadin-button .disabled="${!this.canSubmit}" @click="${this.onAttachClick}" style="margin: 2px"
              >Attach</vaadin-button
            >
          </vaadin-horizontal-layout>
      </div>
    `;
  }

  onAttachClick() {
    this._submit();
  }

  setSelectedDatabase(data: any) {
    const dbId = data.currentTarget.value as number;
    if (!dbId) {
      this.showSameTagWarning = false;
      this.existingDatabaseWithSameTag = undefined;
      this.selectedDatabase = undefined;
      this.updateCanSubmit();
      return;
    }

    this.selectedDatabase = this.databaseMap?.get(dbId);
    if (this.selectedDatabase) {
      this._displayDb();
    }
  }

  private checkForSameTagWarning() {
    if (this.selectedDatabase?.Type) {
      this.existingDatabaseWithSameTag = this.existingDatabases?.find(
        db => db.Type === this.selectedDatabase?.Type
      );
      this.showSameTagWarning = !!this.existingDatabaseWithSameTag;
    }
  }

  private updateCanSubmit() {
    this.canSubmit = !!this.selectedDatabase?.Id;
  }

  _displayDb() {
    const api = new RefDataDatabasesApi();
    api
      .refDataDatabasesGet({
        name: this.selectedDatabase?.Name ?? '',
        server: this.selectedDatabase?.ServerName ?? ''
      })
      .subscribe({
        next: (data: DatabaseApiModel[]) => {
          if (
            !this.selectedDatabase ||
            this.selectedDatabase.Id != data[0].Id
          ) {
            this.selectedDatabase = data[0];
          }

          this.checkForSameTagWarning();
          this.updateCanSubmit();
        },
        error: (err: any) => console.error(err),
        complete: () => console.log('done loading database')
      });
  }

  _boundDatabasesRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<DatabaseApiModel>
  ) {
    const groupApiModel = model.item as DatabaseApiModel;
    root.innerHTML = `<paper-item><span>${groupApiModel.Name} - ${
      groupApiModel.ServerName
    }</span></paper-item>`;
  }

  _submit() {
    const api = new RefDataEnvironmentsDetailsApi();
    api
      .refDataEnvironmentsDetailsPut({
        action: 'attach',
        envId: this.envId,
        component: 'database',
        componentId: this.selectedDatabase?.Id || 0
      })
      .subscribe({
        next: (data: ApiBoolResult) => {
          if (data.Result) {
            this.processAttachDbSuccess();
          } else {
            this.processDbAttachFailure(data);
          }
        },
        error: (err: any) => {
          this.processDbAttachFailure(err);
        }
      });
  }

  _reset() {
    const databases = this.shadowRoot?.getElementById('databases') as ComboBox;

    if (databases) databases.clear();

    if (this.selectedDatabase) {
      this.selectedDatabase.Id = 0;
      this.selectedDatabase.Name = '';
      this.selectedDatabase.Type = '';
      this.selectedDatabase.ServerName = '';
      this.selectedDatabase.AdGroup = '';
    }
    this.showSameTagWarning = false;
    this.existingDatabaseWithSameTag = undefined;
    this.canSubmit = false;
  }

  private setDatabases(data: DatabaseApiModel[]) {
    this.databases = data;
    this.databaseMap = new Map(
      this.databases.map(obj => [obj.Id, obj])
    );
  }

  private processAttachDbSuccess() {
    this._reset();
    const event = new CustomEvent('database-attached', {
      detail: {
        message: 'Database attached successfully!'
      }
    });
    this.dispatchEvent(event);
    console.log('done attaching DB');
  }

  private processDbAttachFailure(result: any) {
    const errorMessage = 'Unable to attach database' + (result?.Message ? `: ${result.Message}` : '');
    Notification.show(errorMessage, {
      theme: 'error',
      position: 'bottom-start',
      duration: 3000
    });
    console.log(result);
  }
}
