import {ECharts, EChartsOption} from 'echarts';
import * as echarts from 'echarts';
import { css, LitElement, PropertyValues } from 'lit';
import { customElement } from 'lit/decorators.js';
import { html } from 'lit/html.js';

@customElement('hegs-chart')
export class HegsChart extends LitElement {
  private chart: ECharts | undefined;

  private _option!: EChartsOption;

  private _resizeObserver: ResizeObserver | undefined;

  get option() {
    return this._option;
  }

  set option(val) {
    const oldVal = this._option;
    this._option = val;
    this.requestUpdate('option', oldVal);
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

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    const container = this.shadowRoot?.querySelector(
      '#container'
    ) as HTMLDivElement;
    this.chart = echarts.init(container);
    this.updateChart();

    this._resizeObserver = new ResizeObserver(() => {
      this.resizeChart();
    });
    this._resizeObserver.observe(container);
  }

  disconnectedCallback() {
    super.disconnectedCallback();
    this._resizeObserver?.disconnect();
    if (this.chart) {
      this.chart.dispose();
      this.chart = undefined;
    }
  }

  updateChart() {
    if (!this.chart || !this.option) return;
    this.chart.setOption(this.option);
  }

  resizeChart() {
    if (!this.chart) return;
    this.chart.resize();
  }
}
