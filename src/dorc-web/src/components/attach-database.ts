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
  private confirmSameTagDialogOpened: boolean = false;

  @property({ type: String })
  private confirmSameTagDialogText: string = '';

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
    return css``;
  }

  render() {
    return html`
      <vaadin-confirm-dialog
          id="sameTagExistsConfirmDialog"
          header="Existing database tag"
          cancel-button-visible
          .opened="${this.confirmSameTagDialogOpened}"
          .message="${this.confirmSameTagDialogText}"
          confirm-text="Yes, attach"
          cancel-text="Cancel"
          @confirm="${this._submit}"
          @cancel="${() => {
            this.confirmSameTagDialogOpened = false;
          }}"
        ></vaadin-confirm-dialog>
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

        <vaadin-button .disabled="${!this.canSubmit}" @click="${this.onAttachClick}"
          >Attach</vaadin-button
        >
        <vaadin-button @click="${this._reset}"
          >Clear</vaadin-button
        >
      </div>
    `;
  }

  onAttachClick() {
    const existingTag = this.existingDatabases?.find(db => db.Type === this.selectedDatabase?.Type);

    if (!existingTag) {
      this._submit();
    }
    else {
      this.confirmSameTagDialogText = `Do you really want to attach another database with tag '${this.selectedDatabase?.Type}'? The database '${existingTag?.Name}' with such tag is already attached`;
      this.confirmSameTagDialogOpened = true;
    }
  }

  setSelectedDatabase(data: any) {
    const dbId = data.currentTarget.value as number;
    this.selectedDatabase = this.databaseMap?.get(dbId);
    if (this.selectedDatabase) {
      this._displayDb();
    }
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

          if (this.selectedDatabase.Id) {
            this.canSubmit = true;
          } else {
            this.canSubmit = false;
          }
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
    // only render the checkbox once, to avoid re-creating during subsequent calls
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
