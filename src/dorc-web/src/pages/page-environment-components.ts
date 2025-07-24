import { css, PropertyValueMap, TemplateResult } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { Router } from '@vaadin/router';
import { Tabs } from '@vaadin/tabs';
import { PageElement } from '../helpers/page-element';
import { PageEnvBase } from '../components/environment-tabs/page-env-base';

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
      <h3 style="text-align: center; margin: 10px 0;">Components</h3>
      
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

    // Extract the component tab name from the URL: /environment/{env}/components/{component}
    const pathParts = location.pathname.split('/');
    const componentTabName = pathParts[4]; // components is at index 3, component name at index 4
    if (componentTabName) {
      const foundIndex = this.tabNames.findIndex(p => p === componentTabName);
      this.tabId = foundIndex >= 0 ? foundIndex : 0;
    } else {
      // If no component specified, default to servers and redirect
      this.tabId = 0;
      const envName = pathParts[2];
      if (envName) {
        Router.go(`/environment/${envName}/components/servers`);
        return;
      }
    }

    const tabs = this.shadowRoot?.getElementById('component-tabs') as unknown as Tabs;
    if (tabs) {
      tabs.selected = this.tabId;
    }
  }

  handleSlotChange(e: Event) {
    const slot = e.target as HTMLSlotElement;
    const childNodes: Node[] = slot?.assignedNodes({ flatten: true });
    const envTabs = childNodes as PageEnvBase[];
    envTabs.forEach(value => {
      value.slotChangeComplete();
    });
  }

  convertUriToHuman(tabName: EnvComponentTabNames): TemplateResult {
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
      // Extract from URL if not set
      envName = location.pathname.split('/')[2];
      this.environmentName = decodeURIComponent(envName);
    }

    const pathStart = `/environment/${envName}/components/`;
    const tabName = this.tabNames[tabIdx];
    this.tabId = tabIdx;
    
    // Check if we're already on this tab
    if (tabName === location.pathname.split('/')[4]) {
      return;
    }

    Router.go(pathStart + tabName);
    console.log(`Telling router to go to components/${tabName}`);
  }
}