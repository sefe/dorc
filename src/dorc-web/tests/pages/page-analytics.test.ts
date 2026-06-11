import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

// --- Hoisted, mutable holders so each test can steer the mocked API ---
const { summaryHolder, monthHolder, unsubscribeSpy } = vi.hoisted(() => ({
  summaryHolder: { value: {} as any, mode: 'next' as 'next' | 'error' },
  monthHolder: { value: [] as any[] },
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
vi.mock('@vaadin/checkbox/src/vaadin-checkbox', () => ({
  Checkbox: class {}
}));

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
    // month + summary + 5 chart streams = 7.
    expect(unsubscribeSpy).toHaveBeenCalledTimes(7);
    expect((el as any).subscriptions).toHaveLength(0);
  });
});
