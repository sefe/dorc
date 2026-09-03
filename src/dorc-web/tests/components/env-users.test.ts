import { Observable, of } from 'rxjs';
import { vi } from 'vitest';
import { expect, fixture, html } from '../_helpers';
import {
  EnvironmentContentApiModel,
  EnvironmentApiModel,
  RefDataEnvironmentsApi,
  RefDataEnvironmentsDetailsApi
} from '../../src/apis/dorc-api/index.js';
import '../../src/components/environment-tabs/env-users.js';
import type { EnvUsers } from '../../src/components/environment-tabs/env-users.js';

describe('EnvUsers details', () => {
  beforeEach(() => {
    const environmentsApi = RefDataEnvironmentsApi.prototype as unknown as {
      refDataEnvironmentsGet: () => Observable<EnvironmentApiModel[]>;
    };
    vi.spyOn(environmentsApi, 'refDataEnvironmentsGet').mockReturnValue(
      of([{ EnvironmentId: 1, EnvironmentName: 'Test Environment' }])
    );

    const detailsApi = RefDataEnvironmentsDetailsApi.prototype as unknown as {
      refDataEnvironmentsDetailsIdGet: () => Observable<EnvironmentContentApiModel>;
    };
    vi.spyOn(detailsApi, 'refDataEnvironmentsDetailsIdGet').mockReturnValue(
      of({ EnvironmentName: 'Test Environment', EndurUsers: [] })
    );
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('fills available height when open and shrinks when collapsed', async () => {
    const el = await fixture<EnvUsers>(html`<env-users></env-users>`);
    el.style.height = '600px';
    await el.updateComplete;

    const details = el.shadowRoot!.querySelector('vaadin-details')!;
    const applicationUsers = details.querySelector(
      '#application-users'
    ) as HTMLElement;
    expect(applicationUsers).to.not.equal(null);
    expect(applicationUsers.getBoundingClientRect().height).to.be.greaterThan(
      400
    );

    details.removeAttribute('opened');
    await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));

    expect(details.getBoundingClientRect().height).to.be.lessThan(100);
  });
});
