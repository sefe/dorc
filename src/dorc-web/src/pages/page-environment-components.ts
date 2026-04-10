import { css, PropertyValueMap, TemplateResult } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { Router, RouterLocation } from '@vaadin/router';
import { Tabs } from '@vaadin/tabs';
import { PageElement } from '../helpers/page-element';
import { PageEnvBase } from '../components/environment-tabs/page-env-base';
import { RouteMeta } from '../router/routes';

export enum EnvComponentTabNames {
  Servers = 'servers',
  Databases = 'databases',
  Daemons = 'daemons',
  Containers = 'containers',
  Cloud = 'cloud',
  APIs = 'apis'
}

@customElement('page-environment-components')
export class PageEnvironmentComponents extends PageElement {
  @property() environmentName = '';

  private tabId = -1;

  private tabNames = Object.values(EnvComponentTabNames);

  static get styles() {
    return css`
      :host {
        height: 100%;
        width: 100%;
        overflow: hidden;
        display: flex;
        flex-direction: column;
      }
    `;
  }

  render() {
    return html`
      <vaadin-tabs
        id="component-tabs"
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

    const pathParts = location.pathname.split('/');
    const envName = pathParts[2];
    if (envName) {
      this.environmentName = this.safeDecodeURI(envName);
    }

    // URL: /environment/{env}/components/{component}
    const componentTabName = pathParts[4];
    if (componentTabName) {
      const foundIndex = this.tabNames.findIndex(p => p === componentTabName);
      this.tabId = foundIndex >= 0 ? foundIndex : 0;
    } else {
      this.tabId = 0;
      if (envName) {
        Router.go(`/environment/${envName}/components/servers`);
        return;
      }
    }

    const tabs = this.shadowRoot?.getElementById(
      'component-tabs'
    ) as unknown as Tabs;
    if (tabs) {
      tabs.selected = this.tabId;
    }
  }

  public slotChangeComplete() {
    // No-op — required by page-environment's handleSlotChange
  }

  public onAfterEnter(location: RouterLocation<RouteMeta>) {
    this.location = location;

    const pathParts = location.pathname.split('/');
    const envName = pathParts[2];
    if (envName) {
      this.environmentName = this.safeDecodeURI(envName);
    }
    const componentTabName = pathParts[4];

    if (!componentTabName && envName) {
      Router.go(`/environment/${envName}/components/servers`);
      return;
    }

    if (componentTabName) {
      const foundIndex = this.tabNames.findIndex(p => p === componentTabName);
      if (foundIndex >= 0 && foundIndex !== this.tabId) {
        this.tabId = foundIndex;
        const tabs = this.shadowRoot?.getElementById(
          'component-tabs'
        ) as unknown as Tabs;
        if (tabs) {
          tabs.selected = this.tabId;
        }
      }
    }
  }

  handleSlotChange(e: Event) {
    const slot = e.target as HTMLSlotElement;
    const childNodes: Node[] = slot?.assignedNodes({ flatten: true });
    childNodes.forEach(node => {
      if (
        node instanceof HTMLElement &&
        'slotChangeComplete' in node
      ) {
        (node as PageEnvBase).slotChangeComplete();
      }
    });
  }

  private static readonly displayNames: Record<string, string> = {
    apis: 'APIs'
  };

  convertUriToHuman(tabName: EnvComponentTabNames): TemplateResult {
    const override =
      PageEnvironmentComponents.displayNames[tabName];
    if (override) return html`<vaadin-tab>${override}</vaadin-tab>`;
    let newTabName: string = tabName.replace('-', ' ');
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
      this.environmentName = this.safeDecodeURI(envName);
    }

    const tabName = this.tabNames[tabIdx];
    this.tabId = tabIdx;

    if (tabName === location.pathname.split('/')[4]) {
      return;
    }

    Router.go(`/environment/${envName}/components/${tabName}`);
  }

  private safeDecodeURI(value: string): string {
    try {
      return decodeURIComponent(value);
    } catch {
      return value;
    }
  }
}
