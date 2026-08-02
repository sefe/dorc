/**
 * P-003 (axe-core under U-16 approval) + SC5 assertions for the §3.4 list
 * row: the narrow rendering's internal semantics are new obligations — the
 * disclosure affordance and actions must be reachable, and axe must find no
 * violations in the rendered list.
 */
import { expect, fixture, html } from '../_helpers';
import axe from 'axe-core';
import '../../src/components/component-deployment-results';

const settle = () =>
  new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));

describe('list-row accessibility (axe + SC5)', () => {
  it('axe finds no violations in the narrow list rendering', async () => {
    const wrapper = (await fixture(html`
      <div style="width: 375px">
        <component-deployment-results
          .resultItems="${[
            {
              ComponentName: 'Dorc.Api.Deploy',
              Status: 'Failed',
              Log: 'System.Exception: deploy step failed',
              StartedTime: '2026-08-02T14:32:07',
              CompletedTime: '2026-08-02T14:39:51',
              Id: 1
            }
          ]}"
        ></component-deployment-results>
      </div>
    `)) as HTMLElement;
    const el = wrapper.querySelector('component-deployment-results')!;
    await (el as HTMLElement & { updateComplete: Promise<unknown> })
      .updateComplete;
    await settle();
    await settle();

    const results = await axe.run(wrapper, {
      rules: {
        // page-level rules do not apply to a component fixture
        region: { enabled: false },
        'page-has-heading-one': { enabled: false }
      }
    });
    const violations = results.violations.map(
      v => `${v.id}: ${v.nodes.map(n => n.target.join(' ')).join('; ')}`
    );
    expect(violations, violations.join('\n')).to.deep.equal([]);
  });

  it('the actions slot is reachable in the narrow row (SC11: no wide-width affordance lost)', async () => {
    const wrapper = (await fixture(html`
      <div style="width: 375px">
        <component-deployment-results
          .resultItems="${[
            {
              ComponentName: 'comp1',
              Status: 'WaitingConfirmation',
              Log: 'log',
              Id: 7
            }
          ]}"
        ></component-deployment-results>
      </div>
    `)) as HTMLElement;
    const el = wrapper.querySelector('component-deployment-results')!;
    await (el as HTMLElement & { updateComplete: Promise<unknown> })
      .updateComplete;
    await settle();
    await settle();

    const sr = el.shadowRoot!;
    const actions = sr.querySelector('.dorc-list-row__actions');
    expect(actions, 'actions area rendered').to.exist;
    const buttons = actions!.querySelectorAll('vaadin-button');
    // log button always; terraform button for WaitingConfirmation
    expect(buttons.length).to.equal(2);
  });
});
