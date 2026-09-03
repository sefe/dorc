import { expect } from '../_helpers';
import { PageServersList } from '../../src/pages/page-servers-list.js';

describe('PageServersList label colors', () => {
  it('uses button colors for application tags and chip colors for environments', () => {
    const styles = PageServersList.styles.toString();
    const tagStyles = styles.match(/\.tag\s*\{([^}]*)\}/)?.[1] ?? '';
    const environmentStyles = styles.match(/\.env\s*\{([^}]*)\}/)?.[1] ?? '';

    expect(tagStyles).to.include('--lumo-primary-text-color');
    expect(environmentStyles).to.include('--dorc-chip-bg');
  });
});
