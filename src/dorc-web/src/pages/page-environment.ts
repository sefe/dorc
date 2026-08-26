import { css, PropertyValueMap, TemplateResult } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../components/add-edit-access-control';
import { Router } from '@vaadin/router';
import { Tabs } from '@vaadin/tabs';
import { PageElement } from '../helpers/page-element';
import { EnvironmentApiModel } from '../apis/dorc-api';
import { PageEnvBase } from '../components/environment-tabs/page-env-base';
import { SuccessNotification } from '../components/notifications/success-notification';

export enum EnvPageTabNames {
  Metadata = 'metadata',
  Variables = 'variables',
  Components = 'components',
  Projects = 'projects',
  Deployments = 'deployments',
  Tenants = 'tenants',
  Monitor = 'monitor',
  Users = 'users',
}

@customElement('page-environment')
export class PageEnvironment extends PageElement {
  @property() environmentName = '';
  @property() parentName = '';

  private tabId = -1;

  /**
   * D-26: render and index/route mapping now derive from ONE list.
   *
   * Previously `tabNames` was the full enum while `convertUriToHuman` returned an
   * empty template for `Users` on non-Endur environments — so the rendered tab
   * count (7) and the indexed list (8) disagreed. That only lined up by accident
   * because `Users` is declared last; any member added after it would silently
   * shift every tab-to-route mapping. A deep link to a hidden tab also set
   * `selected` out of range, leaving nothing highlighted.
   *
   * The `endur` name check itself is gone — it was an old hard-coding.
   */
  private get tabNames(): EnvPageTabNames[] {
    return Object.values(EnvPageTabNames);
  }

  @property({ type: Boolean }) private loading = true;

  @property({ type: Boolean }) private notFound = false;

  static get styles() {
    return css`
      :host {
        height: 100%;
        width: 100%;
        overflow: hidden;
        display: flex;
        flex-direction: column;
      }
      /* D-21: was a three-cell layout <table>, which screen readers announce as
         a data table ("table, 1 row, 3 columns") for what is a visual header. */
      .env-header {
        display: flex;
        align-items: center;
        justify-content: center;
        gap: var(--lumo-space-s);
      }

      .env-header h2 {
        text-align: center;
        margin: var(--lumo-space-s) 0;
      }

      /* D-22: the ring was hardcoded #f3f3f3/#3498db, which renders as a glaring
         near-white circle against the dark theme's #1e1e1e, and it announced
         nothing. Themed and given a status role below.

         Deliberately NOT dorc-spinner: that component is a fixed, full-viewport
         overlay (position:fixed, 100%x100%), so using it for a 12px indicator
         beside a heading would cover the page. This is the plan's stated
         fallback — tokens plus a status role — not a shortcut past it. */
      .small-loader {
        border: 2px solid var(--dorc-border-color);
        border-top: 2px solid var(--dorc-link-color);
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

      @media (prefers-reduced-motion: reduce) {
        .small-loader {
          animation: none;
        }
      }
    `;
  }

  render() {
    if (this.notFound) {
      // D-36: this used to render nothing at all — a blank pane, with the drawer
      // still highlighting the shortcut and no indication of what happened.
      return html`
        <div role="alert" style="padding: var(--lumo-space-l); text-align: center;">
          <h2>Environment not found</h2>
          <p>
            The environment
            ${this.environmentName ? html`<b>${this.environmentName}</b>` : html`requested`}
            no longer exists, or you do not have access to it.
          </p>
          <a class="plain" href="/environments">Back to Environments</a>
        </div>
      `;
    }
    return html`
      <div class="env-header">
        <!-- aria-live: the name is empty on first paint and only arrives when the
             async load lands, so without this it is never announced (D-21). -->
        <h2 aria-live="polite">${this.environmentName}</h2>
        ${this.parentName
          ? html`<vaadin-icon
              icon="vaadin:child"
              title="Child of ${this.parentName}"
              style="color: grey"
            ></vaadin-icon>`
          : html``}
        ${this.loading
          ? html`<div
              class="small-loader"
              role="status"
              aria-label="Loading environment"
            ></div>`
          : html``}
      </div>

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
    this.addEventListener(
      'environment-not-found',
      this.environmentNotFound as EventListener
    );
    this.addEventListener(
      'environment-renamed',
      this.environmentRenamed as EventListener
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

  environmentNotFound() {
    this.notFound = true;
    this.loading = false;
  }

  environmentRenamed(e: CustomEvent) {
    const env = e.detail.environment as EnvironmentApiModel;
    this.environmentName = env.EnvironmentName ?? '';
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
    childNodes.forEach(node => {
      if (node instanceof HTMLElement && 'slotChangeComplete' in node) {
        (node as PageEnvBase).slotChangeComplete();
      }
    });
  }

  convertUriToHuman(tabName: EnvPageTabNames): TemplateResult {
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
      const segment = location.pathname.split('/')[2] ?? '';
      try {
        envName = decodeURIComponent(segment);
      } catch {
        envName = segment;
      }
      this.environmentName = envName;
    }

    // D-12: raw interpolation meant an environment named e.g. "Perf 100% Load"
    // produced a path that threw URIError when decoded for matching, and "Dev#2"
    // routed to the wrong page entirely because #2/... was parsed as a fragment.
    const pathStart = `/environment/${encodeURIComponent(envName)}/`;

    const tabName = this.tabNames[tabIdx];
    this.tabId = tabIdx;
    if (tabName === location.pathname.split('/')[3]) {
      return;
    }

    Router.go(pathStart + tabName);
    console.log(`Telling router to go to ${tabName}`);
  }
}
