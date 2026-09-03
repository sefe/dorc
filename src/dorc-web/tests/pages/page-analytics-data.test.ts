import { describe, it, expect } from 'vitest';
import {
  buildComponentFailureRates,
  buildEnvironmentStaleness,
  distinctMonthOptions,
  distinctProjects,
  filterByMonthRange,
  filterByProject,
  formatMonthOption,
  monthOptionToIndex
} from '../../src/pages/page-analytics-data';

describe('page-analytics-data helpers', () => {
  describe('month options', () => {
    it('formats year/month as YYYY-MM', () => {
      expect(formatMonthOption(2026, 3)).toBe('2026-03');
      expect(formatMonthOption(2026, 12)).toBe('2026-12');
    });

    it('parses options to a comparable index', () => {
      expect(monthOptionToIndex('2026-01')).toBe(2026 * 12);
      expect(monthOptionToIndex('2025-12')).toBe(2025 * 12 + 11);
      expect(monthOptionToIndex('garbage')).toBeUndefined();
    });

    it('derives distinct sorted options and skips incomplete rows', () => {
      const rows = [
        { Year: 2026, Month: 2 },
        { Year: 2025, Month: 11 },
        { Year: 2026, Month: 2 },
        { Year: undefined, Month: 1 }
      ];
      expect(distinctMonthOptions(rows)).toEqual(['2025-11', '2026-02']);
    });
  });

  describe('filterByMonthRange', () => {
    const rows = [
      { Year: 2025, Month: 6, name: 'a' },
      { Year: 2025, Month: 12, name: 'b' },
      { Year: 2026, Month: 1, name: 'c' },
      { Year: undefined, Month: undefined, name: 'd' }
    ];

    it('returns everything (incl. incomplete rows) with no bounds', () => {
      expect(filterByMonthRange(rows)).toHaveLength(4);
    });

    it('applies an inclusive from bound', () => {
      const result = filterByMonthRange(rows, '2025-12');
      expect(result.map(r => r.name)).toEqual(['b', 'c']);
    });

    it('applies an inclusive to bound', () => {
      const result = filterByMonthRange(rows, undefined, '2025-12');
      expect(result.map(r => r.name)).toEqual(['a', 'b']);
    });

    it('applies both bounds', () => {
      const result = filterByMonthRange(rows, '2025-07', '2025-12');
      expect(result.map(r => r.name)).toEqual(['b']);
    });
  });

  describe('filterByProject / distinctProjects', () => {
    const rows = [
      { ProjectName: 'B' },
      { ProjectName: 'A' },
      { ProjectName: 'A' },
      { ProjectName: null }
    ];

    it('keeps all rows for an empty selection', () => {
      expect(filterByProject(rows, '')).toHaveLength(4);
      expect(filterByProject(rows, undefined)).toHaveLength(4);
    });

    it('filters to the selected project', () => {
      expect(filterByProject(rows, 'A')).toHaveLength(2);
    });

    it('lists distinct projects sorted', () => {
      expect(distinctProjects(rows)).toEqual(['A', 'B']);
    });
  });

  describe('buildEnvironmentStaleness', () => {
    it('ranks stalest first and excludes never-succeeded environments', () => {
      const now = new Date('2026-06-11T00:00:00Z');
      const rows = [
        {
          EnvironmentName: 'FRESH',
          LastSuccessfulDeployment: '2026-06-10T00:00:00Z'
        },
        {
          EnvironmentName: 'STALE',
          LastSuccessfulDeployment: '2025-06-11T00:00:00Z'
        },
        { EnvironmentName: 'NEVER', LastSuccessfulDeployment: null }
      ];

      const result = buildEnvironmentStaleness(rows, now, 10);

      expect(result).toHaveLength(2);
      expect(result[0].environmentName).toBe('STALE');
      expect(result[0].daysSinceLastSuccess).toBe(365);
      expect(result[1].environmentName).toBe('FRESH');
      expect(result[1].daysSinceLastSuccess).toBe(1);
    });

    it('clamps future dates to zero and honors the top limit', () => {
      const now = new Date('2026-06-11T00:00:00Z');
      const rows = [
        {
          EnvironmentName: 'FUTURE',
          LastSuccessfulDeployment: '2026-07-01T00:00:00Z'
        },
        {
          EnvironmentName: 'OLD',
          LastSuccessfulDeployment: '2024-01-01T00:00:00Z'
        }
      ];

      const result = buildEnvironmentStaleness(rows, now, 1);

      expect(result).toHaveLength(1);
      expect(result[0].environmentName).toBe('OLD');
    });
  });

  describe('buildComponentFailureRates', () => {
    it('computes percentage, enforces min volume, ranks worst first', () => {
      const rows = [
        { ComponentName: 'Flaky', AttemptCount: 100, FailedCount: 25, RetryAttemptCount: 10 },
        { ComponentName: 'Solid', AttemptCount: 200, FailedCount: 2, RetryAttemptCount: 0 },
        { ComponentName: 'TinySample', AttemptCount: 2, FailedCount: 2, RetryAttemptCount: 0 }
      ];

      const result = buildComponentFailureRates(rows, 20, 10);

      expect(result).toHaveLength(2);
      expect(result[0].componentName).toBe('Flaky');
      expect(result[0].failureRatePercent).toBe(25);
      expect(result[1].componentName).toBe('Solid');
      expect(result[1].failureRatePercent).toBe(1);
    });

    it('honors the top limit', () => {
      const rows = Array.from({ length: 30 }, (_, i) => ({
        ComponentName: `C${i}`,
        AttemptCount: 50,
        FailedCount: i,
        RetryAttemptCount: 0
      }));

      expect(buildComponentFailureRates(rows, 20, 5)).toHaveLength(5);
    });
  });
});
