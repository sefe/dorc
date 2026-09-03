import { expect, settle } from '../_helpers';
import { render } from 'lit';
import '../../src/pages/page-env-history';

// The `live()` half of this branch's recycling work missed the environment
// history comment cell. The gesture-only `@change` half was fixed and is
// guarded (env-history-comment-renderer.test.ts); this is the other half.
//
// Without `live()`, Lit dirty-checks against the value it last committed. A
// user's keystrokes never go through Lit, so when the grid re-renders the same
// comment — which is what Cancel does, via `clearCache()` refetching a row whose
// comment has not changed — the commit is skipped and the field keeps showing
// the abandoned text. The row then displays a comment that is not in the
// database.
//
// The same skip hands a recycled cell the previous row's text whenever the two
// rows' comments happen to be equal.

type History = { Comment?: string };

type Page = HTMLElement & {
  _commentRenderer(history: History, model: { index: number }): unknown;
};

describe('env history comment cell', () => {
  let page: Page;
  let host: HTMLElement;

  beforeEach(async () => {
    // Not attached: connectedCallback drives the page's data provider.
    page = document.createElement('page-env-history') as Page;
    host = document.createElement('div');
    document.body.appendChild(host);
    await settle();
  });

  afterEach(() => host.remove());

  const field = () =>
    host.querySelector('vaadin-text-field') as HTMLElement & { value: string };

  it('re-applies the stored comment after an abandoned edit', async () => {
    const history: History = { Comment: 'Deployed v1' };
    render(page._commentRenderer(history, { index: 0 }) as never, host);
    await settle();

    // The user types. This does not go through Lit.
    field().value = 'typo';
    await settle();

    // Cancel: the grid refetches and re-renders the unchanged model.
    render(page._commentRenderer(history, { index: 0 }) as never, host);
    await settle();

    expect(field().value, 'shows the stored comment again').to.equal(
      'Deployed v1'
    );
  });

  it('does not leak an edit into a recycled row with the same comment', async () => {
    const rowA: History = { Comment: 'A' };
    const rowB: History = { Comment: 'A' };

    render(page._commentRenderer(rowA, { index: 0 }) as never, host);
    await settle();
    const el = field();

    el.value = 'scribble';
    await settle();

    render(page._commentRenderer(rowB, { index: 0 }) as never, host);
    await settle();

    expect(field(), 'element reused').to.equal(el);
    expect(field().value, 'shows the new row').to.equal('A');
  });
});
