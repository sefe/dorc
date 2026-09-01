import { expect } from '../_helpers';
import environmentsListSource from '../../src/pages/page-environments-list.ts?raw';

describe('page-environments-list API clients', () => {
  it('uses the configured API origin and authentication middleware', () => {
    expect(environmentsListSource).to.contain(
      "import { dorcApiConfiguration } from '../services/dorc-api-configuration'"
    );
    expect(environmentsListSource).to.contain(
      'new RefDataRolesApi(dorcApiConfiguration)'
    );
    expect(environmentsListSource).to.contain(
      'new RefDataEnvironmentsApi(dorcApiConfiguration)'
    );
  });
});
