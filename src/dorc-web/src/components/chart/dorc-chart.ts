import {ECharts, EChartsOption} from 'echarts';
import * as echarts from 'echarts';
import { css, LitElement, PropertyValues } from 'lit';
import { customElement } from 'lit/decorators.js';
import { html } from 'lit/html.js';

@customElement('dorc-chart')
export class DorcChart extends LitElement {
  private chart: ECharts | undefined;

  private _option: EChartsOption | undefined;

  private _resizeObserver: ResizeObserver | undefined;

  get option(): EChartsOption | undefined {
    return this._option;
  }

  set option(val: EChartsOption | undefined) {
    this._option = val;
    this.updateChart();
  }

  static get styles() {
    return css`
      :host {
        display: block;
      }
      #container {
        width: 100%;
        height: 100%;
      }
    `;
  }

  render() {
    return html`<div id="container"></div>`;
  }

  private isDarkMode(): boolean {
    return document.documentElement.getAttribute('theme')?.includes('dark') ?? false;
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);
    this._initChart();
  }

  connectedCallback() {
    super.connectedCallback();
    // After disconnect, we dispose ECharts and null `chart`; on reconnect
    // firstUpdated does not re-fire, so we re-init here once the shadow
    // root + #container are present. Guard against double-init on the
    // initial mount (firstUpdated will run after this).
    if (!this.chart && this.shadowRoot?.querySelector('#container')) {
      this._initChart();
    }
  }

  private _initChart() {
    const container = this.shadowRoot?.querySelector(
      '#container'
    ) as HTMLDivElement | null;
    if (!container || this.chart) return;
    this.chart = echarts.init(container, this.isDarkMode() ? 'dark' : undefined);
    this.updateChart();

    this._resizeObserver = new ResizeObserver(() => {
      this.resizeChart();
    });
    this._resizeObserver.observe(container);
  }

  disconnectedCallback() {
    super.disconnectedCallback();
    this._resizeObserver?.disconnect();
    this._resizeObserver = undefined;
    if (this.chart) {
      this.chart.dispose();
      this.chart = undefined;
    }
  }

  updateChart() {
    if (!this.chart || !this.option) return;
    this.chart.setOption({ ...this.option, backgroundColor: 'transparent' });
  }

  resizeChart() {
    if (!this.chart) return;
    this.chart.resize();
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'dorc-chart': DorcChart;
  }
}
