import { Observable, of } from 'rxjs';
import { vi } from 'vitest';
import { expect, fixture, html } from '../_helpers';
import {
  EnvironmentContentApiModel,
  EnvironmentContentBuildsApiModel,
  EnvironmentApiModel,
  RefDataEnvironmentsApi,
  RefDataEnvironmentsDetailsApi
} from '../../src/apis/dorc-api/index.js';
import '../../src/components/environment-tabs/env-deployments.js';
import type { EnvDeployments } from '../../src/components/environment-tabs/env-deployments.js';

describe('EnvDeployments date filter', () => {
  beforeEach(() => {
    const environmentsApi = RefDataEnvironmentsApi.prototype as unknown as {
      refDataEnvironmentsGet: () => Observable<EnvironmentApiModel[]>;
    };
    vi.spyOn(environmentsApi, 'refDataEnvironmentsGet').mockReturnValue(
      of([{ EnvironmentId: 1, EnvironmentName: 'Test Environment' }])
    );

    const detailsApi = RefDataEnvironmentsDetailsApi.prototype as unknown as {
      refDataEnvironmentsDetailsIdGet: () => Observable<EnvironmentContentApiModel>;
      refDataEnvironmentsDetailsGetComponentStatuesGet: () => Observable<
        EnvironmentContentBuildsApiModel[]
      >;
    };
    vi.spyOn(detailsApi, 'refDataEnvironmentsDetailsIdGet').mockReturnValue(
      of({ EnvironmentName: 'Test Environment', Builds: [] })
    );
    vi.spyOn(
      detailsApi,
      'refDataEnvironmentsDetailsGetComponentStatuesGet'
    ).mockReturnValue(of([]));
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('clears the grid when the selected period has no deployments', async () => {
    const el = await fixture<EnvDeployments>(
      html`<env-deployments></env-deployments>`
    );
    await el.updateComplete;
    el.loading = false;
    el.deployments = [
      {
        RequestId: 1,
        ComponentName: 'Old deployment',
        UpdateDate: '2026-08-25T12:00:00'
      }
    ];
    await el.updateComplete;

    const picker = el.shadowRoot!.querySelector(
      '#deployments-filter'
    ) as HTMLElement & { value: string };
    picker.value = '2026-08-26T16:00';
    el.applyDateTimeFilter();

    expect(el.deployments).to.deep.equal([]);
    expect(el.applyingNewFilter).to.equal(false);
  });
});
