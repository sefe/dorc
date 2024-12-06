import { EChartOption, ECharts, EChartsResponsiveOption } from 'echarts';
import { css, LitElement, PropertyValues } from 'lit';
import { customElement } from 'lit/decorators.js';
import { html } from 'lit/html.js';

@customElement('hegs-chart')
export class HegsChart extends LitElement {
  private chart: ECharts | undefined;

  private _option!: EChartOption | EChartsResponsiveOption;

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
    return css``;
  }

  render() {
    return html`<div id="container" style="width: 100%; height: 100%;"></div>`;
  }

  static get observedAttributes() {
    return ['style', 'option'];
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    const container = this.shadowRoot?.querySelector(
      '#container'
    ) as HTMLDivElement;
    this.chart = echarts.init(container);
    this.updateChart();
  }

  disconnectedCallback() {
    const container = this.shadowRoot?.querySelector(
      '#container'
    ) as HTMLDivElement;
    if (container) {
      container.innerHTML = '';
    }
    if (this.chart) {
      echarts.dispose(container);
    }
  }

  attributeChangedCallback(name: string, _oldValue: any, newValue: any) {
    if (name === 'option') {
      this.updateChart();
    } else if (name === 'style') {
      const container = this.shadowRoot?.querySelector(
        '#container'
      ) as HTMLDivElement;
      if (container) {
        container.setAttribute('style', newValue);
      }
      this.resizeChart();
    }
  }

  updateChart() {
    if (!this.chart) return;
    this.chart.setOption(this.option);
  }

  resizeChart() {
    if (!this.chart) return;
    this.chart.resize();
  }
}
