import { css, PropertyValueMap, TemplateResult } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../components/add-edit-access-control';
import '../components/environment-tabs/env-control-center';
import { Router } from '@vaadin/router/dist/vaadin-router';
import { Tabs } from '@vaadin/tabs';
import { PageElement } from '../helpers/page-element';
import { EnvironmentApiModel } from '../apis/dorc-api';
import { PageEnvBase } from '../components/environment-tabs/page-env-base';
import { SuccessNotification } from '../components/notifications/success-notification';

@customElement('page-environment')
export class PageEnvironment extends PageElement {
  @property() environmentName = '';
  @property() parentName = '';

  private tabId = -1;

  @property({ type: Array }) private tabNames = [
    'metadata',
    'control-center',
    'variables',
    'servers',
    'databases',
    'projects',
    'daemons',
    'deployments',
    'users',
    'delegated-users'
  ];

  @property({ type: Boolean }) private loading = true;

  static get styles() {
    return css`
      :host {
        display: inline-block;
        height: 100vh - 70px;
        width: 100%;
        overflow: hidden;
      }
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

  render() {
    return html`
      <table style="margin-left: auto; margin-right: auto;">
        <tr>
          <td>
            <h2 style="text-align: center;">${this.environmentName}</h2>
          </td>
          <td>
            ${this.parentName ? html`<vaadin-icon icon="vaadin:child" title="Child of ${this.parentName}"></vaadin-icon>` : html``}
          </td>
          <td>
            ${this.loading ? html` <div class="small-loader"></div> ` : html``}
          </td>
        </tr>
      </table>

      <vaadin-tabs
        id="env-tabs"
        theme="centered"
        selected="${this.tabId}"
        @selected-changed="${this.selectedChanged}"
      >
        ${this.tabNames.map(tabName => this.convertUriToHuman(tabName))}
      </vaadin-tabs>
      <vaadin-vertical-layout style="padding: 0px; height: 100%">
        <slot @slotchange=${this.handleSlotChange}></slot>
      </vaadin-vertical-layout>
    `;
  }

  protected firstUpdated(
    _changedProperties: PropertyValueMap<any> | Map<PropertyKey, unknown>
  ): void {
    super.firstUpdated(_changedProperties);

    this.addEventListener(
      'environment-details-updated',
      this.environmentDetailsUpdated as EventListener
    );
    this.addEventListener(
      'environment-loading',
      this.environmentLoading as EventListener
    );
    this.addEventListener(
      'environment-loaded',
      this.environmentLoaded as EventListener
    );

    const tabName = location.pathname.split('/')[3];
    if (tabName) this.tabId = this.tabNames.findIndex(p => p === tabName);
    else this.tabId = 0;

    const tabs = this.shadowRoot?.getElementById('env-tabs') as unknown as Tabs;
    if (tabs) {
      tabs.selected = this.tabId;
    }
  }

  environmentLoading() {
    this.loading = true;
  }

  environmentLoaded(e: CustomEvent) {
    const env = e.detail.environment as EnvironmentApiModel;
    this.environmentName = env.EnvironmentName ?? '';
    this.parentName = env.ParentEnvironment?.EnvironmentName ?? '';
    this.loading = false;
  }

  environmentDetailsUpdated() {
    const msg = `metadata saved for environment ${this.environmentName}`;
    const notification = new SuccessNotification();
    notification.setAttribute('successMessage', msg);
    this.shadowRoot?.appendChild(notification);
    notification.open();
  }

  handleSlotChange(e: Event) {
    const slot = e.target as HTMLSlotElement;
    const childNodes: Node[] = slot?.assignedNodes({ flatten: true });
    const envTabs = childNodes as PageEnvBase[];
    envTabs.forEach(value => {
      value.slotChangeComplete();
    });
  }

  convertUriToHuman(tabName: string): TemplateResult {
    if (this.environmentName?.toLowerCase().indexOf('endur') === -1) {
      if (tabName === 'users' || tabName === 'delegated-users') return html``;
    }

    let newTabName: string;
    newTabName = tabName.replace('-', ' ');

    const re = /(\b[a-z](?!\s))/g;
    newTabName = newTabName.replace(re, x => x.toUpperCase());

    return html`<vaadin-tab>${newTabName}</vaadin-tab>`;
  }

  selectedChanged(e: CustomEvent) {
    if (e.detail.value < 0) return;

    const tabIdx = e.detail.value as number;
    let envName = this.environmentName;
    if (envName === '') {
      envName = location.pathname.split('/')[2];
      this.environmentName = decodeURIComponent(envName);
    }

    const pathStart = `/environment/${envName}/`;

    const tabName = this.tabNames[tabIdx];
    this.tabId = tabIdx;
    if (tabName === location.pathname.split('/')[3]) {
      return;
    }

    if (tabName !== '') {
      Router.go(pathStart + tabName);
      console.log(`Telling router to go to ${tabName}`);
    }
  }
}
