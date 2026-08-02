import {
  expect,
  settle,
  dialogIn,
  isDialogOpen,
  inDialog,
  pressEscape,
  clickOutside
} from '../_helpers';
import '../../src/pages/page-sql-ports-list';

// Reference conversion for the dialog-consistency work: page-sql-ports-list's
// "Add SQL Port" dialog, paper-dialog -> vaadin-dialog.
//
// These tests are the contract every later conversion copies, so they assert
// the behaviours two review rounds found the planning documents guessing at:
// which dismissal paths actually work, whether content survives a close, and
// where the rendered content lives.

interface SqlPortsPage extends HTMLElement {
  addSqlPortDialogOpened: boolean;
  addSqlPort(): void;
  sqlPortCreated(): void;
  updateComplete: Promise<unknown>;
}

const mount = async (): Promise<SqlPortsPage> => {
  const el = document.createElement('page-sql-ports-list') as SqlPortsPage;
  document.body.appendChild(el);
  await el.updateComplete;
  await settle();
  return el;
};

describe('page-sql-ports-list add-SQL-port dialog', () => {
  let el: SqlPortsPage;
  const dialog = () => dialogIn(el, 'add-sqlport-dialog');

  const open = async () => {
    el.addSqlPort();
    await el.updateComplete;
    await settle();
  };

  beforeEach(async () => {
    el = await mount();
  });

  afterEach(() => el.remove());

  it('renders a vaadin-dialog, not a paper-dialog', () => {
    expect(dialog()).to.not.equal(null);
    expect(el.shadowRoot?.querySelector('paper-dialog')).to.equal(null);
  });

  it('is closed on first render', () => {
    expect(el.addSqlPortDialogOpened).to.equal(false);
    expect(isDialogOpen(dialog())).to.equal(false);
  });

  it('opens through reactive state, not an imperative handle', async () => {
    await open();
    expect(el.addSqlPortDialogOpened).to.equal(true);
    expect(isDialogOpen(dialog())).to.equal(true);
  });

  it('renders its content component into the dialog', async () => {
    await open();
    expect(inDialog(dialog(), 'add-sql-port')).to.not.equal(null);
  });

  it('renders a Close button into the footer slot', async () => {
    await open();
    const footer = dialog()?.querySelector(
      'vaadin-dialog-content[slot="footer"]'
    );
    expect(footer?.textContent?.trim()).to.equal('Close');
  });

  it('carries a header title', async () => {
    await open();
    expect(dialog()?.getAttribute('header-title')).to.equal('Add SQL Port');
  });

  it('closes via its Close button', async () => {
    await open();
    const close = dialog()?.querySelector(
      'vaadin-dialog-content[slot="footer"] vaadin-button'
    ) as HTMLElement | null;

    expect(close, 'Close button not rendered').to.not.equal(null);
    close?.click();
    await el.updateComplete;
    await settle();

    expect(el.addSqlPortDialogOpened).to.equal(false);
    expect(isDialogOpen(dialog())).to.equal(false);
  });

  it('closes on Escape', async () => {
    // Behaviour change from paper-dialog, which set `modal` and ignored Escape.
    // Accepted deliberately so every dialog dismisses the same way.
    await open();
    pressEscape();
    await el.updateComplete;
    await settle();

    expect(el.addSqlPortDialogOpened).to.equal(false);
    expect(isDialogOpen(dialog())).to.equal(false);
  });

  it('closes on an outside click', async () => {
    // Also new: paper-dialog's `modal` implied no-cancel-on-outside-click.
    await open();
    clickOutside(dialog());
    await el.updateComplete;
    await settle();

    expect(el.addSqlPortDialogOpened).to.equal(false);
    expect(isDialogOpen(dialog())).to.equal(false);
  });

  it('closes when a port is created', async () => {
    await open();
    el.sqlPortCreated();
    await el.updateComplete;
    await settle();

    expect(el.addSqlPortDialogOpened).to.equal(false);
  });

  it('reopens with its content intact', async () => {
    // Review R2-02: <vaadin-dialog> caches its renderer root, so content
    // persists across close/reopen rather than being rebuilt. Pinned because
    // the planning documents claimed the opposite twice, and every later
    // conversion needs to know which it is.
    await open();
    expect(inDialog(dialog(), 'add-sql-port')).to.not.equal(null);

    pressEscape();
    await settle();
    await open();

    expect(inDialog(dialog(), 'add-sql-port')).to.not.equal(null);
  });
});
