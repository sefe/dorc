import { describe, it, expect, vi } from 'vitest';
import { of } from 'rxjs';

// Pressing the pencil in a Value cell is a two-step dance: the control only
// dispatches `editing-started`, the host records the id, and the *cell must be
// repainted* for `.editing` to reach the control. Each host used to force that
// with `this.grid?.requestContentUpdate?.()` immediately after the assignment.
// This branch deleted those calls and left the `columnBodyRenderer` dependency
// array as the only trigger.
//
// So if the array is lost, clicking Edit does nothing at all — the field stays
// readonly and Save/Cancel never appear, on the Variables page, the environment
// Variables tab and the value lookup. That is the array being the mechanism
// rather than decoration, which is the case worth a test.
//
// These drive the real grid. Calling the renderer by hand and re-rendering it
// supplies the very repaint the array is supposed to trigger, so it passes with
// the array blanked — the trap round 7 fell into.

const { pagedSpy } = vi.hoisted(() => ({
  pagedSpy: vi.fn(() =>
    of({
      Items: [
        {
          Id: 1,
          Name: 'Var1',
          Value: 'v1',
          PropertyValueFilter: 'ENV',
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
    PropertyValuesApi: class {
      propertyValuesPagedGet = pagedSpy;
      propertyValuesScopeOptionsGet = () => of([]);
      propertyValuesGet = () => of([]);
    }
  };
});

await import('../../src/pages/page-variables');

const settle = () => new Promise(r => setTimeout(r, 250));

const controlIn = (el: HTMLElement) =>
  el.shadowRoot
    ?.querySelector('vaadin-grid')
    ?.querySelector('variable-value-controls') as
    | (HTMLElement & { editing: boolean })
    | null;

describe('variable value cells enter edit mode', () => {
  it('the Variables page repaints the cell into edit mode', async () => {
    const el = document.createElement('page-variables') as HTMLElement & {
      propertyValues: unknown[];
      updateComplete: Promise<unknown>;
    };
    document.body.appendChild(el);
    await el.updateComplete;
    await settle();

    el.propertyValues = [
      { Id: 1, Value: 'v1', PropertyValueFilter: 'ENV', Secure: false }
    ];
    await el.updateComplete;
    await settle();

    const control = controlIn(el);
    expect(control, 'the value cell rendered').to.not.equal(null);
    expect(control?.editing, 'not editing yet').to.equal(false);

    // Exactly what the control dispatches when the pencil is clicked.
    el.dispatchEvent(
      new CustomEvent('editing-started', {
        detail: { id: 1 },
        bubbles: true,
        composed: true
      })
    );
    await el.updateComplete;
    await settle();

    expect(controlIn(el)?.editing, 'the cell repainted into edit mode').to.equal(
      true
    );

    el.remove();
  });
});
