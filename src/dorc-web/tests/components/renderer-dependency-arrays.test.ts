import { expect } from '../_helpers';
import '../../src/pages/page-config-values-list';
import '../../src/components/map-daemons';

// The dependency array is the migration's whole correctness contract:
// LitRendererDirective returns early and never reassigns the renderer when
// nothing in the array changed. Blanking every array in `src` left all but six
// binding sites green, so most of them were unenforced.
//
// These cover the two that replaced a deleted imperative repaint, where the
// array is the mechanism rather than decoration — losing it leaves a
// permanently stale control, not a cosmetic lag.
//
// They drive the real grid. Calling a renderer method directly and re-rendering
// it by hand proves only that the body reads the flag; it passes with the array
// blanked, because the test supplies the repaint the array was supposed to
// trigger. Both mutations below were confirmed to fail these as written.

const settle = () => new Promise(r => setTimeout(r, 200));

describe('dependency arrays that replaced an imperative repaint', () => {
  it('config values: the roles callback re-enables the checkboxes', async () => {
    // `loadRoles()` used to call grid.requestContentUpdate(). It resolves after
    // first paint, so the grid renders with isAdmin false; without the
    // dependency the Secure and Is-For-Prod columns stay disabled forever and
    // the page is silently read-only for administrators.
    const el = document.createElement('page-config-values-list') as HTMLElement & {
      isAdmin: boolean;
      updateComplete: Promise<unknown>;
    };
    el.isAdmin = false;
    document.body.appendChild(el);
    await el.updateComplete;
    await settle();
    // After the responsive mixin has measured, not before — it re-measures on
    // connect and would overwrite this. The two checkbox columns are
    // `?hidden="${this._narrowScreen}"`, and a hidden Vaadin column renders no
    // cells at all; the test viewport is narrow.
    (el as unknown as { _narrowScreen: boolean })._narrowScreen = false;
    await el.updateComplete;
    await settle();

    // The page's own load path: it clears the `loading` spinner that otherwise
    // renders instead of the grid, and `loading` is a plain field, so assigning
    // it directly would not re-render.
    (
      el as unknown as {
        setConfigValues(v: unknown[]): void;
      }
    ).setConfigValues([
      { Id: 1, Key: 'k', Value: 'v', Secure: false, IsForProd: false }
    ]);
    await el.updateComplete;
    await settle();

    const checkboxes = () =>
      Array.from(
        el.shadowRoot?.querySelector('vaadin-grid')?.querySelectorAll(
          'vaadin-checkbox'
        ) ?? []
      ) as (HTMLElement & { disabled: boolean })[];

    expect(checkboxes().length, 'checkbox cells rendered').to.be.greaterThan(0);
    expect(
      checkboxes().every(c => c.disabled),
      'disabled before roles resolve'
    ).to.equal(true);

    el.isAdmin = true;
    await el.updateComplete;
    await settle();

    expect(
      checkboxes().some(c => !c.disabled),
      'enabled once roles resolve'
    ).to.equal(true);

    el.remove();
  });

  it('map-daemons: readonly reaches the unmap button', async () => {
    // `readonly` derives from `environment?.UserEditable`, which lands
    // asynchronously. attached-env-tenants has the identical fix and a guard;
    // this one had none, so the Unmap buttons could stay enabled and red on a
    // read-only environment.
    const el = document.createElement('map-daemons') as HTMLElement & {
      readonly: boolean;
      mappedDaemons: unknown;
      updateComplete: Promise<unknown>;
    };
    el.readonly = false;
    document.body.appendChild(el);
    await el.updateComplete;

    el.mappedDaemons = [{ Id: 1, Name: 'Scheduler', DisplayName: 'Scheduler' }];
    await el.updateComplete;
    await settle();

    const unmapButtons = () =>
      Array.from(
        el.shadowRoot?.querySelector('vaadin-grid')?.querySelectorAll(
          'vaadin-button'
        ) ?? []
      ) as (HTMLElement & { disabled: boolean })[];

    expect(unmapButtons().length, 'unmap buttons rendered').to.be.greaterThan(0);
    expect(
      unmapButtons().every(b => !b.disabled),
      'enabled while editable'
    ).to.equal(true);

    el.readonly = true;
    await el.updateComplete;
    await settle();

    expect(
      unmapButtons().every(b => b.disabled),
      'disabled once readonly lands'
    ).to.equal(true);

    el.remove();
  });
});
