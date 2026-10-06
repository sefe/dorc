import { expect } from '../_helpers';
import { ComponentDeploymentResults } from '../../src/components/component-deployment-results';
import { PageMonitorResult } from '../../src/pages/page-monitor-result';

const cssText = (component: { styles?: { cssText?: string } }) =>
  component.styles?.cssText ?? '';

describe('deployment result scrolling', () => {
  it('keeps wide component results horizontally reachable', () => {
    expect(cssText(ComponentDeploymentResults)).to.include('overflow-x: auto');
  });

  it('allows the result page to scroll in both directions', () => {
    expect(cssText(PageMonitorResult)).to.include('overflow: auto');
  });
});
