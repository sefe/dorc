import { expect, fixture, html, settle } from '../_helpers';
import type { HegsJsonDiff } from '../../src/components/hegs-json-diff';
import '../../src/components/hegs-json-diff';
import { buildSideBySide, computeLineDiff } from '../../src/helpers/text-diff';

const lines = (count: number, prefix = 'same') =>
  Array.from({ length: count }, (_, i) => `${prefix} ${i}`);

describe('hegs-json-diff', () => {
  it('pairs changed lines side by side with character highlights', async () => {
    const before = ['{', '  "Name": "Parent",', '  "Enabled": true', '}'].join(
      '\n'
    );
    const after = ['{', '  "Name": "Parent 2",', '  "Enabled": true', '}'].join(
      '\n'
    );
    const el = await fixture<HegsJsonDiff>(
      html`<hegs-json-diff
        .oldText=${before}
        .newText=${after}
      ></hegs-json-diff>`
    );
    await settle();
    const root = el.shadowRoot!;
    expect(
      root.querySelector('.summary')?.textContent?.replace(/\s+/g, ' ').trim()
    ).to.equal('+1 -1 lines');
    const removed = root.querySelector('.diff-cell.line-remove')!;
    const added = root.querySelector('.diff-cell.line-add')!;
    expect(removed.textContent).to.contain('"Name": "Parent",');
    expect(added.querySelector('.highlight')?.textContent).to.equal(' 2');
    expect(root.querySelectorAll('.fold')).to.have.length(0);
  });

  it('folds long unchanged runs and expands them on demand', async () => {
    const before = [...lines(20), 'old tail'].join('\n');
    const after = [...lines(20), 'new tail'].join('\n');
    const el = await fixture<HegsJsonDiff>(
      html`<hegs-json-diff
        .oldText=${before}
        .newText=${after}
      ></hegs-json-diff>`
    );
    await settle();
    const root = el.shadowRoot!;
    const fold = root.querySelector<HTMLButtonElement>('.fold')!;
    expect(fold.textContent?.trim()).to.contain('17 unchanged lines');
    // 3 trailing context rows + 1 change row = 4 rows of 2 cells
    expect(root.querySelectorAll('.diff-cell')).to.have.length(8);
    fold.click();
    await settle();
    expect(root.querySelectorAll('.fold')).to.have.length(0);
    expect(root.querySelectorAll('.diff-cell')).to.have.length(42);
  });

  it('shows every line when context is negative', async () => {
    const el = await fixture<HegsJsonDiff>(
      html`<hegs-json-diff
        .oldText=${lines(30).join('\n')}
        .newText=${[...lines(30), 'extra'].join('\n')}
        .context=${-1}
      ></hegs-json-diff>`
    );
    await settle();
    expect(el.shadowRoot!.querySelectorAll('.fold')).to.have.length(0);
    expect(el.shadowRoot!.querySelectorAll('.diff-cell')).to.have.length(62);
  });

  it('pairs surplus inserts and deletes positionally', () => {
    const rows = buildSideBySide(computeLineDiff('a\nb\nc', 'a\nx\ny\nc'));
    expect(rows.map(r => r.kind)).to.deep.equal([
      'keep',
      'change',
      'insert',
      'keep'
    ]);
    expect(rows[2].left).to.equal(null);
    expect(rows[2].right).to.equal('y');
  });
});
