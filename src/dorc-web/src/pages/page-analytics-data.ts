import type {
  AnalyticsComponentReliabilityApiModel,
  AnalyticsEnvironmentUsageApiModel
} from '../apis/dorc-api';

/**
 * Pure data helpers for the analytics page: month-range / project filtering
 * and derived series. Kept free of DOM and chart concerns so they can be
 * unit-tested directly.
 */

interface MonthRow {
  Year?: number;
  Month?: number;
}

interface ProjectRow {
  ProjectName?: string | null;
}

/** 'YYYY-MM' for a year/month pair. */
export function formatMonthOption(year: number, month: number): string {
  return `${year}-${String(month).padStart(2, '0')}`;
}

/** Comparable index for a 'YYYY-MM' option; undefined when not parseable. */
export function monthOptionToIndex(option: string): number | undefined {
  const match = /^(\d{4})-(\d{2})$/.exec(option);
  if (!match) return undefined;
  return Number(match[1]) * 12 + (Number(match[2]) - 1);
}

/** Distinct 'YYYY-MM' options present in the rows, ascending. */
export function distinctMonthOptions(rows: MonthRow[]): string[] {
  const options = new Set<string>();
  rows.forEach(row => {
    if (row.Year && row.Month) {
      options.add(formatMonthOption(row.Year, row.Month));
    }
  });
  return [...options].sort();
}

/** Distinct project names present in the rows, ascending. */
export function distinctProjects(rows: ProjectRow[]): string[] {
  const projects = new Set<string>();
  rows.forEach(row => {
    if (row.ProjectName) {
      projects.add(row.ProjectName);
    }
  });
  return [...projects].sort();
}

/**
 * Keep rows whose Year/Month fall inside the inclusive 'YYYY-MM' range.
 * An empty/unparseable bound leaves that side of the range open; rows with
 * missing Year/Month are dropped only when a bound is active.
 */
export function filterByMonthRange<T extends MonthRow>(
  rows: T[],
  from?: string,
  to?: string
): T[] {
  const fromIndex = from ? monthOptionToIndex(from) : undefined;
  const toIndex = to ? monthOptionToIndex(to) : undefined;
  if (fromIndex === undefined && toIndex === undefined) return rows;

  return rows.filter(row => {
    if (!row.Year || !row.Month) return false;
    const index = row.Year * 12 + (row.Month - 1);
    if (fromIndex !== undefined && index < fromIndex) return false;
    if (toIndex !== undefined && index > toIndex) return false;
    return true;
  });
}

/** Keep rows for the given project; an empty selection keeps everything. */
export function filterByProject<T extends ProjectRow>(
  rows: T[],
  project?: string
): T[] {
  if (!project) return rows;
  return rows.filter(row => row.ProjectName === project);
}

export interface EnvironmentStaleness {
  environmentName: string;
  daysSinceLastSuccess: number;
}

/**
 * Environments ranked stalest-first by days since their last successful
 * deployment. Environments that never succeeded are excluded (no date to
 * measure from).
 */
export function buildEnvironmentStaleness(
  rows: AnalyticsEnvironmentUsageApiModel[],
  now: Date,
  top: number
): EnvironmentStaleness[] {
  const msPerDay = 24 * 60 * 60 * 1000;
  return rows
    .filter(row => row.LastSuccessfulDeployment && row.EnvironmentName)
    .map(row => ({
      environmentName: row.EnvironmentName ?? '',
      daysSinceLastSuccess: Math.max(
        0,
        Math.floor(
          (now.getTime() -
            new Date(row.LastSuccessfulDeployment as string).getTime()) /
            msPerDay
        )
      )
    }))
    .sort((a, b) => b.daysSinceLastSuccess - a.daysSinceLastSuccess)
    .slice(0, top);
}

export interface ComponentFailureRate {
  componentName: string;
  failureRatePercent: number;
  attemptCount: number;
  retryAttemptCount: number;
}

/**
 * Components ranked by failure rate (percent of attempts that failed),
 * ignoring components below the minimum attempt volume so tiny samples
 * don't dominate the chart.
 */
export function buildComponentFailureRates(
  rows: AnalyticsComponentReliabilityApiModel[],
  minAttempts: number,
  top: number
): ComponentFailureRate[] {
  return rows
    .filter(
      row => (row.AttemptCount ?? 0) >= minAttempts && row.ComponentName
    )
    .map(row => ({
      componentName: row.ComponentName ?? '',
      failureRatePercent:
        Math.round(
          ((row.FailedCount ?? 0) / (row.AttemptCount ?? 1)) * 1000
        ) / 10,
      attemptCount: row.AttemptCount ?? 0,
      retryAttemptCount: row.RetryAttemptCount ?? 0
    }))
    .sort((a, b) => b.failureRatePercent - a.failureRatePercent)
    .slice(0, top);
}
