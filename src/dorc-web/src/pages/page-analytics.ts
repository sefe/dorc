import { css } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { Subscription } from 'rxjs';
import '../components/chart/dorc-chart';
import '../components/dorc-spinner';
import '@vaadin/checkbox';
import '@vaadin/combo-box';
import type { Checkbox } from '@vaadin/checkbox';
import type { ComboBox } from '@vaadin/combo-box';
import { PageElement } from '../helpers/page-element';
import {
  AnalyticsComponentReliabilityApi,
  AnalyticsDeploymentsMonthApi,
  AnalyticsDeploymentSummaryApi,
  AnalyticsEnvironmentUsageApi,
  AnalyticsEnvironmentWaitApi,
  AnalyticsMonthlyOutcomeApi,
  AnalyticsProjectDurationApi,
  AnalyticsRecoveryTimeApi,
  AnalyticsUserActivityApi,
  AnalyticsTimePatternApi,
  AnalyticsComponentUsageApi,
  AnalyticsDurationApi,
  AnalyticsComponentReliabilityApiModel,
  AnalyticsDeploymentsPerProjectApiModel,
  AnalyticsDeploymentSummaryApiModel,
  AnalyticsEnvironmentUsageApiModel,
  AnalyticsEnvironmentWaitApiModel,
  AnalyticsMonthlyOutcomeApiModel,
  AnalyticsProjectDurationApiModel,
  AnalyticsRecoveryTimeApiModel,
  AnalyticsUserActivityApiModel,
  AnalyticsTimePatternApiModel,
  AnalyticsComponentUsageApiModel,
  AnalyticsDurationApiModel
} from '../apis/dorc-api';
import {
  buildComponentFailureRates,
  buildEnvironmentStaleness,
  distinctMonthOptions,
  distinctProjects,
  filterByMonthRange,
  filterByProject
} from './page-analytics-data';
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
import type {
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

  @state() private monthlyOutcomeChartOptions: EChartsOption | undefined;

  @state() private environmentWaitChartOptions: EChartsOption | undefined;

  @state() private projectDurationChartOptions: EChartsOption | undefined;

  @state()
  private componentReliabilityChartOptions: EChartsOption | undefined;

  @state() private recoveryTimeChartOptions: EChartsOption | undefined;

  @state() private stalenessChartOptions: EChartsOption | undefined;

  @state() private monthFilterOptions: string[] = [];

  @state() private projectFilterOptions: string[] = [];

  @state() private filterFromMonth = '';

  @state() private filterToMonth = '';

  @state() private filterProject = '';

  private monthlyOutcomeResponse: AnalyticsMonthlyOutcomeApiModel[] = [];

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
      .filter-bar {
        display: flex;
        flex-wrap: wrap;
        align-items: baseline;
        gap: var(--lumo-space-m);
        padding: 0 var(--lumo-space-s);
      }

      div#page_div {
        overflow: auto;
        width: 100%;
        flex: 1;
        min-height: 0;
      }

      /* Chart sizing: dorc-chart re-renders ECharts through a ResizeObserver,
         so responsive height/width here is all that's needed per viewport. */
      .chart {
        display: block;
        width: 100%;
      }
      .chart--sm {
        height: 400px;
      }
      .chart--md {
        height: 600px;
      }
      .chart--lg {
        height: 700px;
      }
      .chart--xl {
        height: 1200px;
      }

      @media (max-width: 1024px) {
        .chart--lg {
          height: 560px;
        }
        .chart--xl {
          height: 800px;
        }
      }

      @media (max-width: 768px) {
        .chart--sm {
          height: 300px;
        }
        .chart--md {
          height: 400px;
        }
        .chart--lg {
          height: 460px;
        }
        .chart--xl {
          height: 560px;
        }

        .statistics-cards {
          max-width: 100%;
        }
        .statistics-cards__item {
          flex: 1 1 40%;
          min-width: 0;
        }
        .top3-chart-block {
          padding: var(--lumo-space-s);
          min-width: 0;
          flex: 1 1 100%;
        }
        .filter-bar vaadin-combo-box {
          width: 100%;
        }
      }

      @media (max-width: 480px) {
        .chart--sm {
          height: 260px;
        }
        .chart--md {
          height: 340px;
        }
        .chart--lg {
          height: 400px;
        }
        .chart--xl {
          height: 480px;
        }
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
                  <div class="statistics-cards__item card-element"
                    title="P50 (50th percentile): half of all deployments complete within this time">
                    <h3>
                      ${this.durationStats?.P50DurationMinutes?.toFixed(1) ??
                      '—'}
                      min
                    </h3>
                    <span class="card-element__text"
                      >Median Duration (P50)</span
                    >
                  </div>
                  <div class="statistics-cards__item card-element"
                    title="P90 (90th percentile): 90% of deployments complete within this time — only 10% take longer">
                    <h3>
                      ${this.durationStats?.P90DurationMinutes?.toFixed(1) ??
                      '—'}
                      min
                    </h3>
                    <span class="card-element__text">P90 Duration</span>
                  </div>
                  <div class="statistics-cards__item card-element"
                    title="P95 (95th percentile): 95% of deployments complete within this time — only the slowest 5% take longer">
                    <h3>
                      ${this.durationStats?.P95DurationMinutes?.toFixed(1) ??
                      '—'}
                      min
                    </h3>
                    <span class="card-element__text">P95 Duration</span>
                  </div>
                </div>
                <div class="top3-chart-block">
                  <dorc-chart
                    class="chart chart--sm"
                    .option="${this.top3PieChartOptions}"
                  ></dorc-chart>
                </div>
              </div>
              <div class="statistics-cards__item card-element">
                <dorc-chart
                  class="chart chart--md"
                  .option="${this.environmentUsageChartOptions}"
                ></dorc-chart>
              </div>
              <div class="statistics-cards__item card-element">
                <dorc-chart
                  class="chart chart--md"
                  .option="${this.userActivityChartOptions}"
                ></dorc-chart>
              </div>
              <div class="statistics-cards__item card-element">
                <dorc-chart
                  class="chart chart--md"
                  .option="${this.timePatternChartOptions}"
                ></dorc-chart>
              </div>
              <div class="statistics-cards__item card-element">
                <dorc-chart
                  class="chart chart--lg"
                  .option="${this.componentUsageChartOptions}"
                ></dorc-chart>
              </div>
              <div class="statistics-cards__item card-element">
                <dorc-chart
                  class="chart chart--md"
                  .option="${this.environmentWaitChartOptions}"
                ></dorc-chart>
              </div>
              <div class="statistics-cards__item card-element">
                <dorc-chart
                  class="chart chart--lg"
                  .option="${this.projectDurationChartOptions}"
                ></dorc-chart>
              </div>
              <div class="statistics-cards__item card-element">
                <dorc-chart
                  class="chart chart--lg"
                  .option="${this.componentReliabilityChartOptions}"
                ></dorc-chart>
              </div>
              <div class="statistics-cards__item card-element">
                <dorc-chart
                  class="chart chart--md"
                  .option="${this.recoveryTimeChartOptions}"
                ></dorc-chart>
              </div>
              <div class="statistics-cards__item card-element">
                <dorc-chart
                  class="chart chart--lg"
                  .option="${this.stalenessChartOptions}"
                ></dorc-chart>
              </div>
              <div class="statistics-cards__item card-element">
                <div class="filter-bar">
                  <vaadin-combo-box
                    label="From month"
                    clear-button-visible
                    .items="${this.monthFilterOptions}"
                    .value="${this.filterFromMonth}"
                    @value-changed="${this.updateFromMonthFilter}"
                  ></vaadin-combo-box>
                  <vaadin-combo-box
                    label="To month"
                    clear-button-visible
                    .items="${this.monthFilterOptions}"
                    .value="${this.filterToMonth}"
                    @value-changed="${this.updateToMonthFilter}"
                  ></vaadin-combo-box>
                  <vaadin-combo-box
                    label="Project"
                    clear-button-visible
                    .items="${this.projectFilterOptions}"
                    .value="${this.filterProject}"
                    @value-changed="${this.updateProjectFilter}"
                  ></vaadin-combo-box>
                  <span class="card-element__text"
                    >Filters apply to the deployment river and monthly outcome
                    charts</span
                  >
                </div>
                <dorc-chart
                  class="chart chart--md"
                  .option="${this.monthlyOutcomeChartOptions}"
                ></dorc-chart>
              </div>
              <div class="statistics-cards__item card-element">
                <dorc-chart
                  class="chart chart--xl"
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
                  class="chart chart--xl"
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

  private updateFromMonthFilter(e: CustomEvent) {
    this.filterFromMonth = (e.target as ComboBox).value ?? '';
    this.applyTimeSeriesFilters();
  }

  private updateToMonthFilter(e: CustomEvent) {
    this.filterToMonth = (e.target as ComboBox).value ?? '';
    this.applyTimeSeriesFilters();
  }

  private updateProjectFilter(e: CustomEvent) {
    this.filterProject = (e.target as ComboBox).value ?? '';
    this.applyTimeSeriesFilters();
  }

  /** Re-derives the time-series charts from the active filter selection. */
  private applyTimeSeriesFilters() {
    this.constructRiverChart(!this.includeDeprecated);
    this.constructMonthlyOutcomeChart();
  }

  private loadMonthData() {
    const api = new AnalyticsDeploymentsMonthApi();
    this.subscriptions.push(
      api.analyticsDeploymentsMonthGet().subscribe({
        next: (res: AnalyticsDeploymentsPerProjectApiModel[]) => {
          this.analyticsDeploymentsMonthResponse = res;
          this.monthFilterOptions = distinctMonthOptions(res);
          this.projectFilterOptions = distinctProjects(res);
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
          this.constructStalenessChart(res);
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

    // Monthly Outcomes (volume / failures / cancellations, prod vs non-prod)
    const outcomeApi = new AnalyticsMonthlyOutcomeApi();
    this.subscriptions.push(
      outcomeApi.analyticsMonthlyOutcomeGet().subscribe({
        next: (res: AnalyticsMonthlyOutcomeApiModel[]) => {
          this.monthlyOutcomeResponse = res;
          this.constructMonthlyOutcomeChart();
        },
        error: err => {
          console.error('Failed to load monthly outcome data:', err);
        }
      })
    );

    // Environment Wait Times
    const waitApi = new AnalyticsEnvironmentWaitApi();
    this.subscriptions.push(
      waitApi.analyticsEnvironmentWaitGet().subscribe({
        next: (res: AnalyticsEnvironmentWaitApiModel[]) => {
          this.constructEnvironmentWaitChart(res);
        },
        error: err => {
          console.error('Failed to load environment wait data:', err);
        }
      })
    );

    // Per-project Durations
    const projDurApi = new AnalyticsProjectDurationApi();
    this.subscriptions.push(
      projDurApi.analyticsProjectDurationGet().subscribe({
        next: (res: AnalyticsProjectDurationApiModel[]) => {
          this.constructProjectDurationChart(res);
        },
        error: err => {
          console.error('Failed to load project duration data:', err);
        }
      })
    );

    // Component Reliability
    const reliabilityApi = new AnalyticsComponentReliabilityApi();
    this.subscriptions.push(
      reliabilityApi.analyticsComponentReliabilityGet().subscribe({
        next: (res: AnalyticsComponentReliabilityApiModel[]) => {
          this.constructComponentReliabilityChart(res);
        },
        error: err => {
          console.error('Failed to load component reliability data:', err);
        }
      })
    );

    // Recovery Times
    const recoveryApi = new AnalyticsRecoveryTimeApi();
    this.subscriptions.push(
      recoveryApi.analyticsRecoveryTimeGet().subscribe({
        next: (res: AnalyticsRecoveryTimeApiModel[]) => {
          this.constructRecoveryTimeChart(res);
        },
        error: err => {
          console.error('Failed to load recovery time data:', err);
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

    const filteredRows = filterByProject(
      filterByMonthRange(
        this.analyticsDeploymentsMonthResponse,
        this.filterFromMonth,
        this.filterToMonth
      ),
      this.filterProject
    );

    filteredRows.forEach(m => {
      if (!m.Year || !m.Month) return;
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
      data: users,
      axisLabel: { width: 140, overflow: 'truncate' }
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
        if (point.length < 3) return '';
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
      axisLabel: { interval: 0, width: 140, overflow: 'truncate' }
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

  private constructMonthlyOutcomeChart() {
    const rows = filterByMonthRange(
      this.monthlyOutcomeResponse,
      this.filterFromMonth,
      this.filterToMonth
    );

    // One bucket per calendar month, prod and non-prod kept separate for the
    // stacked volume bars; failure/cancellation rates computed on the total.
    const byMonth = new Map<
      string,
      { prod: number; nonProd: number; failed: number; cancelled: number }
    >();
    rows.forEach(row => {
      if (!row.Year || !row.Month) return;
      const key = `${row.Year}-${String(row.Month).padStart(2, '0')}`;
      const bucket = byMonth.get(key) ?? {
        prod: 0,
        nonProd: 0,
        failed: 0,
        cancelled: 0
      };
      if (row.IsProd === true) {
        bucket.prod += row.CountOfDeployments ?? 0;
      } else {
        bucket.nonProd += row.CountOfDeployments ?? 0;
      }
      bucket.failed += row.Failed ?? 0;
      bucket.cancelled += row.Cancelled ?? 0;
      byMonth.set(key, bucket);
    });

    const months = [...byMonth.keys()].sort();
    const hasData = months.length > 0;
    const prodSeries = months.map(m => byMonth.get(m)?.prod ?? 0);
    const nonProdSeries = months.map(m => byMonth.get(m)?.nonProd ?? 0);
    const failureRate = months.map(m => {
      const bucket = byMonth.get(m);
      const total = (bucket?.prod ?? 0) + (bucket?.nonProd ?? 0);
      return total > 0
        ? Math.round(((bucket?.failed ?? 0) / total) * 1000) / 10
        : 0;
    });
    const cancelledSeries = months.map(m => byMonth.get(m)?.cancelled ?? 0);

    this.monthlyOutcomeChartOptions = {
      title: {
        text: hasData
          ? 'Monthly Deployments: Volume, Failure Rate and Cancellations'
          : 'Monthly Deployments (no data available)',
        left: 'center'
      },
      tooltip: { trigger: 'axis' },
      legend: {
        data: ['Non-Prod', 'Prod', 'Cancelled', 'Failure Rate %'],
        top: 40      },
      grid: { left: '3%', right: '4%', bottom: '3%', containLabel: true },
      xAxis: { type: 'category', data: months },
      yAxis: [
        { type: 'value', name: 'Deployments' },
        {
          type: 'value',
          name: 'Failure Rate %',
          min: 0,
          axisLabel: { formatter: '{value}%' }
        }
      ],
      series: [
        {
          name: 'Non-Prod',
          type: 'bar',
          stack: 'volume',
          data: nonProdSeries,
          itemStyle: { color: '#91caff' }
        },
        {
          name: 'Prod',
          type: 'bar',
          stack: 'volume',
          data: prodSeries,
          itemStyle: { color: '#0958d9' }
        },
        {
          name: 'Cancelled',
          type: 'bar',
          data: cancelledSeries,
          itemStyle: { color: '#bfbfbf' }
        },
        {
          name: 'Failure Rate %',
          type: 'line',
          yAxisIndex: 1,
          data: failureRate,
          itemStyle: { color: '#cf1322' }
        }
      ]
    };
  }

  private constructEnvironmentWaitChart(
    data: AnalyticsEnvironmentWaitApiModel[]
  ) {
    // The API pre-sorts by median wait, but sort defensively so the "top 10"
    // claim holds regardless of server ordering.
    const top = [...(data ?? [])]
      .sort((a, b) => (b.MedianWaitMinutes ?? 0) - (a.MedianWaitMinutes ?? 0))
      .slice(0, 10);
    const hasData = top.length > 0;
    const environments = top.map(d => d.EnvironmentName ?? '');

    this.environmentWaitChartOptions = {
      title: {
        text: hasData
          ? 'Top 10 Environments by Queue Wait Time'
          : 'Environment Queue Wait Times (no data available)',
        left: 'center'
      },
      tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
      legend: { data: ['Median Wait (min)', 'P90 Wait (min)'], top: 40 },
      grid: { left: '3%', right: '4%', bottom: '3%', containLabel: true },
      yAxis: {
        type: 'category',
        data: environments,
        axisLabel: { width: 140, overflow: 'truncate' }
      },
      xAxis: { type: 'value', name: 'Minutes' },
      series: [
        {
          name: 'Median Wait (min)',
          type: 'bar',
          data: top.map(d => d.MedianWaitMinutes ?? 0),
          itemStyle: { color: '#fa8c16' }
        },
        {
          name: 'P90 Wait (min)',
          type: 'bar',
          data: top.map(d => d.P90WaitMinutes ?? 0),
          itemStyle: { color: '#ffd591' }
        }
      ]
    };
  }

  private constructProjectDurationChart(
    data: AnalyticsProjectDurationApiModel[]
  ) {
    // The API pre-sorts by sample count, but sort defensively so the "top 15
    // by volume" claim holds regardless of server ordering.
    const top = [...(data ?? [])]
      .sort((a, b) => (b.SampleCount ?? 0) - (a.SampleCount ?? 0))
      .slice(0, 15);
    const hasData = top.length > 0;

    this.projectDurationChartOptions = {
      title: {
        text: hasData
          ? 'Deployment Duration by Project (top 15 by volume)'
          : 'Deployment Duration by Project (no data available)',
        left: 'center'
      },
      tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
      legend: { data: ['Median (min)', 'P90 (min)'], top: 40 },
      grid: { left: '3%', right: '4%', bottom: '3%', containLabel: true },
      yAxis: {
        type: 'category',
        data: top.map(d => d.ProjectName ?? ''),
        axisLabel: { width: 140, overflow: 'truncate' }
      },
      xAxis: { type: 'value', name: 'Minutes' },
      series: [
        {
          name: 'Median (min)',
          type: 'bar',
          data: top.map(d => d.MedianDurationMinutes ?? 0),
          itemStyle: { color: '#13c2c2' }
        },
        {
          name: 'P90 (min)',
          type: 'bar',
          data: top.map(d => d.P90DurationMinutes ?? 0),
          itemStyle: { color: '#87e8de' }
        }
      ]
    };
  }

  private constructComponentReliabilityChart(
    data: AnalyticsComponentReliabilityApiModel[]
  ) {
    // Minimum volume keeps one-off failures from dominating the ranking.
    const rates = buildComponentFailureRates(data ?? [], 20, 15);
    const hasData = rates.length > 0;

    this.componentReliabilityChartOptions = {
      title: {
        text: hasData
          ? 'Least Reliable Components (failure %, min 20 executed attempts, recent history)'
          : 'Component Reliability (no data available)',
        left: 'center'
      },
      tooltip: {
        trigger: 'axis',
        axisPointer: { type: 'shadow' },
        formatter: (params: TopLevelFormatterParams) => {
          const item = (Array.isArray(params) ? params[0] : params) as
            | CallbackDataParams
            | undefined;
          if (!item) return '';
          const rate = rates[item.dataIndex ?? 0];
          if (!rate) return '';
          return (
            `${rate.componentName}<br/>` +
            `Failure rate: ${rate.failureRatePercent}%<br/>` +
            `Attempts: ${rate.attemptCount}<br/>` +
            `Retry attempts: ${rate.retryAttemptCount}`
          );
        }
      },
      grid: { left: '3%', right: '4%', bottom: '3%', containLabel: true },
      yAxis: {
        type: 'category',
        data: rates.map(r => r.componentName),
        axisLabel: { interval: 0, width: 140, overflow: 'truncate' }
      },
      xAxis: { type: 'value', name: 'Failure %' },
      series: [
        {
          type: 'bar',
          data: rates.map(r => r.failureRatePercent),
          itemStyle: { color: '#cf1322' }
        }
      ]
    };
  }

  private constructRecoveryTimeChart(data: AnalyticsRecoveryTimeApiModel[]) {
    // The API pre-sorts by median recovery, but sort defensively so the
    // "slowest" claim holds regardless of server ordering.
    const top = [...(data ?? [])]
      .sort(
        (a, b) => (b.MedianRecoveryHours ?? 0) - (a.MedianRecoveryHours ?? 0)
      )
      .slice(0, 10);
    const hasData = top.length > 0;

    this.recoveryTimeChartOptions = {
      title: {
        text: hasData
          ? 'Slowest Projects to Recover After a Failure (median hours)'
          : 'Recovery Times (no data available)',
        left: 'center'
      },
      tooltip: {
        trigger: 'axis',
        axisPointer: { type: 'shadow' },
        formatter: (params: TopLevelFormatterParams) => {
          const item = (Array.isArray(params) ? params[0] : params) as
            | CallbackDataParams
            | undefined;
          if (!item) return '';
          const row = top[item.dataIndex ?? 0];
          if (!row) return '';
          return (
            `${row.ProjectName ?? ''}<br/>` +
            `Median recovery: ${row.MedianRecoveryHours ?? 0} h<br/>` +
            `Average recovery: ${row.AvgRecoveryHours ?? 0} h<br/>` +
            `Failures measured: ${row.SampleCount ?? 0}`
          );
        }
      },
      grid: { left: '3%', right: '4%', bottom: '3%', containLabel: true },
      yAxis: {
        type: 'category',
        data: top.map(d => d.ProjectName ?? ''),
        axisLabel: { width: 140, overflow: 'truncate' }
      },
      xAxis: { type: 'value', name: 'Hours' },
      series: [
        {
          type: 'bar',
          data: top.map(d => d.MedianRecoveryHours ?? 0),
          itemStyle: { color: '#722ed1' }
        }
      ]
    };
  }

  private constructStalenessChart(data: AnalyticsEnvironmentUsageApiModel[]) {
    const stale = buildEnvironmentStaleness(data ?? [], new Date(), 15);
    const hasData = stale.length > 0;

    this.stalenessChartOptions = {
      title: {
        text: hasData
          ? 'Stalest Environments (days since last successful deployment)'
          : 'Environment Staleness (no data available)',
        left: 'center'
      },
      tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
      grid: { left: '3%', right: '4%', bottom: '3%', containLabel: true },
      yAxis: {
        type: 'category',
        data: stale.map(s => s.environmentName),
        axisLabel: { width: 140, overflow: 'truncate' }
      },
      xAxis: { type: 'value', name: 'Days' },
      series: [
        {
          type: 'bar',
          data: stale.map(s => s.daysSinceLastSuccess),
          itemStyle: { color: '#8c8c8c' }
        }
      ]
    };
  }
}

interface AnalyticsProjectDeployment {
  project: string;
  numDeployments: number;
}
