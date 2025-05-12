import { css, render } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import { customElement } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PageEnvBase } from './page-env-base';
import '@vaadin/details';
import '../attached-app-users';
import {
  BundledRequestsApi,
  BundledRequestsApiModel, BundledRequestType,
} from '../../apis/dorc-api';
import { ErrorNotification } from '../notifications/error-notification.ts';
import { GridColumn } from '@vaadin/grid/vaadin-grid-column';
import { GridItemModel } from '@vaadin/grid';
import { HegsJsonViewer } from '../hegs-json-viewer.ts';
import '../grid-button-groups/bundle-request-controls';

@customElement('env-bundles')
export class EnvBundles extends PageEnvBase {
  static get styles() {
    return css`
      :host {
        display: flex;
        width: 100%;
        height: 100%;
        flex-direction: column;
      }

      vaadin-grid {
        height: 100%;
      }
    `;
  }

  private bundledRequests: Array<BundledRequestsApiModel> = [];

  render() {
    return html`
      <vaadin-details
        opened
        summary="Bundles"
        style="border-top: 6px solid cornflowerblue; background-color: ghostwhite; padding-left: 4px; margin: 0px;"
      >

      </vaadin-details>
      <vaadin-grid
        .items="${this.bundledRequests}"
        column-reordering-allowed
        multi-sort
        theme="compact row-stripes no-row-borders no-border"
      >
        <vaadin-grid-column
          path="BundleName"
          header="Bundle Name"
          auto-width
          flex-grow="0"
          resizable
        ></vaadin-grid-column>
        <vaadin-grid-column
          .renderer="${this._typeRenderer}"
          header="Type"
          auto-width
          flex-grow="0"
          resizable
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="RequestName"
          header="Request Name"
          auto-width
          flex-grow="0"
          resizable
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="Sequence"
          header="Sequence"
          auto-width
          flex-grow="0"
          resizable
        ></vaadin-grid-column>
        <vaadin-grid-column
          .renderer="${this.bundleControlsRenderer}"
          resizable
          flex-grow="0"
        ></vaadin-grid-column>
        <vaadin-grid-column
          path="Request"
          header="Request"
          resizable
          .renderer="${this._jsonRenderer}"
        ></vaadin-grid-column>
      </vaadin-grid>
    `;
  }

  bundleControlsRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<BundledRequestsApiModel>
  ) {

    render(
      html`<bundle-request-controls .value="${model.item}">
      </bundle-request-controls>`,
      root
    );
  }

  _jsonRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<BundledRequestsApiModel>
  ) {
    const bundle = model.item as BundledRequestsApiModel;

    root.innerHTML = `<hegs-json-viewer style="font-size: small ">${
      bundle.Request
    }</hegs-json-viewer>`;
    const viewer = root.querySelector(
      'hegs-json-viewer'
    ) as unknown as HegsJsonViewer;
    viewer.expand('**');
  }

  _typeRenderer(
    root: HTMLElement,
    _column: GridColumn,
    model: GridItemModel<BundledRequestsApiModel>
  ) {
    const bundle = model.item as BundledRequestsApiModel;

    let typeString = '';

    if (bundle.Type === BundledRequestType.NUMBER_1) {
      typeString = 'JobRequest'
    } else if (bundle.Type === BundledRequestType.NUMBER_2) {
      typeString = 'CopyEnvBuild'
    } else {
      typeString = 'Unknown';
    }

    root.innerHTML = `<span>${typeString}</span>`;
  }

  private fetchBundledRequests() {
    const api = new BundledRequestsApi();
    const projs: string[] =
      this.envContent?.MappedProjects?.map(p => p.ProjectName || '').filter(
        name => name
      ) || [];
    api.bundledRequestsGet({ projectNames: projs }).subscribe(
      data => {
        this.bundledRequests = data;
        this.requestUpdate();
      },
      error => {
        console.error('Error fetching bundled requests:', error);
        new ErrorNotification().open();
      }
    );
  }

  constructor() {
    super();
    super.loadEnvironmentInfo();
  }

  override notifyEnvironmentContentReady(){
    this.fetchBundledRequests();
  }
}
