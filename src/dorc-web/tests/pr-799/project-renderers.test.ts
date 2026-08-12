import { render } from 'lit';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { expect, fixture, html, settle } from '../_helpers';
import '../../src/components/grid-button-groups/project-controls';
import '../../src/components/component-deployment-results';
import '../../src/pages/page-project-components';
import '../../src/pages/page-variables-value-lookup';

const { lookupSpy } = vi.hoisted(() => ({
  lookupSpy: vi.fn(() =>
    of({
      Items: [
        {
          PropertyValueId: 12,
          PropertyValue: 'value',
          PropertyValueScope: 'DEV',
          PropertyValueScopeId: 1,
          UserEditable: true,
          PropertyId: 3,
          Property: 'ConnectionString',
          Secure: false
        }
      ],
      TotalItems: 1
    })
  )
}));

vi.mock('../../src/apis/dorc-api', async importOriginal => {
  const actual = (await importOriginal()) as Record<string, unknown>;
  return {
    ...actual,
    RefDataSearchPropertyValuesApi: class {
      refDataSearchPropertyValuesPut = lookupSpy;
    }
  };
});

describe('PR #799 project action menu accessibility', () => {
  let originalMatchMedia: typeof window.matchMedia;

  beforeEach(() => {
    originalMatchMedia = window.matchMedia;
    window.matchMedia = () =>
      ({
        matches: true,
        media: '(max-width: 768px)',
        onchange: null,
        addEventListener: () => {},
        removeEventListener: () => {},
        addListener: () => {},
        removeListener: () => {},
        dispatchEvent: () => true
      }) as MediaQueryList;
  });

  afterEach(() => {
    window.matchMedia = originalMatchMedia;
  });

  it('uses a labelled keyboard menu and returns focus to its trigger on Escape', async () => {
    const el = await fixture<HTMLElement>(html`
      <project-controls
        .project="${{ ProjectId: 8, ProjectName: 'Payments' }}"
      ></project-controls>
    `);
    const trigger = el.shadowRoot?.querySelector(
      'vaadin-button[aria-haspopup="menu"]'
    ) as HTMLElement;
    trigger.click();
    await el.updateComplete;

    const menu = document.querySelector('[role="menu"]') as HTMLElement;
    const items = Array.from(
      menu.querySelectorAll<HTMLElement>('[role="menuitem"]')
    );
    expect(trigger.getAttribute('aria-expanded')).to.equal('true');
    expect(menu.getAttribute('aria-labelledby')).to.equal(trigger.id);
    expect(items.map(item => item.textContent?.trim())).to.include(
      'Components'
    );
    expect(document.activeElement).to.equal(items[0]);

    menu.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown' }));
    expect(document.activeElement).to.equal(items[1]);

    menu.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    await el.updateComplete;
    expect(document.querySelector('[role="menu"]')).to.equal(null);
    // Focus inside a shadow root is retargeted to its host for the document,
    // while the shadow root exposes the concrete trigger that owns it.
    expect(document.activeElement).to.equal(el);
    expect(el.shadowRoot?.activeElement).to.equal(trigger);
  });
});

describe('PR #799 deployment result actions', () => {
  it('opens the Terraform plan from the rendered action and publishes the resulting status change', async () => {
    const results = [{ Id: 17, Status: 'WaitingConfirmation' }];
    const el = await fixture<HTMLElement>(html`
      <component-deployment-results
        .resultItems="${results}"
      ></component-deployment-results>
    `);
    let statusChange: unknown;
    el.addEventListener('deployment-status-changed', event => {
      statusChange = (event as CustomEvent).detail;
    });

    const actionHost = await fixture<HTMLElement>(html`<div></div>`);
    render(
      (
        el as HTMLElement & {
          actionsRenderer(item: unknown): unknown;
        }
      ).actionsRenderer(results[0]) as never,
      actionHost
    );
    (
      actionHost.querySelector(
        'vaadin-button[title="View Terraform Plan"]'
      ) as HTMLElement
    ).click();
    await (el as HTMLElement & { updateComplete: Promise<unknown> })
      .updateComplete;

    const dialog = el.shadowRoot?.querySelector(
      'terraform-plan-dialog'
    ) as HTMLElement & { opened: boolean; deploymentResultId: number };
    expect(dialog.opened).to.equal(true);
    expect(dialog.deploymentResultId).to.equal(17);

    el.dispatchEvent(
      new CustomEvent('terraform-plan-confirmed', {
        detail: { deploymentResultId: 17 },
        bubbles: true,
        composed: true
      })
    );
    await el.updateComplete;

    expect(results[0].Status).to.equal('Confirmed');
    expect(statusChange).to.deep.equal({
      deploymentResultId: 17,
      newStatus: 'Confirmed'
    });
  });
});

describe('PR #799 component and variable renderers', () => {
  let host: HTMLElement;

  beforeEach(() => {
    host = document.createElement('div');
    document.body.appendChild(host);
  });

  afterEach(() => {
    host.remove();
    vi.restoreAllMocks();
  });

  it('renders an actionable button for deployed component build numbers', async () => {
    const page = document.createElement(
      'page-project-components'
    ) as HTMLElement & {
      buildNumberRenderer(row: unknown): unknown;
    };
    const openSpy = vi.spyOn(window, 'open').mockReturnValue(null);

    render(
      page.buildNumberRenderer({
        buildNumber: '2026.08.12',
        requestId: 42,
        build: { RequestId: 42 }
      }) as never,
      host
    );
    await settle();

    const button = host.querySelector(
      'button.request-link[data-request-id="42"]'
    ) as HTMLButtonElement;
    expect(button, 'deployed build is a semantic button').to.not.equal(null);
    expect(button.type).to.equal('button');
    button.click();
    expect(openSpy.mock.calls).to.deep.equal([
      ['/monitor-result/42', '_blank', 'noopener,noreferrer']
    ]);
  });

  it('re-renders the real variable lookup grid cell in edit mode after its public event', async () => {
    const page = await fixture<
      HTMLElement & {
        updateComplete: Promise<unknown>;
      }
    >(html`<page-variables-value-lookup></page-variables-value-lookup>`);
    await settle(300);

    const control = page.shadowRoot
      ?.querySelector('vaadin-grid')
      ?.querySelector('variable-value-controls') as
      (HTMLElement & { editing: boolean }) | null;
    expect(control, 'the lookup grid renders its value control').to.not.equal(
      null
    );
    expect(control?.editing).to.equal(false);

    control?.dispatchEvent(
      new CustomEvent('editing-started', {
        detail: { id: 12 },
        bubbles: true,
        composed: true
      })
    );
    await page.updateComplete;
    await settle(300);

    expect(
      (
        page.shadowRoot
          ?.querySelector('vaadin-grid')
          ?.querySelector('variable-value-controls') as HTMLElement & {
          editing: boolean;
        }
      ).editing
    ).to.equal(true);
  });
});
