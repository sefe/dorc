import { expect } from '../_helpers';
import { dorcEnvironmentNameFromMetadata } from '../../src/helpers/dorc-environment-name';

describe('dorcEnvironmentNameFromMetadata', () => {
  it('extracts and trims the environment prefix', () => {
    expect(dorcEnvironmentNameFromMetadata(' DORC DV 02 - build 123')).to.equal(
      'DORC DV 02'
    );
  });

  it('rejects malformed non-string metadata', () => {
    expect(dorcEnvironmentNameFromMetadata(null)).to.equal(undefined);
    expect(dorcEnvironmentNameFromMetadata({ environment: 'DV' })).to.equal(
      undefined
    );
  });
});
