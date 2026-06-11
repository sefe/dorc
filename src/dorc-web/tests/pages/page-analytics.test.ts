import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

// --- Hoisted, mutable holders so each test can steer the mocked API ---
const { summaryHolder, monthHolder, outcomeHolder, unsubscribeSpy } =
  vi.hoisted(() => ({
    summaryHolder: { value: {} as any, mode: 'next' as 'next' | 'error' },
    monthHolder: { value: [] as any[] },
    outcomeHolder: { value: [] as any[] },
    unsubscribeSpy: vi.fn()
  }));

function syncObservable(value: unknown, mode: 'next' | 'error' = 'next') {
  return {
    subscribe: (handlers: any) => {
      if (mode === 'error') {
        handlers.error?.(new Error('mock error'));
      } else {
        handlers.next?.(value);
        handlers.complete?.();
      }
      return { unsubscribe: unsubscribeSpy };
    }
  };
}

// --- Mock heavy / side-effect imports ---
vi.mock('../../src/router/routes.ts', () => ({}));
vi.mock('../../src/helpers/html-meta-manager', () => ({
  updateMetadata: vi.fn()
}));
vi.mock('../../src/components/chart/dorc-chart', () => ({}));
vi.mock('@vaadin/checkbox', () => ({}));
vi.mock('@vaadin/combo-box', () => ({}));

// --- Mock the DOrc API surface used by the page ---
vi.mock('../../src/apis/dorc-api', () => ({
  AnalyticsDeploymentsMonthApi: class {
    analyticsDeploymentsMonthGet = () => syncObservable(monthHolder.value);
  },
  AnalyticsDeploymentSummaryApi: class {
    analyticsDeploymentSummaryGet = () =>
      syncObservable(summaryHolder.value, summaryHolder.mode);
  },
  AnalyticsEnvironmentUsageApi: class {
    analyticsEnvironmentUsageGet = () => syncObservable([]);
  },
  AnalyticsUserActivityApi: class {
    analyticsUserActivityGet = () => syncObservable([]);
  },
  AnalyticsTimePatternApi: class {
    analyticsTimePatternGet = () => syncObservable([]);
  },
  AnalyticsComponentUsageApi: class {
    analyticsComponentUsageGet = () => syncObservable([]);
  },
  AnalyticsDurationApi: class {
    analyticsDurationGet = () => syncObservable({});
  },
  AnalyticsMonthlyOutcomeApi: class {
    analyticsMonthlyOutcomeGet = () => syncObservable(outcomeHolder.value);
  },
  AnalyticsEnvironmentWaitApi: class {
    analyticsEnvironmentWaitGet = () => syncObservable([]);
  },
  AnalyticsProjectDurationApi: class {
    analyticsProjectDurationGet = () => syncObservable([]);
  },
  AnalyticsComponentReliabilityApi: class {
    analyticsComponentReliabilityGet = () => syncObservable([]);
  },
  AnalyticsRecoveryTimeApi: class {
    analyticsRecoveryTimeGet = () => syncObservable([]);
  }
}));

// --- Import component after mocks are defined ---
import { PageAnalytics } from '../../src/pages/page-analytics';

async function mount(): Promise<PageAnalytics> {
  const el = new PageAnalytics();
  document.body.appendChild(el);
  await el.updateComplete;
  return el;
}

describe('PageAnalytics', () => {
  beforeEach(() => {
    summaryHolder.value = {};
    summaryHolder.mode = 'next';
    monthHolder.value = [];
    outcomeHolder.value = [];
    unsubscribeSpy.mockClear();
  });

  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('populates headline stats from the summary endpoint and clears loading', async () => {
    summaryHolder.value = {
      TotalDeployments: 126,
      TotalDeploymentsThisYear: 26,
      AverageDeploymentsPerDay: 3,
      BusiestDeploymentCount: 10,
      TotalFailedDeploymentsThisYear: 6,
      PercentTop3Projects: 100,
      TopProjectsThisYear: [
        { ProjectName: 'A', CountOfDeployments: 15 },
        { ProjectName: 'B', CountOfDeployments: 8 },
        { ProjectName: 'C', CountOfDeployments: 3 }
      ]
    };

    const el = await mount();

    expect((el as any).loading).toBe(false);
    expect((el as any).totalDeployments).toBe(126);
    expect((el as any).totalDeploymentsThisYear).toBe(26);
    expect((el as any).busiestDeploymentCount).toBe(10);
    expect((el as any).topProjectsThisYear).toHaveLength(3);
    expect((el as any).topProjectsThisYear[0].project).toBe('A');
  });

  it('clears the loading spinner even when the summary request fails', async () => {
    summaryHolder.mode = 'error';

    const el = await mount();

    expect((el as any).loading).toBe(false);
  });

  it('renders an explicit no-data title for an empty time-pattern heatmap', async () => {
    const el = await mount();

    (el as any).constructTimePatternChart([]);
    const options = (el as any).timePatternChartOptions;

    expect(options.title.text).toContain('no data available');
    expect(options.series[0].data).toHaveLength(0);
  });

  it('excludes the top 3 projects without throwing when fewer than 3 exist', async () => {
    monthHolder.value = [
      { Year: new Date().getFullYear(), ProjectName: 'Solo', CountOfDeployments: 5 }
    ];

    const el = await mount();

    const options = (el as any).pieChartOptions;
    expect(options.series[0].data).toHaveLength(0);
  });

  it('unsubscribes from all streams on disconnect', async () => {
    const el = await mount();
    el.remove();

    // One unsubscribe per subscription opened during load:
    // month + summary + 10 chart streams = 12.
    expect(unsubscribeSpy).toHaveBeenCalledTimes(12);
    expect((el as any).subscriptions).toHaveLength(0);
  });

  it('aggregates monthly outcomes into prod/non-prod volumes and failure rate', async () => {
    outcomeHolder.value = [
      { Year: 2026, Month: 1, IsProd: true, CountOfDeployments: 10, Failed: 1, Cancelled: 0 },
      { Year: 2026, Month: 1, IsProd: false, CountOfDeployments: 30, Failed: 3, Cancelled: 2 },
      { Year: 2026, Month: 2, IsProd: false, CountOfDeployments: 20, Failed: 0, Cancelled: 1 }
    ];

    const el = await mount();
    const options = (el as any).monthlyOutcomeChartOptions;

    expect(options.xAxis.data).toEqual(['2026-01', '2026-02']);
    // series: [non-prod, prod, cancelled, failure rate %]
    expect(options.series[0].data).toEqual([30, 20]);
    expect(options.series[1].data).toEqual([10, 0]);
    expect(options.series[2].data).toEqual([2, 1]);
    expect(options.series[3].data).toEqual([10, 0]); // 4/40 = 10%, 0/20 = 0%
  });

  it('applies the month-range filter to the monthly outcome chart', async () => {
    outcomeHolder.value = [
      { Year: 2025, Month: 12, IsProd: false, CountOfDeployments: 5, Failed: 0, Cancelled: 0 },
      { Year: 2026, Month: 1, IsProd: false, CountOfDeployments: 7, Failed: 0, Cancelled: 0 }
    ];

    const el = await mount();
    (el as any).filterFromMonth = '2026-01';
    (el as any).applyTimeSeriesFilters();

    const options = (el as any).monthlyOutcomeChartOptions;
    expect(options.xAxis.data).toEqual(['2026-01']);
  });

  it('derives filter options from the month response and filters the river chart by project', async () => {
    monthHolder.value = [
      { Year: 2026, Month: 1, ProjectName: 'Alpha', CountOfDeployments: 5 },
      { Year: 2026, Month: 2, ProjectName: 'Beta', CountOfDeployments: 7 }
    ];

    const el = await mount();

    expect((el as any).monthFilterOptions).toEqual(['2026-01', '2026-02']);
    expect((el as any).projectFilterOptions).toEqual(['Alpha', 'Beta']);

    (el as any).filterProject = 'Alpha';
    (el as any).applyTimeSeriesFilters();

    const riverData = (el as any).riverChartOptions.series[0].data as unknown[][];
    expect(riverData).toHaveLength(1);
    expect(riverData[0][2]).toBe('Alpha');
  });

  it('renders no-data titles for the new charts when their tables are empty', async () => {
    const el = await mount();

    expect((el as any).monthlyOutcomeChartOptions.title.text).toContain('no data available');
    expect((el as any).environmentWaitChartOptions.title.text).toContain('no data available');
    expect((el as any).projectDurationChartOptions.title.text).toContain('no data available');
    expect((el as any).componentReliabilityChartOptions.title.text).toContain('no data available');
    expect((el as any).recoveryTimeChartOptions.title.text).toContain('no data available');
    expect((el as any).stalenessChartOptions.title.text).toContain('no data available');
  });
});
