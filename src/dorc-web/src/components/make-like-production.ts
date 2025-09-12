import { css, LitElement, PropertyValues, render } from 'lit';
import '@vaadin/checkbox';
import '@vaadin/button';
import '@vaadin/combo-box';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import {
  BundledRequestsApi,
  BundledRequestsApiModel,
  MakeLikeProdApi,
  PropertiesApi,
  PropertyApiModel,
  RequestProperty
} from '../apis/dorc-api';
import { ComboBox } from '@vaadin/combo-box/src/vaadin-combo-box';
import { TextField } from '@vaadin/text-field';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import { GridItemModel } from '@vaadin/grid';
import './deploy/property-override-controls';
import { MakeLikeProductionDialog } from './make-like-production-dialog.ts';

@customElement('make-like-production')
export class MakeLikeProduction extends LitElement {
  get mappedProjects(): string[] | undefined {
    return this._mappedProjects;
  }

  @property({ type: Array })
  set mappedProjects(value: string[] | undefined) {
    this._mappedProjects = value;

    const api = new BundledRequestsApi();
    api.bundledRequestsGet({ projectNames: this._mappedProjects }).subscribe({
      next: (data: BundledRequestsApiModel[]) => {
        this.bundleRequests = data;

        const unique = [...new Set(data.map(item => item.BundleName))];

        this.setBundleNames(unique);
      },
      error: (err: any) => console.error(err),
      complete: () => console.log('done loading bundles')
    });
  }

  private bundleRequests!: BundledRequestsApiModel[];
  @property({ type: Array }) private dataBackups: string[] | undefined;

  @property({ type: Array }) private bundledRequests:
    | (string | null | undefined)[]
    | undefined;

  private _mappedProjects: string[] | undefined;

  private selectedDataBackup: string | undefined;

  private selectedBundleName: string | undefined;

  @property({ type: Array }) propertyOverrides: RequestProperty[] = [];

  @property({ type: Array }) properties: PropertyApiModel[] | undefined;

  @property({ type: Object }) dialog: MakeLikeProductionDialog | undefined;

  private propertyName = '';

  private propertyValue = '';

  static get styles() {
    return css`
      .block {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 100%;
      }

      .loader {
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

  render() {
    return html`
      <vaadin-vertical-layout style="width: 700px">
        <vaadin-combo-box
          label="Bundle Request"
          class="block"
          .items="${this.bundledRequests}"
          .itemValuePath=""
          style="min-width: 600px"
          @value-changed="${this._dataSourceBundleNameChanged}"
        ></vaadin-combo-box>
        <vaadin-combo-box
          label="Data Source"
          class="block"
          .items="${this.dataBackups}"
          style="min-width: 600px"
          @value-changed="${this._dataSourceDataBackupChanged}"
        ></vaadin-combo-box>
        <vaadin-details
          closed
          summary="Property Overrides (Optional)"
          style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; width: 100%"
        >
          <vaadin-vertical-layout style="align-items: stretch">
            <vaadin-combo-box
              @value-changed="${this._propNameValueChanged}"
              .items="${this.properties}"
              placeholder="Select Property"
              clear-button-visible
              item-label-path="Name"
              item-value-path="Name"
              style="min-width: 600px"
            ></vaadin-combo-box>
            <vaadin-text-field
              required
              placeholder="Property Value"
              @value-changed="${this._propValueChanged}"
              style="min-width: 500px"
            ></vaadin-text-field>
            <vaadin-button
              @click="${this.AddOverrideProperty}"
              style="width: 96px"
              theme="primary"
              >Add
            </vaadin-button>
            <vaadin-grid
              id="grid"
              .items="${this.propertyOverrides}"
              column-reordering-allowed
              multi-sort
              style="height: 200px"
              theme="compact row-stripes no-row-borders no-border"
            >
              <vaadin-grid-sort-column
                header="Property Name"
                path="PropertyName"
                width="300px"
                flex-grow="0"
                resizable
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                header="Property Value"
                path="PropertyValue"
                flex-grow="0"
                width="300px"
                resizable
              ></vaadin-grid-sort-column>
              <vaadin-grid-column
                .renderer="${this._boundPropOverridesButtonsRenderer}"
                .attachedDbsControl="${this}"
                resizable
              ></vaadin-grid-column>
            </vaadin-grid>
          </vaadin-vertical-layout>
        </vaadin-details>
      </vaadin-vertical-layout>
    `;
  }

  constructor() {
    super();

    const api = new PropertiesApi();
    api.propertiesGet().subscribe({
      next: (data: PropertyApiModel[]) => {
        this.properties = data;
      },
      error: (err: any) => console.error(err),
      complete: () => console.log('done loading properties')
    });
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);
  }

  _propNameValueChanged(data: any) {
    if (data) {
      const combo = data.target as ComboBox;
      this.propertyName = combo.value;
    }
  }

  private _propValueChanged(data: any) {
    if (data) {
      const field = data.target as TextField;
      this.propertyValue = field.value as string;
    }
  }

  private AddOverrideProperty() {
    const find = this.properties?.find(
      value => value.Name === this.propertyName
    );

    if (find === undefined) {
      alert('Please select a property from the list!');
      return;
    }

    if (this.propertyValue === '') {
      alert('The property must contain a value!');
      return;
    }

    const property: RequestProperty = {
      PropertyName: find.Name,
      PropertyValue: this.propertyValue
    };
    this.propertyOverrides.push(property);
    this.propertyOverrides = JSON.parse(JSON.stringify(this.propertyOverrides));

    this.dialog?.propertyAdded(property);
  }

  _boundPropOverridesButtonsRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<RequestProperty>
  ) {
    // The below line has a horrible hack
    // eslint-disable-next-line @typescript-eslint/ban-ts-comment
    // @ts-ignore
    const altThis = _column.attachedDbsControl as MakeLikeProduction;
    const propertyOverride = model.item as RequestProperty;

    render(
      html` <property-override-controls
        .propertyOverride="${propertyOverride}"
        @property-override-removed="${() => {
          altThis.RemoveOverrideProperty(propertyOverride);
        }}"
      ></property-override-controls>`,
      root
    );
  }

  private RemoveOverrideProperty(propertyOverride: RequestProperty) {
    this.propertyOverrides = this.propertyOverrides.filter(
      val => val.PropertyName != propertyOverride.PropertyName
    );
    this.propertyOverrides = JSON.parse(JSON.stringify(this.propertyOverrides));
    this.dialog?.propertyRemoved(propertyOverride);
  }

  _dataSourceDataBackupChanged(data: CustomEvent) {
    this.selectedDataBackup = data.detail.value;

    this.dialog?.backupChanged(this.selectedDataBackup);
  }

  _dataSourceBundleNameChanged(data: CustomEvent) {
    this.isLoading();
    this.selectedBundleName = data.detail.value;

    const selectedBundleReqs = this.bundleRequests?.filter(
      b => b.BundleName === this.selectedBundleName
    );

    if (selectedBundleReqs.length === 0) {
      return;
    }

    const projectId: number = selectedBundleReqs[0].ProjectId ?? 0;

    const api = new MakeLikeProdApi();
    api.makeLikeProdDataBackupsGet({ projectId: projectId }).subscribe({
      next: (data: string[]) => {
        this.setDataBackups(data);
      },
      error: (err: any) => console.error(err),
      complete: () => console.log('done loading data backups')
    });

    this.dialog?.bundleChanged(this.selectedBundleName);
  }

  isLoading() {
    this.dialog?.mlpIsLoading();
  }

  isntLoading() {
    this.dialog?.mlpIsntLoading();
  }

  private setBundleNames(data: (string | null | undefined)[]) {
    this.bundledRequests = data;
  }

  private setDataBackups(data: string[]) {
    this.dataBackups = data;

    this.isntLoading();
  }
}
