import { css } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../components/chart/hegs-chart';
import { EChartOption, EChartTitleOption } from 'echarts';
import '@vaadin/checkbox';
import { Checkbox } from '@vaadin/checkbox/src/vaadin-checkbox';
import { PageElement } from '../helpers/page-element';
import {
  AnalyticsDeploymentsDateApi,
  AnalyticsDeploymentsMonthApi
} from '../apis/dorc-api';
import { AnalyticsDeploymentsPerProjectApiModel } from '../apis/dorc-api';
import SeriesPie = echarts.EChartOption.SeriesPie;
import Tooltip = echarts.EChartOption.Tooltip;
import DataObject = echarts.EChartOption.SeriesPie.DataObject;
import SeriesThemeRiver = echarts.EChartOption.SeriesThemeRiver;
import SingleAxis = echarts.EChartOption.SingleAxis;
import Format = echarts.EChartOption.Tooltip.Format;

interface ProjectDeployments {
  project: string;
  numDeployments: number;
}

@customElement('page-about')
export class PageAbout extends PageElement {
  @property({ type: Array })
  AnalyticsDeploymentsMonthResponse: AnalyticsDeploymentsPerProjectApiModel[] =
    [];

  @property({ type: Array })
  AnalyticsDeploymentsDateResponse: AnalyticsDeploymentsPerProjectApiModel[] =
    [];

  @property({ type: Object }) top3PieChartOptions:
    | EChartOption<SeriesPie>
    | undefined;

  @property({ type: Object }) pieChartOptions:
    | EChartOption<SeriesPie>
    | undefined;

  @property({ type: Object }) riverChartOptions:
    | EChartOption<SeriesThemeRiver>
    | undefined;

  @property({ type: Array }) pieDataTable: (string | number)[][] = [
    ['Project', 'Deployments']
  ];

  @property({ type: Array }) allDeploymentsMonth: (Date | number)[][] = [];

  @property({ type: Array })
  top3ProjectsByDeployments: ProjectDeployments[] = [];

  @property({ type: Number }) DeploymentsThisYear = 0;

  @property({ type: Number }) TotalDeployments = 0;

  @property({ type: Number }) TotalDeploymentsThisYear = 0;

  @property({ type: Number }) AverageDeploymentsPerDay = 0;

  @property({ type: Number }) MaxDeploymentsThisYear = 0;

  @property({ type: Number }) TotalFailedDeploymentsThisYear = 0;

  @property({ type: Number }) PercentTotalFailedDeploymentsThisYear = 0;

  @property({ type: Number }) PercentTop3ProjectsByDeploymentsThisYear = 0;

  private loading = true;

  @property({ type: Boolean }) private includeDeprecated = true;

  constructor() {
    super();

    this.loadMonthData();
    this.loadDayData();
  }

  static get styles() {
    return css`
      .page-about {
        padding: 1rem;
      }

      .page-about__main-info {
        margin-bottom: 18px;
      }

      .card-element {
        padding: 10px;
        box-shadow: 1px 2px 3px rgba(0, 0, 0, 0.2);
      }
      .card-element__heading {
        color: #ff3131;
      }
      .card-element__text {
        color: gray;
      }

      .statistics-cards {
        max-width: 500px;
        display: flex;
        flex-wrap: wrap;
      }
      .statistics-cards__item {
        margin: 5px;
        flex-shrink: 0;
      }

      #chart-all {
        margin: 0px;
        padding: 0px;
        height: 100vh;
        width: 100%;
      }

      .main-info {
        display: flex;
      }

      .top3-chart-block {
        padding: 26px;
        box-shadow: 1px 2px 3px rgba(0, 0, 0, 0.2);
      }

      .top3-chart-block__percent {
        float: right;
        vertical-align: middle;
      }
      .loader {
        border: 16px solid #f3f3f3; /* Light grey */
        border-top: 16px solid #3498db; /* Blue */
        border-radius: 50%;
        width: 120px;
        height: 120px;
        animation: spin 2s linear infinite;
      }
        
      div#page_div {
        overflow: auto;
        width: calc(100% - 4px);
        height: calc(100vh - 50px);
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
      ${this.loading
        ? html` <div class="loader"></div> `
        : html`
          <div id="page_div">
            <div class="page-about__main-info main-info">
              <div class="statistics-cards">
                <div class="statistics-cards__item card-element">
                  <h3>${this.TotalDeployments}</h3>
                  <span class="card-element__text">Total # deployments</span>
                </div>
                <div class="statistics-cards__item card-element">
                  <h3>${this.TotalDeploymentsThisYear}</h3>
                  <span class="card-element__text"
                    >Total # deployments this year</span
                  >
                </div>
                <div class="statistics-cards__item card-element">
                  <h3>${this.AverageDeploymentsPerDay}</h3>
                  <span class="card-element__text"
                    >Average Deployments Per Day</span
                  >
                </div>
                <div class="statistics-cards__item card-element">
                  <h3>${this.MaxDeploymentsThisYear}</h3>
                  <span class="card-element__text">Busiest Week Of Year</span>
                </div>
                <div class="statistics-cards__item card-element">
                  <h3>${this.TotalFailedDeploymentsThisYear}</h3>
                  <span class="card-element__text"
                    >Total Failures This Year</span
                  >
                </div>
              </div>
              <div class="top3-chart-block">
                <hegs-chart
                  style="display: block; width: 600px; height: 400px;"
                  .option="${this.top3PieChartOptions}"
                ></hegs-chart>
              </div>
            </div>
            <div class="statistics-cards__item card-element">
              <hegs-chart
                style="display: block; width: 100%; height: 1200px;"
                .option="${this.riverChartOptions}"
              ></hegs-chart>
              <vaadin-checkbox
                label="Include Deprecated"
                ?checked="${this.includeDeprecated}"
                @change="${this.updateDeprecated}"
              ></vaadin-checkbox>
            </div>
            <div class="statistics-cards__item card-element">
              <hegs-chart
                style="display: block; width: 100%; height: 1200px;"
                .option="${this.pieChartOptions}"
              ></hegs-chart>
            </div>
          </div>
          `}
    `;
  }

  updateDeprecated(e: CustomEvent) {
    const cbx = e.target as Checkbox;
    this.constructRiverChart(!cbx.checked);
  }

  loadMonthData() {
    const api = new AnalyticsDeploymentsMonthApi();
    api
      .analyticsDeploymentsMonthGet()
      .subscribe((res: AnalyticsDeploymentsPerProjectApiModel[]) => {
        this.AnalyticsDeploymentsMonthResponse = res;
        this.constructRiverChart(!this.includeDeprecated);
        this.constructPieChart();
      });
  }

  private constructRiverChart(excludeDeprecated: boolean) {
    const title: EChartTitleOption = {
      text: 'Deployments By Project'
    };

    const tt: Tooltip = {
      trigger: 'axis',
      axisPointer: {
        type: 'line',
        lineStyle: {
          color: 'rgba(0,0,0,0.2)',
          width: 1,
          type: 'solid'
        }
      },
      formatter(params) {
        let output = '';
        if (params instanceof Array) {
          params = params.sort((a: Format, b: Format): number => {
            if (String(a.data[2]) > String(b.data[2])) return 1;
            return -1;
          });
          for (let i = 0; i < params.length; i += 1) {
            const ttFormat = params[i];
            if (Number(ttFormat.data[1]) === 0) {
              continue;
            }
            const current = `${ttFormat.data[2]} : ${ttFormat.data[1]} </br>`;
            output += current;
          }
        }
        return output;
      }
    };

    const singleAxis: SingleAxis = {
      axisTick: {},
      axisLabel: {},
      type: 'time',
      axisPointer: {
        label: {
          show: true
        }
      },
      splitLine: {
        show: true,
        lineStyle: {
          type: 'dashed',
          opacity: 0.2
        }
      }
    };

    const data: (string | number | undefined)[][] = [];

    this.AnalyticsDeploymentsMonthResponse.map(m => {
      const date: string = `${String(m.Year)}/${String(m.Month)}/${String(1)}`;
      const dataItem: (string | number | undefined)[] = [
        date,
        m.CountOfDeployments,
        m.ProjectName ?? ''
      ];
      if (
        excludeDeprecated &&
        String(dataItem[2]).toLowerCase().includes('deprecated')
      ) {
        return;
      }
      data.push(dataItem);
    });

    const series: SeriesThemeRiver[] = [
      {
        type: 'themeRiver',
        emphasis: {
          itemStyle: {
            shadowBlur: 20,
            shadowColor: 'rgba(0, 0, 0, 0.8)'
          }
        },
        label: { show: false },
        data
      }
    ];

    this.riverChartOptions = {
      tooltip: tt,
      title,
      singleAxis,
      series
    };
  }

  loadDayData() {
    const api = new AnalyticsDeploymentsDateApi();
    api
      .analyticsDeploymentsDateGet()
      .subscribe((res: AnalyticsDeploymentsPerProjectApiModel[]) => {
        this.AnalyticsDeploymentsDateResponse = res;
        this.dayProcessor();
        this.loading = false;
      });
  }

  dayProcessor() {
    const top3ThisYear = new Map<string, number>();
    const today = new Date();
    const todaysYear = today.getFullYear();

    this.AnalyticsDeploymentsDateResponse.forEach(
      ({
        CountOfDeployments,
        Failed,
        ProjectName,
        Year
      }: AnalyticsDeploymentsPerProjectApiModel) => {
        if (
          CountOfDeployments !== undefined &&
          Failed !== undefined &&
          ProjectName !== undefined
        ) {
          this.TotalDeployments += CountOfDeployments;

          if (todaysYear === Year) {
            this.TotalDeploymentsThisYear += CountOfDeployments;
            this.TotalFailedDeploymentsThisYear += Failed;

            if (CountOfDeployments > this.MaxDeploymentsThisYear) {
              this.MaxDeploymentsThisYear = CountOfDeployments;
            }

            const proj = top3ThisYear.get(ProjectName ?? '');
            if (proj === undefined)
              top3ThisYear.set(ProjectName ?? '', CountOfDeployments);
            else top3ThisYear.set(ProjectName ?? '', proj + CountOfDeployments);
          }
        }
      }
    );

    this.PercentTotalFailedDeploymentsThisYear = Math.round(
      (this.TotalFailedDeploymentsThisYear / this.TotalDeploymentsThisYear) *
        100
    );
    const sortable: { project: string; total: number }[] = [];

    top3ThisYear.forEach((value, key) => {
      sortable.push({ project: key, total: value });
    });

    sortable.sort((a, b) => a.total - b.total);

    let countTop3DeploymentsByProject = 0;
    for (let k = 0; k < min(3, sortable.length); k += 1) {
      const node = {
        project: sortable[sortable.length - k - 1]?.project,
        numDeployments: sortable[sortable.length - k - 1]?.total
      };
      this.top3ProjectsByDeployments.push(node);
      countTop3DeploymentsByProject += node.numDeployments;
    }

    this.PercentTop3ProjectsByDeploymentsThisYear = Math.round(
      (countTop3DeploymentsByProject / this.TotalDeploymentsThisYear) * 100
    );
    this.AverageDeploymentsPerDay = Math.round(
      this.TotalDeploymentsThisYear /
        this.days_between(new Date(today.getFullYear(), 0o1, 0o1), today)
    );

    this.top3ProjectsByDeployments.forEach(e => {
      this.pieDataTable.push([
        e.project,
        e.numDeployments
      ]);
    });
    
    const model: [][] = JSON.parse(JSON.stringify(this.pieDataTable));
    this.pieDataTable = model;
    this.constructTop3PieChart();

    console.log('completed the days processor');
  }

  private constructTop3PieChart() {
    const tt: Tooltip = {
      trigger: 'item'
    };
    const title: EChartTitleOption = {
      text: 'Top 3 Total Deployments By Project This Year',
      subtext: `${String(this.PercentTop3ProjectsByDeploymentsThisYear)}%`
    };
    const data: DataObject[] = [];

    this.top3ProjectsByDeployments.forEach(value =>
      data.push({ name: value.project, value: value.numDeployments })
    );

    const series: SeriesPie[] = [
      {
        type: 'pie',
        data
      }
    ];

    this.top3PieChartOptions = {
      tooltip: tt,
      title,
      series
    };
  }

  private constructPieChart() {
    const tt: Tooltip = {
      trigger: 'item'
    };
    const title: EChartTitleOption = {
      text: 'Total Deployments By Project This Year',
      subtext: 'Not including top 3'
    };
    const data: DataObject[] = [];

    const currentYear = new Date().getFullYear();

    const justThisYear = this.AnalyticsDeploymentsMonthResponse.filter(
      d => d.Year === currentYear
    );

    const distinctProjects = [
      ...new Set(justThisYear.map(x => x.ProjectName ?? ''))
    ].sort();

    const summedProjects: AnalyticsDeploymentsPerProjectApiModel[] = [];

    distinctProjects.map(x => {
      const sum = justThisYear.reduce((accumulator, object) => {
        const deployments = object.CountOfDeployments ?? 0;
        if (object.ProjectName !== x) return accumulator;
        return accumulator + deployments;
      }, 0);
      const summed: AnalyticsDeploymentsPerProjectApiModel = {
        ProjectName: x,
        CountOfDeployments: sum
      };
      summedProjects.push(summed);
    });

    const sortedNSummed = summedProjects.sort((a, b) => {
      const first = a.CountOfDeployments ?? 0;
      const second = b.CountOfDeployments ?? 0;
      return first - second;
    });

    const sortedNSummedNoTop3 = sortedNSummed.splice(
      0,
      sortedNSummed.length - 3
    );

    sortedNSummedNoTop3.forEach(value =>
      data.push({
        name: value.ProjectName ?? '',
        value: value.CountOfDeployments
      })
    );

    const series: SeriesPie[] = [
      {
        type: 'pie',
        data
      }
    ];

    this.pieChartOptions = {
      tooltip: tt,
      title,
      series
    };
  }

  days_between(date1: Date, date2: Date) {
    // The number of milliseconds in one day
    const ONE_DAY = 1000 * 60 * 60 * 24;

    // Convert both dates to milliseconds
    const date1Ms = date1.getTime();
    const date2Ms = date2.getTime();

    // Calculate the difference in milliseconds
    const differenceMs = Math.abs(date1Ms - date2Ms);

    // Convert back to days and return
    return Math.round(differenceMs / ONE_DAY);
  }
}
function min(arg0: number, arg1: number) {
  if (arg0 < arg1) return arg0;
  return arg1;
}

