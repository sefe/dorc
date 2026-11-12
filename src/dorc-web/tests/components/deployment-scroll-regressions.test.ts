import { expect } from '../_helpers';
import { ComponentDeploymentResults } from '../../src/components/component-deployment-results';
import { ComponentPreviousAttempts } from '../../src/components/component-previous-attempts';
import { PageMonitorResult } from '../../src/pages/page-monitor-result';

const cssText = (component: { styles?: { cssText?: string } }) =>
  component.styles?.cssText ?? '';

describe('deployment result scrolling', () => {
  it('keeps wide component results horizontally reachable', () => {
    expect(cssText(ComponentDeploymentResults)).to.include('overflow-x: auto');
  });

  it('reserves space below the last row so the horizontal scrollbar does not cover row controls', async () => {
    expect(cssText(ComponentDeploymentResults)).to.include('::part(last-row)');
    expect(cssText(ComponentDeploymentResults)).to.include('padding-bottom: 18px');
    expect(cssText(ComponentPreviousAttempts)).to.include('::part(last-row)');
    expect(cssText(ComponentPreviousAttempts)).to.include('padding-bottom: 18px');

    const { fixture, html } = await import('../_helpers');
    const wrapper = await fixture<HTMLElement>(html`
      <div style="width: 500px;">
        <component-deployment-results
          .resultItems="${[
            {
              Id: 1,
              RequestId: 10,
              ProjectId: 1,
              ComponentId: 2,
              ComponentName: 'Deploy Copy Env Build',
              Status: 'Complete',
              StartedTime: '2026-11-09T12:26:52Z',
              CompletedTime: '2026-11-09T12:26:57Z',
              SourceRevisionLabel:
                'Observed PowerShell ba83bbf37f24242b88be1f3c6eb21750aa230a1e8ceab3058d3540a977a9e674',
              DefinitionRevision:
                '3f56a8443f56a8443f56a8443f56a8443f56a8443f56a8443f56a8443f56a844',
              Log: 'Starting Service Deployment... [2026-11-09 12:26:52] with a very long log line that forces horizontal scrolling in the grid\n26.11.09.3845'
            }
          ]}"
        ></component-deployment-results>
      </div>
    `);
    const el = wrapper.querySelector('component-deployment-results')!;
    const grid = el.shadowRoot!.querySelector('vaadin-grid') as HTMLElement;
    await new Promise(r => setTimeout(r, 100));
    const table = grid.shadowRoot!.querySelector('#table') as HTMLElement;
    const lastRow = grid.shadowRoot!.querySelector('[part~="last-row"]') as HTMLElement;
    const badge = el.shadowRoot!.querySelector('.dorc-sha-badge') as HTMLElement;
    const tableClientBottom = table.getBoundingClientRect().top + table.clientHeight;
    const badgeBottom = badge.getBoundingClientRect().bottom;
    expect(lastRow.getBoundingClientRect().height).to.be.greaterThan(50);
    expect(badgeBottom).to.be.lessThanOrEqual(tableClientBottom);
  });

  it('allows the result page to scroll in both directions', () => {
    expect(cssText(PageMonitorResult)).to.include('overflow: auto');
  });
});
