import { expect, settle } from '../_helpers';
import { render } from 'lit';
import '../../src/pages/page-env-history';

// Drives the real `_commentRenderer`, not a lookalike template. The lookalike
// in grid-cell-recycling.test.ts demonstrates the mechanism but cannot catch a
// regression in this renderer's own binding — reverting it to `value-changed`
// left the whole suite green.
//
// The stakes: edit-comments-controls saves the row model this writes into, so a
// recycled cell that fires into the previous row's listener persists a comment
// the user never typed.

type History = { EnvironmentHistoryId?: number; Comment?: string };

type Page = HTMLElement & {
  _commentRenderer(item: History, model: { index: number }): unknown;
};

describe('page-env-history comment renderer', () => {
  let page: Page;
  let host: HTMLElement;

  beforeEach(async () => {
    // Not attached: connectedCallback would call the environment history API.
    page = document.createElement('page-env-history') as Page;
    host = document.createElement('div');
    document.body.appendChild(host);
    await settle();
  });

  afterEach(() => host.remove());

  it('does not write the next row comment into the previous row', async () => {
    const rowA: History = { EnvironmentHistoryId: 1, Comment: 'A comment' };
    const rowB: History = { EnvironmentHistoryId: 2, Comment: 'B comment' };

    render(page._commentRenderer(rowA, { index: 0 }) as never, host);
    await settle();

    // Recycling the cell: the same container re-rendered for the next row.
    render(page._commentRenderer(rowB, { index: 0 }) as never, host);
    await settle();

    expect(rowA.Comment, 'row A untouched').to.equal('A comment');
    expect(rowB.Comment, 'row B untouched').to.equal('B comment');
  });

  it('records a comment the user actually commits', async () => {
    const row: History = { EnvironmentHistoryId: 1, Comment: 'before' };

    render(page._commentRenderer(row, { index: 0 }) as never, host);
    await settle();

    const field = host.querySelector('vaadin-text-field') as HTMLElement & {
      readonly: boolean;
      value: string;
    };
    // The grid drops `readonly` when the row enters edit mode.
    field.readonly = false;
    field.value = 'after';
    field.dispatchEvent(new Event('change', { bubbles: true }));
    await settle();

    expect(row.Comment).to.equal('after');
  });
});
