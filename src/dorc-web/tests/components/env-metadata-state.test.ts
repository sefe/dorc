import { expect, fixture, html } from '../_helpers';
import '../../src/components/environment-tabs/env-metadata';
import type { EnvMetadata } from '../../src/components/environment-tabs/env-metadata';
import type { EnvControlCenter } from '../../src/components/environment-tabs/env-control-center';

describe('EnvMetadata control-center state', () => {
  it('passes the loaded environment state to the control center', async () => {
    const environment = {
      EnvironmentId: 42,
      EnvironmentName: 'DEV1'
    };
    const envContent = {
      EnvironmentName: 'DEV1'
    };
    const el = await fixture<EnvMetadata>(html`
      <env-metadata
        .environment="${environment}"
        .envContent="${envContent}"
      ></env-metadata>
    `);
    await el.updateComplete;

    const controlCenter = el.shadowRoot?.querySelector(
      'env-control-center'
    ) as EnvControlCenter | null;

    expect(controlCenter?.environment).to.equal(environment);
    expect(controlCenter?.envContent).to.equal(envContent);
  });
});
