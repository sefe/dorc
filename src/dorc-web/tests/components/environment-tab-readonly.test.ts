import { expect } from '../_helpers';
import '../../src/components/attached-servers';
import '../../src/components/application-daemons';

// These grids gate destructive controls on a flag that arrives *after* the rows
// are painted: `readonly` / `userEditable` derive from the environment call,
// while the rows come from their own request. Each host used to force the
// repaint imperatively; the migration left the `columnBodyRenderer` dependency
// array as the only trigger, and each of these is the sole dependency-bearing
// column in its grid, so the array is the whole mechanism.
//
// Lose it and the controls stay live for the rest of the session — this is the
// fail-open direction. `server-controls` gates Edit, Application Tags, Manage
// Daemons, Detach *and Delete server* on `readonly`; `daemon-controls` gates
// Start, Stop and Restart. A user with read-only access to an environment would
// be shown a working Delete button.
//
// The same fix on `map-daemons` and `attached-env-tenants` is already guarded;
// these are the siblings that were left without a test.

const settle = () => new Promise(r => setTimeout(r, 200));

const controlsIn = (el: HTMLElement, tag: string) =>
  Array.from(
    el.shadowRoot?.querySelector('vaadin-grid')?.querySelectorAll(tag) ?? []
  ) as (HTMLElement & { readonly?: boolean; userEditable?: boolean })[];

describe('environment tab grids respect a late read-only flag', () => {
  it('attached-servers disables its row controls once readonly lands', async () => {
    const el = document.createElement('attached-servers') as HTMLElement & {
      readonly: boolean;
      servers: unknown[];
      updateComplete: Promise<unknown>;
    };
    // The real sequence: rows first, the environment's editability later.
    el.readonly = false;
    document.body.appendChild(el);
    await el.updateComplete;

    el.servers = [{ ServerId: 1, Name: 'SRV-A' }];
    await el.updateComplete;
    await settle();

    const controls = controlsIn(el, 'server-controls');
    expect(controls.length, 'row controls rendered').to.be.greaterThan(0);
    expect(
      controls.every(c => c.readonly === false),
      'editable while the flag says so'
    ).to.equal(true);

    el.readonly = true;
    await el.updateComplete;
    await settle();

    expect(
      controlsIn(el, 'server-controls').every(c => c.readonly === true),
      'read-only once the environment call lands'
    ).to.equal(true);

    el.remove();
  });

  it('application-daemons disables its row controls once the flag lands', async () => {
    const el = document.createElement('application-daemons') as HTMLElement & {
      userEditable: boolean;
      daemonsAndStatuses: unknown[];
      updateComplete: Promise<unknown>;
    };
    el.userEditable = true;
    document.body.appendChild(el);
    await el.updateComplete;

    el.daemonsAndStatuses = [
      { Daemon: { Id: 1, Name: 'Scheduler' }, Status: 'Running' }
    ];
    await el.updateComplete;
    await settle();

    const controls = controlsIn(el, 'daemon-controls');
    expect(controls.length, 'row controls rendered').to.be.greaterThan(0);

    el.userEditable = false;
    await el.updateComplete;
    await settle();

    expect(
      controlsIn(el, 'daemon-controls').every(c => c.userEditable === false),
      'not editable once the flag lands'
    ).to.equal(true);

    el.remove();
  });
});
