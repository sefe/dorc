import { css } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { Subscription } from 'rxjs';
import '../components/chart/dorc-chart';
import '../components/dorc-spinner';
import '@vaadin/checkbox';
import { Checkbox } from '@vaadin/checkbox/src/vaadin-checkbox';
import { PageElement } from '../helpers/page-element';
import {
  AnalyticsDeploymentsMonthApi,
  AnalyticsDeploymentSummaryApi,
  AnalyticsEnvironmentUsageApi,
  AnalyticsUserActivityApi,
  AnalyticsTimePatternApi,
  AnalyticsComponentUsageApi,
  AnalyticsDurationApi
} from '../apis/dorc-api';
import {
  AnalyticsDeploymentsPerProjectApiModel,
  AnalyticsDeploymentSummaryApiModel,
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
type OptionDataValueDate = Date | string | number;
type OptionDataValueNumeric = number | '-';

declare type ThemerRiverDataItem = [
  OptionDataValueDate,
  OptionDataValueNumeric,
  string
];

const DAY_NAMES = [
  'Sunday',
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday'
];

@customElement('page-analytics')
export class PageAnalytics extends PageElement {
  @state()
  private analyticsDeploymentsMonthResponse: AnalyticsDeploymentsPerProjectApiModel[] =
    [];

  @state() private top3PieChartOptions: EChartsOption | undefined;

  @state() private pieChartOptions: EChartsOption | undefined;

  @state() private riverChartOptions: EChartsOption | undefined;

  @state() private environmentUsageChartOptions: EChartsOption | undefined;

  @state() private userActivityChartOptions: EChartsOption | undefined;

  @state() private timePatternChartOptions: EChartsOption | undefined;

  @state() private componentUsageChartOptions: EChartsOption | undefined;

  @state() private totalDeployments = 0;

  @state() private totalDeploymentsThisYear = 0;

  @state() private averageDeploymentsPerDay = 0;

  @state() private busiestDeploymentCount = 0;

  @state() private totalFailedDeploymentsThisYear = 0;

  @state() private percentFailedThisYear = 0;

  @state() private percentTop3ProjectsThisYear = 0;

  @state()
  private topProjectsThisYear: AnalyticsProjectDeployment[] = [];

  @state() private durationStats: AnalyticsDurationApiModel | undefined;

  @state() private loading = true;

  @state() private includeDeprecated = true;

  private subscriptions: Subscription[] = [];

  connectedCallback() {
    super.connectedCallback();
    // Reset to the loading state so a re-attach (router navigating back to
    // /analytics) shows the spinner again rather than stale data.
    this.loading = true;
    this.loadMonthData();
    this.loadSummary();
    this.loadCharts();
  }

  disconnectedCallback() {
    this.subscriptions.forEach(sub => sub.unsubscribe());
    this.subscriptions = [];
    super.disconnectedCallback();
  }

  static get styles() {
    return css`
      :host {
        display: flex;
        flex-direction: column;
        height: 100%;
      }

      .page-analytics {
        padding: 1rem;
      }

      .page-analytics__main-info {
        margin-bottom: 18px;
      }

      .card-element {
        padding: 10px;
        box-shadow: 1px 2px 3px rgba(0, 0, 0, 0.2);
      }
      .card-element__heading {
        color: var(--dorc-error-color);
      }
      .card-element__text {
        color: var(--dorc-text-secondary);
      }

      .statistics-cards {
        max-width: 500px;
        display: flex;
        flex-wrap: wrap;
      }
      .statistics-cards__item {
        margin: var(--lumo-space-xs);
        flex-shrink: 0;
        min-width: 120px;
      }

      .main-info {
        display: flex;
        flex-wrap: wrap;
        gap: var(--lumo-space-s);
      }

      .top3-chart-block {
        padding: 26px;
        box-shadow: 1px 2px 3px rgba(0, 0, 0, 0.2);
        align-self: flex-start;
        flex: 1 1 400px;
        min-width: 300px;
        max-width: 652px;
      }

      .top3-chart-block__percent {
        float: right;
        vertical-align: middle;
      }
      div#page_div {
        overflow: auto;
        width: 100%;
        flex: 1;
        min-height: 0;
      }
    `;
  }

  render() {
    return html`
      ${this.loading
        ? html` <dorc-spinner></dorc-spinner> `
        : html`
            <div id="page_div">
              <div class="page-analytics__main-info main-info">
                <div class="statistics-cards">
                  <div class="statistics-cards__item card-element">
                    <h3>${this.totalDeployments}</h3>
                    <span class="card-element__text">Total # deployments</span>
                  </div>
                  <div class="statistics-cards__item card-element">
                    <h3>${this.totalDeploymentsThisYear}</h3>
                    <span class="card-element__text"
                      >Total # deployments this year</span
                    >
                  </div>
                  <div class="statistics-cards__item card-element">
                    <h3>${this.averageDeploymentsPerDay}</h3>
                    <span class="card-element__text"
                      >Average Deployments Per Day</span
                    >
                  </div>
                  <div class="statistics-cards__item card-element">
                    <h3>${this.busiestDeploymentCount}</h3>
                    <span class="card-element__text"
                      >Most Deployments In A Day</span
                    >
                  </div>
                  <div class="statistics-cards__item card-element">
                    <h3>${this.totalFailedDeploymentsThisYear}</h3>
                    <span class="card-element__text"
                      >Total Failures This Year</span
                    >
                  </div>
                  <div class="statistics-cards__item card-element">
                    <h3>${this.percentFailedThisYear}%</h3>
                    <span class="card-element__text"
                      >Failure Rate This Year</span
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
                  <dorc-chart
                    style="display: block; width: 100%; height: 400px;"
                    .option="${this.top3PieChartOptions}"
                  ></dorc-chart>
                </div>
              </div>
              <div class="statistics-cards__item card-element">
                <dorc-chart
                  style="display: block; width: 100%; height: 600px;"
                  .option="${this.environmentUsageChartOptions}"
                ></dorc-chart>
              </div>
              <div class="statistics-cards__item card-element">
                <dorc-chart
                  style="display: block; width: 100%; height: 600px;"
                  .option="${this.userActivityChartOptions}"
                ></dorc-chart>
              </div>
              <div class="statistics-cards__item card-element">
                <dorc-chart
                  style="display: block; width: 100%; height: 600px;"
                  .option="${this.timePatternChartOptions}"
                ></dorc-chart>
              </div>
              <div class="statistics-cards__item card-element">
                <dorc-chart
                  style="display: block; width: 100%; height: 800px;"
                  .option="${this.componentUsageChartOptions}"
                ></dorc-chart>
              </div>
              <div class="statistics-cards__item card-element">
                <dorc-chart
                  style="display: block; width: 100%; height: 1200px;"
                  .option="${this.riverChartOptions}"
                ></dorc-chart>
                <vaadin-checkbox
                  label="Include Deprecated"
                  ?checked="${this.includeDeprecated}"
                  @change="${this.updateDeprecated}"
                ></vaadin-checkbox>
              </div>
              <div class="statistics-cards__item card-element">
                <dorc-chart
                  style="display: block; width: 100%; height: 1200px;"
                  .option="${this.pieChartOptions}"
                ></dorc-chart>
              </div>
            </div>
          `}
    `;
  }

  updateDeprecated(e: CustomEvent) {
    const cbx = e.target as Checkbox;
    this.includeDeprecated = cbx.checked;
    this.constructRiverChart(!cbx.checked);
  }

  private loadMonthData() {
    const api = new AnalyticsDeploymentsMonthApi();
    this.subscriptions.push(
      api.analyticsDeploymentsMonthGet().subscribe({
        next: (res: AnalyticsDeploymentsPerProjectApiModel[]) => {
          this.analyticsDeploymentsMonthResponse = res;
          this.constructRiverChart(!this.includeDeprecated);
          this.constructPieChart();
        },
        error: err => {
          console.error('Failed to load monthly deployment data:', err);
        }
      })
    );
  }

  private loadSummary() {
    const api = new AnalyticsDeploymentSummaryApi();
    this.subscriptions.push(
      api.analyticsDeploymentSummaryGet().subscribe({
        next: (res: AnalyticsDeploymentSummaryApiModel) => {
          this.applySummary(res);
          this.loading = false;
        },
        error: err => {
          console.error('Failed to load deployment summary:', err);
          this.loading = false;
        }
      })
    );
  }

  private applySummary(summary: AnalyticsDeploymentSummaryApiModel) {
    this.totalDeployments = summary.TotalDeployments ?? 0;
    this.totalDeploymentsThisYear = summary.TotalDeploymentsThisYear ?? 0;
    this.averageDeploymentsPerDay = summary.AverageDeploymentsPerDay ?? 0;
    this.busiestDeploymentCount = summary.BusiestDeploymentCount ?? 0;
    this.totalFailedDeploymentsThisYear =
      summary.TotalFailedDeploymentsThisYear ?? 0;
    this.percentFailedThisYear = summary.PercentFailedThisYear ?? 0;
    this.percentTop3ProjectsThisYear = summary.PercentTop3Projects ?? 0;
    this.topProjectsThisYear = (summary.TopProjectsThisYear ?? []).map(p => ({
      project: p.ProjectName ?? '',
      numDeployments: p.CountOfDeployments ?? 0
    }));
    this.constructTop3PieChart();
  }

  private loadCharts() {
    // Environment Usage
    const envApi = new AnalyticsEnvironmentUsageApi();
    this.subscriptions.push(
      envApi.analyticsEnvironmentUsageGet().subscribe({
        next: (res: AnalyticsEnvironmentUsageApiModel[]) => {
          this.constructEnvironmentUsageChart(res);
        },
        error: err => {
          console.error('Failed to load environment usage data:', err);
        }
      })
    );

    // User Activity
    const userApi = new AnalyticsUserActivityApi();
    this.subscriptions.push(
      userApi.analyticsUserActivityGet().subscribe({
        next: (res: AnalyticsUserActivityApiModel[]) => {
          this.constructUserActivityChart(res);
        },
        error: err => {
          console.error('Failed to load user activity data:', err);
        }
      })
    );

    // Time Patterns
    const timeApi = new AnalyticsTimePatternApi();
    this.subscriptions.push(
      timeApi.analyticsTimePatternGet().subscribe({
        next: (res: AnalyticsTimePatternApiModel[]) => {
          this.constructTimePatternChart(res);
        },
        error: err => {
          console.error('Failed to load time pattern data:', err);
        }
      })
    );

    // Component Usage
    const compApi = new AnalyticsComponentUsageApi();
    this.subscriptions.push(
      compApi.analyticsComponentUsageGet().subscribe({
        next: (res: AnalyticsComponentUsageApiModel[]) => {
          this.constructComponentUsageChart(res);
        },
        error: err => {
          console.error('Failed to load component usage data:', err);
        }
      })
    );

    // Duration Stats
    const durApi = new AnalyticsDurationApi();
    this.subscriptions.push(
      durApi.analyticsDurationGet().subscribe({
        next: (res: AnalyticsDurationApiModel) => {
          this.durationStats = res;
        },
        error: err => {
          console.error('Failed to load duration stats:', err);
        }
      })
    );
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
            const current = `${tttFormat[2]} : ${tttFormat[1]} <br/>`;
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

    this.analyticsDeploymentsMonthResponse.forEach(m => {
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

  private constructTop3PieChart() {
    const tt: TooltipComponentOption = {
      trigger: 'item'
    };
    const title: TitleComponentOption = {
      text: 'Top 3 Total Deployments By Project This Year',
      subtext: `${String(this.percentTop3ProjectsThisYear)}%`
    };

    const series: PieSeriesOption[] = [
      {
        type: 'pie',
        data: this.topProjectsThisYear.map(value => ({
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

    const justThisYear = this.analyticsDeploymentsMonthResponse.filter(
      d => d.Year === currentYear
    );

    const distinctProjects = [
      ...new Set(justThisYear.map(x => x.ProjectName ?? ''))
    ].sort();

    const summedProjects: AnalyticsDeploymentsPerProjectApiModel[] = [];

    distinctProjects.forEach(x => {
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

    // Sort descending and drop the top 3 (shown in their own chart), without
    // mutating in place. slice() copes safely with fewer than 3 projects.
    const sortedDescending = [...summedProjects].sort((a, b) => {
      const first = a.CountOfDeployments ?? 0;
      const second = b.CountOfDeployments ?? 0;
      return second - first;
    });
    const withoutTop3 = sortedDescending.slice(3);

    const series: PieSeriesOption[] = [
      {
        type: 'pie',
        data: withoutTop3.map(value => ({
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
    // Group by day of week and hour for heatmap
    const hours = Array.from({ length: 24 }, (_, i) => `${i}:00`);

    const heatmapData: [number, number, number][] = [];

    (data ?? []).forEach(d => {
      if (
        d.HourOfDay !== undefined &&
        d.HourOfDay !== null &&
        d.DayOfWeek !== undefined &&
        d.DayOfWeek !== null
      ) {
        heatmapData.push([d.HourOfDay, d.DayOfWeek, d.CountOfDeployments ?? 0]);
      }
    });

    const hasData = heatmapData.length > 0;

    const title: TitleComponentOption = {
      text: hasData
        ? 'Deployment Time Patterns (Hour vs Day of Week)'
        : 'Deployment Time Patterns (no data available)',
      left: 'center'
    };

    const tooltip: TooltipComponentOption = {
      position: 'top',
      formatter: (params: TopLevelFormatterParams) => {
        const item = (Array.isArray(params) ? params[0] : params) as
          | CallbackDataParams
          | undefined;
        const point = (item?.data as number[]) ?? [];
        const hour = point[0];
        const day = point[1];
        const count = point[2];
        const dayName =
          day >= 0 && day < DAY_NAMES.length ? DAY_NAMES[day] : String(day);
        return `${dayName}, ${hour}:00<br/>Deployments: ${count}`;
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
      data: DAY_NAMES,
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
    // The API already returns these ordered by deployment count, but sort
    // defensively so the "top 15" stays correct regardless of server ordering.
    const top = [...(data ?? [])]
      .sort((a, b) => (b.CountOfDeployments ?? 0) - (a.CountOfDeployments ?? 0))
      .filter(d => (d.ComponentName ?? '') !== '')
      .slice(0, 15);

    const components = top.map(d => d.ComponentName ?? '');
    const counts = top.map(d => d.CountOfDeployments ?? 0);

    const hasData = components.length > 0;

    const title: TitleComponentOption = {
      text: hasData
        ? 'Top 15 Components by Deployment Count'
        : 'Top 15 Components by Deployment Count (no data available)',
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
}

interface AnalyticsProjectDeployment {
  project: string;
  numDeployments: number;
}
