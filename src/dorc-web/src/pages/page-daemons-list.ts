import { css, PropertyValues } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '../components/add-daemon';
import '@polymer/paper-dialog';
import '@vaadin/text-field';
import { PaperDialogElement } from '@polymer/paper-dialog';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PageElement } from '../helpers/page-element';
import { DaemonApiModel } from '../apis/dorc-api';
import { RefDataDaemonsApi } from '../apis/dorc-api';

@customElement('page-daemons-list')
export class PageDaemonsList extends PageElement {
  @property({ type: Array }) daemons: Array<DaemonApiModel> = [];

  @property({ type: Array }) filteredDaemons: Array<DaemonApiModel> = [];

  @property({ type: Array }) appConfig = [];

  @property({ type: Boolean }) details = false;

  private loading = true;

  constructor() {
    super();
    this.getDaemonsList();
  }

  private getDaemonsList() {
    const api = new RefDataDaemonsApi();
    api.refDataDaemonsGet().subscribe(
      (data: DaemonApiModel[]) => {
        this.setDaemons(data);
      },

      (err: any) => console.error(err),
      () => console.log('done loading daemons')
    );
  }

  static get styles() {
    return css`
      vaadin-grid#grid {
        overflow: hidden;
        height: calc(100vh - 110px);
        --divider-color: rgb(223, 232, 239);
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
      paper-dialog.size-position {
        top: 16px;
        overflow: auto;
        padding: 10px;
      }
    `;
  }

  render() {
    return html`<div style="display: inline">
        <vaadin-text-field
          style="padding-left: 5px; width: 50%;"
          placeholder="Search"
          @value-changed="${this.updateSearch}"
          clear-button-visible
          helper-text="Use | for multiple search terms"
        >
          <vaaadin-icon slot="prefix" icon="vaadin:search"></vaaadin-icon>
        </vaadin-text-field>
        <vaadin-button
          title="Add Daemon"
          style="width: 250px"
          @click="${this.addDaemon}"
        >
          <vaadin-icon
            icon="vaadin:cog"
            style="color: cornflowerblue"
          ></vaadin-icon
          >Add Daemon...
        </vaadin-button>
      </div>
      <paper-dialog
        class="size-position"
        id="add-daemon-dialog"
        allow-click-through
        modal
      >
        <add-daemon id="add-daemon"></add-daemon>
        <div style="display: flex; justify-content: flex-end">
          <vaadin-button dialog-confirm>Close</vaadin-button>
        </div>
      </paper-dialog>
      ${this.loading
        ? html`
            <div class="overlay" style="z-index: 2">
              <div class="overlay__inner">
                <div class="overlay__content">
                  <span class="spinner"></span>
                </div>
              </div>
            </div>
          `
        : html`
            <vaadin-grid
              id="grid"
              .items=${this.filteredDaemons}
              column-reordering-allowed
              multi-sort
              theme="compact row-stripes no-row-borders no-border"
            >
              <vaadin-grid-sort-column
                path="Name"
                header="Daemon Name"
                resizable
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                path="DisplayName"
                header="Display Name"
                resizable
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                path="AccountName"
                header="Account Name"
                resizable
              ></vaadin-grid-sort-column>
              <vaadin-grid-sort-column
                path="ServiceType"
                header="Type"
                resizable
              ></vaadin-grid-sort-column>
            </vaadin-grid>
          `} `;
  }

  firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener(
      'daemon-created',
      this.daemonCreated as EventListener
    );
  }

  daemonCreated() {
    this.getDaemonsList();

    const dialog = this.shadowRoot?.getElementById(
      'add-daemon-dialog'
    ) as PaperDialogElement;
    dialog.close();
  }

  updateSearch(e: CustomEvent) {
    const value = (e.detail.value as string) || '';
    const filters = value
      .trim()
      .split('|')
      .map(filter => new RegExp(filter, 'i'));

    this.filteredDaemons = this.daemons.filter(({ DisplayName, Name }) =>
      filters.some(
        filter => filter.test(DisplayName || '') || filter.test(Name || '')
      )
    );
  }

  setDaemons(daemons: DaemonApiModel[]) {
    this.daemons = daemons;
    this.filteredDaemons = daemons;
    this.loading = false;
  }

  addDaemon() {
    const attachEnv = this.shadowRoot?.getElementById(
      'add-daemon-dialog'
    ) as PaperDialogElement;
    attachEnv.open();
  }
}
