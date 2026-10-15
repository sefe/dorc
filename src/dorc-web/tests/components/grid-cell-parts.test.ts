import { expect, fixture, html, setTheme, settle } from '../_helpers';
import type { DorcTheme } from '../_helpers';
import '../../src/components/env-deployments';
import type { EnvDeployments } from '../../src/components/env-deployments';

// Nine grids colour rows by returning part names from cellPartNameGenerator and
// styling them with `vaadin-grid::part(...)`. That contract lives entirely in
// Vaadin: 25.3 moved cell parts to declarative Lit rendering, and nothing else
// in the suite would notice if the generated parts stopped reaching the cells
// or the outer ::part rules stopped painting them. This pins both halves on
// one representative grid.

type Cell = HTMLTableCellElement;

function bodyCells(host: EnvDeployments): Cell[] {
  const grid = host.shadowRoot!.querySelector('vaadin-grid')!;
  return [
    ...grid.shadowRoot!.querySelectorAll<Cell>('#items [part~="body-cell"]')
  ];
}

// Cells for one row, found by the text in its first (Component Name) column.
function rowCells(host: EnvDeployments, componentName: string): Cell[] {
  const row = bodyCells(host)
    .map(cell => cell.parentElement!)
    .find(tr => {
      const slot = tr.querySelector('slot');
      const content = slot?.assignedElements()[0];
      return content?.textContent?.trim() === componentName;
    });
  expect(row, `row for ${componentName}`).to.exist;
  return [...row!.querySelectorAll<Cell>('[part~="body-cell"]')];
}

// The token as the browser resolves it, so it compares with getComputedStyle.
function resolvedToken(token: string): string {
  const probe = document.createElement('div');
  probe.style.backgroundColor = `var(${token})`;
  document.body.appendChild(probe);
  const value = getComputedStyle(probe).backgroundColor;
  probe.remove();
  return value;
}

describe('grid cell parts', () => {
  const builds = [
    { ComponentName: 'Passed', State: 'Complete' },
    { ComponentName: 'Broken', State: 'Failed' },
    { ComponentName: 'Waiting', State: 'Pending' }
  ];

  async function renderGrid(): Promise<EnvDeployments> {
    const host = await fixture<EnvDeployments>(
      html`<env-deployments .builds="${builds}"></env-deployments>`
    );
    await settle();
    return host;
  }

  it('puts the generated part names on every cell of the row', async () => {
    const host = await renderGrid();

    const parts = (name: string) =>
      rowCells(host, name).map(cell => cell.getAttribute('part'));

    for (const part of parts('Passed')) {
      expect(part).to.match(/\bsuccess\b/);
      expect(part).not.to.match(/\bfailure\b/);
    }
    for (const part of parts('Broken')) {
      expect(part).to.match(/\bfailure\b/);
      expect(part).not.to.match(/\bsuccess\b/);
    }
    for (const part of parts('Waiting')) {
      expect(part).not.to.match(/\b(success|failure)\b/);
    }
  });

  for (const theme of ['light', 'dark'] as DorcTheme[]) {
    it(`paints the parts with the status tokens (${theme})`, async () => {
      setTheme(theme);
      const host = await renderGrid();

      const background = (name: string) =>
        getComputedStyle(rowCells(host, name)[0]).backgroundColor;

      expect(background('Passed')).to.equal(resolvedToken('--dorc-success-bg'));
      expect(background('Broken')).to.equal(resolvedToken('--dorc-failure-bg'));
      expect(background('Waiting')).not.to.be.oneOf([
        resolvedToken('--dorc-success-bg'),
        resolvedToken('--dorc-failure-bg')
      ]);
    });
  }
});
