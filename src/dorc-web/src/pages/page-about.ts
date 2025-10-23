import { css } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../components/chart/hegs-chart';
import '@vaadin/checkbox';
import { Checkbox } from '@vaadin/checkbox/src/vaadin-checkbox';
import { PageElement } from '../helpers/page-element';
import {
  AnalyticsDeploymentsDateApi,
  AnalyticsDeploymentsMonthApi,
  AnalyticsEnvironmentUsageApi,
  AnalyticsUserActivityApi,
  AnalyticsTimePatternApi,
  AnalyticsComponentUsageApi,
  AnalyticsDurationApi
} from '../apis/dorc-api';
import {
  AnalyticsDeploymentsPerProjectApiModel,
  AnalyticsEnvironmentUsageApiModel,
  AnalyticsUserActivityApiModel,
  AnalyticsTimePatternApiModel,
  AnalyticsComponentUsageApiModel,
  AnalyticsDurationApiModel
} from '../apis/dorc-api';
import type {
  EChartsOption,
  TitleComponentOption,
  TooltipComponentOption,
  SingleAxisComponentOption,
  GridComponentOption,
  XAXisComponentOption,
  YAXisComponentOption
} from 'echarts';
import type {
  PieSeriesOption,
  ThemeRiverSeriesOption,
  BarSeriesOption
} from 'echarts';
import {
  CallbackDataParams,
  TopLevelFormatterParams
} from 'echarts/types/dist/shared';
import {
  OptionDataValueDate,
  OptionDataValueNumeric
} from 'echarts/types/src/util/types';

declare type ThemerRiverDataItem = [
  OptionDataValueDate,
  OptionDataValueNumeric,
  string
];

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

  @property({ type: Object }) top3PieChartOptions: EChartsOption | undefined;

  @property({ type: Object }) pieChartOptions: EChartsOption | undefined;

  @property({ type: Object }) riverChartOptions: EChartsOption | undefined;

  @property({ type: Object }) environmentUsageChartOptions:
    | EChartsOption
    | undefined;

  @property({ type: Object }) userActivityChartOptions:
    | EChartsOption
    | undefined;

  @property({ type: Object }) timePatternChartOptions:
    | EChartsOption
    | undefined;

  @property({ type: Object }) componentUsageChartOptions:
    | EChartsOption
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

  @property({ type: Object }) durationStats:
    | AnalyticsDurationApiModel
    | undefined;

  private loading = true;

  @property({ type: Boolean }) private includeDeprecated = true;

  constructor() {
    super();

    this.loadMonthData();
    this.loadDayData();
    this.loadNewAnalytics();
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
                  <div class="statistics-cards__item card-element">
                    <h3>
                      ${this.durationStats?.AverageDurationMinutes?.toFixed(
                        1
                      ) ?? 0}
                      min
                    </h3>
                    <span class="card-element__text"
                      >Average Deployment Duration</span
                    >
                  </div>
                  <div class="statistics-cards__item card-element">
                    <h3>
                      ${this.durationStats?.MaxDurationMinutes?.toFixed(1) ?? 0}
                      min
                    </h3>
                    <span class="card-element__text">Longest Deployment</span>
                  </div>
                  <div class="statistics-cards__item card-element">
                    <h3>
                      ${this.durationStats?.MinDurationMinutes?.toFixed(1) ?? 0}
                      min
                    </h3>
                    <span class="card-element__text">Shortest Deployment</span>
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
                  style="display: block; width: 100%; height: 600px;"
                  .option="${this.environmentUsageChartOptions}"
                ></hegs-chart>
              </div>
              <div class="statistics-cards__item card-element">
                <hegs-chart
                  style="display: block; width: 100%; height: 600px;"
                  .option="${this.userActivityChartOptions}"
                ></hegs-chart>
              </div>
              <div class="statistics-cards__item card-element">
                <hegs-chart
                  style="display: block; width: 100%; height: 600px;"
                  .option="${this.timePatternChartOptions}"
                ></hegs-chart>
              </div>
              <div class="statistics-cards__item card-element">
                <hegs-chart
                  style="display: block; width: 100%; height: 800px;"
                  .option="${this.componentUsageChartOptions}"
                ></hegs-chart>
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

  loadNewAnalytics() {
    // Load Environment Usage
    const envApi = new AnalyticsEnvironmentUsageApi();
    envApi.analyticsEnvironmentUsageGet().subscribe({
      next: (res: AnalyticsEnvironmentUsageApiModel[]) => {
        this.constructEnvironmentUsageChart(res);
      },
      error: err => {
        console.error('Failed to load environment usage data:', err);
      }
    });

    // Load User Activity
    const userApi = new AnalyticsUserActivityApi();
    userApi.analyticsUserActivityGet().subscribe({
      next: (res: AnalyticsUserActivityApiModel[]) => {
        this.constructUserActivityChart(res);
      },
      error: err => {
        console.error('Failed to load user activity data:', err);
      }
    });

    // Load Time Patterns
    const timeApi = new AnalyticsTimePatternApi();
    timeApi.analyticsTimePatternGet().subscribe({
      next: (res: AnalyticsTimePatternApiModel[]) => {
        console.log('Time pattern data received:', res);
        this.constructTimePatternChart(res);
      },
      error: err => {
        console.error('Failed to load time pattern data:', err);
      }
    });

    // Load Component Usage
    const compApi = new AnalyticsComponentUsageApi();
    compApi.analyticsComponentUsageGet().subscribe({
      next: (res: AnalyticsComponentUsageApiModel[]) => {
        console.log('Component usage data received:', res);
        this.constructComponentUsageChart(res);
      },
      error: err => {
        console.error('Failed to load component usage data:', err);
      }
    });

    // Load Duration Stats
    const durApi = new AnalyticsDurationApi();
    durApi.analyticsDurationGet().subscribe({
      next: (res: AnalyticsDurationApiModel) => {
        this.durationStats = res;
      },
      error: err => {
        console.error('Failed to load duration stats:', err);
      }
    });
  }

  private constructRiverChart(excludeDeprecated: boolean) {
    const title: TitleComponentOption = {
      text: 'Deployments By Project'
    };

    const tt: TooltipComponentOption = {
      trigger: 'axis',
      axisPointer: {
        type: 'line',
        lineStyle: {
          color: 'rgba(0,0,0,0.2)',
          width: 1,
          type: 'solid'
        }
      },
      formatter(params: TopLevelFormatterParams) {
        let output = '';

        if (Array.isArray(params)) {
          let arr = params as CallbackDataParams[];
          arr = arr.sort((a, b) => {
            const aa = a.data as string[];
            const bb = b.data as string[];
            if (String(aa[2] as string) > String(bb[2] as string)) return 1;
            return -1;
          });
          for (let i = 0; i < arr.length; i += 1) {
            const ttFormat = arr[i];
            const tttFormat = ttFormat.data as number[];
            if (Number(tttFormat[1] as number) === 0) {
              continue;
            }
            const current = `${tttFormat[2]} : ${tttFormat[1]} </br>`;
            output += current;
          }
        }
        return output;
      }
    };

    const singleAxis: SingleAxisComponentOption = {
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

    const data: ThemerRiverDataItem[] = [];

    this.AnalyticsDeploymentsMonthResponse.map(m => {
      const date: string = `${String(m.Year)}/${String(m.Month)}/${String(1)}`;
      const dataItem: ThemerRiverDataItem = [
        date,
        m.CountOfDeployments ?? 0,
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

    const series: ThemeRiverSeriesOption[] = [
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
      this.pieDataTable.push([e.project, e.numDeployments]);
    });

    const model: [][] = JSON.parse(JSON.stringify(this.pieDataTable));
    this.pieDataTable = model;
    this.constructTop3PieChart();

    console.log('completed the days processor');
  }

  private constructTop3PieChart() {
    const tt: TooltipComponentOption = {
      trigger: 'item'
    };
    const title: TitleComponentOption = {
      text: 'Top 3 Total Deployments By Project This Year',
      subtext: `${String(this.PercentTop3ProjectsByDeploymentsThisYear)}%`
    };

    const series: PieSeriesOption[] = [
      {
        type: 'pie',
        data: this.top3ProjectsByDeployments.map(value => ({
          name: value.project,
          value: value.numDeployments
        }))
      }
    ];

    this.top3PieChartOptions = {
      tooltip: tt,
      title,
      series
    };
  }

  private constructPieChart() {
    const tt: TooltipComponentOption = {
      trigger: 'item'
    };
    const title: TitleComponentOption = {
      text: 'Total Deployments By Project This Year',
      subtext: 'Not including top 3'
    };

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

    const series: PieSeriesOption[] = [
      {
        type: 'pie',
        data: sortedNSummedNoTop3.map(value => ({
          name: value.ProjectName ?? '',
          value: value.CountOfDeployments
        }))
      }
    ];

    this.pieChartOptions = {
      tooltip: tt,
      title,
      series
    };
  }

  private constructEnvironmentUsageChart(
    data: AnalyticsEnvironmentUsageApiModel[]
  ) {
    const environments = data.slice(0, 10).map(d => d.EnvironmentName ?? '');
    const counts = data.slice(0, 10).map(d => d.CountOfDeployments ?? 0);

    const title: TitleComponentOption = {
      text: 'Top 10 Environments by Deployment Count',
      left: 'center'
    };

    const tooltip: TooltipComponentOption = {
      trigger: 'axis',
      axisPointer: { type: 'shadow' }
    };

    const grid: GridComponentOption = {
      left: '3%',
      right: '4%',
      bottom: '3%',
      containLabel: true
    };

    const xAxis: XAXisComponentOption = {
      type: 'category',
      data: environments,
      axisLabel: {
        interval: 0,
        rotate: 45
      }
    };

    const yAxis: YAXisComponentOption = {
      type: 'value'
    };

    const series: BarSeriesOption[] = [
      {
        type: 'bar',
        data: counts,
        itemStyle: {
          color: '#1890ff'
        }
      }
    ];

    this.environmentUsageChartOptions = {
      title,
      tooltip,
      grid,
      xAxis,
      yAxis,
      series
    };
  }

  private constructUserActivityChart(data: AnalyticsUserActivityApiModel[]) {
    const users = data.slice(0, 10).map(d => d.UserName ?? '');
    const counts = data.slice(0, 10).map(d => d.CountOfDeployments ?? 0);

    const title: TitleComponentOption = {
      text: 'Top 10 Users by Deployment Count',
      left: 'center'
    };

    const tooltip: TooltipComponentOption = {
      trigger: 'axis',
      axisPointer: { type: 'shadow' }
    };

    const grid: GridComponentOption = {
      left: '3%',
      right: '4%',
      bottom: '3%',
      containLabel: true
    };

    const yAxis: YAXisComponentOption = {
      type: 'category',
      data: users
    };

    const xAxis: XAXisComponentOption = {
      type: 'value'
    };

    const series: BarSeriesOption[] = [
      {
        type: 'bar',
        data: counts,
        itemStyle: {
          color: '#52c41a'
        }
      }
    ];

    this.userActivityChartOptions = {
      title,
      tooltip,
      grid,
      xAxis,
      yAxis,
      series
    };
  }

  private constructTimePatternChart(data: AnalyticsTimePatternApiModel[]) {
    console.log('constructTimePatternChart called with data:', data);

    // Group by day of week and hour for heatmap
    const dayNames = [
      'Sunday',
      'Monday',
      'Tuesday',
      'Wednesday',
      'Thursday',
      'Friday',
      'Saturday'
    ];
    const hours = Array.from({ length: 24 }, (_, i) => `${i}:00`);

    const heatmapData: [number, number, number][] = [];

    if (data && data.length > 0) {
      data.forEach(d => {
        if (
          d.HourOfDay !== undefined &&
          d.DayOfWeek !== undefined &&
          d.HourOfDay !== null &&
          d.DayOfWeek !== null
        ) {
          heatmapData.push([
            d.HourOfDay,
            d.DayOfWeek,
            d.CountOfDeployments ?? 0
          ]);
        }
      });
    }

    // If no data, create at least one data point so chart renders
    if (heatmapData.length === 0) {
      console.warn('No valid heatmap data, creating empty chart');
      heatmapData.push([0, 0, 0]);
    }

    const title: TitleComponentOption = {
      text: 'Deployment Time Patterns (Hour vs Day of Week)',
      left: 'center'
    };

    const tooltip: TooltipComponentOption = {
      position: 'top',
      formatter: (params: any) => {
        const hour = params.data[0];
        const day = params.data[1];
        const count = params.data[2];
        return `${dayNames[day]}, ${hour}:00<br/>Deployments: ${count}`;
      }
    };

    const grid: GridComponentOption = {
      left: '10%',
      right: '10%',
      bottom: '10%',
      containLabel: true
    };

    const xAxis: XAXisComponentOption = {
      type: 'category',
      data: hours,
      name: 'Hour of Day',
      nameLocation: 'middle',
      nameGap: 30
    };

    const yAxis: YAXisComponentOption = {
      type: 'category',
      data: dayNames,
      name: 'Day of Week'
    };

    const maxValue = Math.max(...heatmapData.map(d => d[2]), 1);

    this.timePatternChartOptions = {
      title,
      tooltip,
      grid,
      xAxis,
      yAxis,
      visualMap: {
        min: 0,
        max: maxValue,
        calculable: true,
        orient: 'horizontal',
        left: 'center',
        bottom: '0%'
      },
      series: [
        {
          type: 'heatmap',
          data: heatmapData,
          label: {
            show: false
          },
          emphasis: {
            itemStyle: {
              shadowBlur: 10,
              shadowColor: 'rgba(0, 0, 0, 0.5)'
            }
          }
        }
      ]
    };
  }

  private constructComponentUsageChart(
    data: AnalyticsComponentUsageApiModel[]
  ) {
    console.log('constructComponentUsageChart called with data:', data);

    let components: string[] = [];
    let counts: number[] = [];

    if (data && data.length > 0) {
      components = data
        .slice(0, 15)
        .map(d => d.ComponentName ?? '')
        .filter(c => c !== '');
      counts = data.slice(0, 15).map(d => d.CountOfDeployments ?? 0);
    }

    // If no data, create placeholder
    if (components.length === 0) {
      console.warn('No valid component data, creating empty chart');
      components = ['No Data'];
      counts = [0];
    }

    const title: TitleComponentOption = {
      text: 'Top 15 Components by Deployment Count',
      left: 'center'
    };

    const tooltip: TooltipComponentOption = {
      trigger: 'axis',
      axisPointer: { type: 'shadow' }
    };

    const grid: GridComponentOption = {
      left: '3%',
      right: '4%',
      bottom: '3%',
      containLabel: true
    };

    const yAxis: YAXisComponentOption = {
      type: 'category',
      data: components,
      axisLabel: {
        interval: 0
      }
    };

    const xAxis: XAXisComponentOption = {
      type: 'value'
    };

    const series: BarSeriesOption[] = [
      {
        type: 'bar',
        data: counts,
        itemStyle: {
          color: '#faad14'
        }
      }
    ];

    this.componentUsageChartOptions = {
      title,
      tooltip,
      grid,
      xAxis,
      yAxis,
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
