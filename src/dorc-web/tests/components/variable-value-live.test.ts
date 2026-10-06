import { expect, settle } from '../_helpers';
import '../../src/components/grid-button-groups/variable-value-controls';

// `variable-value-controls` is the editable Value cell of three grids (the
// Variables page, the environment Variables tab, and the value lookup). Its
// `.value` binding had no `live()`, so Lit dirty-checked against the value it
// last committed and skipped the write when the model had not changed — leaving
// whatever the DOM happened to hold. Under the directives a cell is reused
// across rows, so that stale text belongs to the previous variable.
//
// Scope note: this covers the recycling half only. There is a second, older
// defect in this component — `@value-changed` writes every keystroke straight
// into the grid item, and `_cancelClick` does not restore it — so an abandoned
// edit leaves the item mutated client-side. Both of those lines are byte
// identical at the merge base, so they are pre-existing and out of scope here;
// `live()` cannot help, because the model and the DOM genuinely agree by then.

type Control = HTMLElement & {
  value: { Id?: number; Value?: string };
  editing: boolean;
};

const fieldIn = (el: Control) =>
  el.shadowRoot?.querySelector('vaadin-text-field') as HTMLElement & {
    value: string;
  };

describe('variable value cell', () => {
  let el: Control;

  beforeEach(async () => {
    el = document.createElement('variable-value-controls') as Control;
    el.value = { Id: 1, Value: 'orig' };
    el.editing = true;
    document.body.appendChild(el);
    await settle();
  });

  afterEach(() => el.remove());

  it('shows the new row when the cell is recycled onto an equal value', async () => {
    // The DOM diverges without Lit knowing — the shape a half-typed cell is in
    // when the grid scrolls.
    fieldIn(el).value = 'scribble';
    await settle();

    // Recycled onto a different variable whose stored value equals the one this
    // cell started with, so Lit's committed value is unchanged.
    el.value = { Id: 2, Value: 'orig' };
    await settle();

    expect(fieldIn(el).value, 'shows the new row, not the leftover').to.equal(
      'orig'
    );
  });

  it('re-applies the stored value when the model is restored', async () => {
    fieldIn(el).value = 'scribble';
    await settle();

    // A caller that does restore the model — which is what the recycling path
    // does — must repaint the field.
    el.value = { Id: 1, Value: 'orig' };
    await settle();

    expect(fieldIn(el).value).to.equal('orig');
  });
});
