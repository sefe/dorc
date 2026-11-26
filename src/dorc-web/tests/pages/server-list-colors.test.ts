import { render } from 'lit';
import { expect } from '../_helpers';
import '../../src/pages/page-daemons-list.js';
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

describe('PageDaemonsList last seen status colors', () => {
  it('uses --dorc-success-text (not --dorc-success-bg) for running daemons and appropriate tokens for other states', () => {
    const page = document.createElement('page-daemons-list') as unknown as {
      _lastSeenRenderer: (d: unknown) => unknown;
    };
    const host = document.createElement('div');

    render(
      page._lastSeenRenderer({
        LastSeenDate: new Date(Date.now() - 86_400_000).toISOString(),
        LastSeenStatus: 'Running'
      }),
      host
    );
    const runningSpan = host.querySelector('span');
    expect(runningSpan?.getAttribute('style')).to.include('--dorc-success-text');
    expect(runningSpan?.getAttribute('style')).to.not.include('--dorc-success-bg');

    render(
      page._lastSeenRenderer({
        LastSeenDate: new Date(Date.now() - 86_400_000).toISOString(),
        LastSeenStatus: 'Stopped'
      }),
      host
    );
    expect(host.querySelector('span')?.getAttribute('style')).to.include('color: inherit');

    render(
      page._lastSeenRenderer({
        LastSeenDate: new Date(Date.now() - 86_400_000).toISOString(),
        LastSeenStatus: ''
      }),
      host
    );
    expect(host.querySelector('span')?.getAttribute('style')).to.include('--dorc-error-color');

    render(page._lastSeenRenderer({}), host);
    expect(host.querySelector('span')?.textContent?.trim()).to.equal('Never');
    expect(host.querySelector('span')?.getAttribute('style')).to.include('--dorc-text-secondary');
  });
});
