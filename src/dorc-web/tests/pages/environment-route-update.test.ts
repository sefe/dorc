import { expect, fixture, html } from '../_helpers';
import type { Tabs } from '@vaadin/tabs';
import '../../src/pages/page-environment';
import '../../src/pages/page-environment-components';
import type { PageEnvironment } from '../../src/pages/page-environment';
import type { PageEnvironmentComponents } from '../../src/pages/page-environment-components';

describe('environment route updates', () => {
  it('updates the selected environment tab on a reused layout', async () => {
    const el = await fixture<PageEnvironment>(
      html`<page-environment></page-environment>`
    );

    el.onRouteUpdate({ pathname: '/environment/DEV1/variables' });

    const tabs = el.shadowRoot?.getElementById('env-tabs') as Tabs;
    expect(tabs.selected).to.equal(1);
  });

  it('updates the selected component tab on a reused layout', async () => {
    const el = await fixture<PageEnvironmentComponents>(
      html`<page-environment-components></page-environment-components>`
    );

    el.onRouteUpdate({
      pathname: '/environment/DEV1/components/databases'
    });

    const tabs = el.shadowRoot?.getElementById('component-tabs') as Tabs;
    expect(tabs.selected).to.equal(1);
  });
});
