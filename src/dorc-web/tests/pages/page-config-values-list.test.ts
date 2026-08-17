import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

// --- Hoisted mock values (available before vi.mock factories run) ---
const { mockConfigGet, mockConfigPut, mockRolesGet } = vi.hoisted(() => ({
  mockConfigGet: vi.fn(),
  mockConfigPut: vi.fn(),
  mockRolesGet: vi.fn()
}));

// Vaadin web component side-effect registrations. The checkbox is NOT mocked —
// these tests assert on a real Checkbox instance's checked and disabled state.
vi.mock('@vaadin/grid/vaadin-grid', () => ({}));
vi.mock('@vaadin/grid/vaadin-grid-sort-column', () => ({}));
vi.mock('@vaadin/grid', () => ({}));
vi.mock('@vaadin/icons/vaadin-icons', () => ({}));
vi.mock('@vaadin/icon', () => ({}));
vi.mock('@vaadin/text-field', () => ({}));
vi.mock('@vaadin/button', () => ({}));
vi.mock('@polymer/paper-dialog', () => ({}));

vi.mock('../../src/components/dorc-spinner', () => ({}));
vi.mock('../../src/components/grid-button-groups/config-value-controls', () => ({}));
vi.mock('../../src/components/add-config-value', () => ({}));
vi.mock('../../src/helpers/html-meta-manager', () => ({ updateMetadata: vi.fn() }));
vi.mock('../../src/router/routes.ts', () => ({}));
vi.mock('@vaadin/router', () => ({}));

vi.mock('../../src/apis/dorc-api', () => ({
  RefDataConfigApi: class {
    refDataConfigGet = mockConfigGet;
    refDataConfigPut = mockConfigPut;
  },
  RefDataRolesApi: class {
    refDataRolesGet = mockRolesGet;
  }
}));

import { PageConfigValuesList } from '../../src/pages/page-config-values-list';
import { Checkbox } from '@vaadin/checkbox';
import type { ConfigValueApiModel } from '../../src/apis/dorc-api';

/** A subscribe() that hands the supplied payload straight to the next handler. */
const emitting = (payload: unknown) => ({
  subscribe: (handlers: any) => {
    handlers.next?.(payload);
    handlers.complete?.();
  }
});

async function flushAsync(): Promise<void> {
  await new Promise(resolve => setTimeout(resolve, 0));
}

/**
 * A config value's script visibility is the only classification on this page that is not
 * simply a property of the value — it decides whether a deployment script can read it,
 * so who may change it and when it means anything both matter.
 *
 * The classification applies to secure values only. A non-secure value is ordinary
 * configuration and is visible by definition; the server forces it true on creation, and
 * a checkbox that let an administrator appear to un-publish one would be lying.
 */
describe('PageConfigValuesList script visibility', () => {
  let el: PageConfigValuesList;

  const secure: ConfigValueApiModel = {
    Id: 1,
    Key: 'SomeAccountPassword',
    Value: 'enc:x',
    Secure: true,
    IsForProd: true,
    VisibleToScripts: false
  };

  const nonSecure: ConfigValueApiModel = {
    Id: 2,
    Key: 'ArtefactsUrl',
    Value: 'https://example.invalid',
    Secure: false,
    IsForProd: false,
    VisibleToScripts: true
  };

  /** Runs the renderer for one row and returns the checkbox it produced. */
  function renderVisibility(item: ConfigValueApiModel): Checkbox {
    const root = document.createElement('div');
    el.isVisibleToScriptsRenderer(root, {} as any, { item } as any);
    return root.firstElementChild as Checkbox;
  }

  async function mount(roles: string[]): Promise<void> {
    mockConfigGet.mockImplementation(() => emitting([secure, nonSecure]));
    mockRolesGet.mockImplementation(() => emitting(roles));
    mockConfigPut.mockImplementation(() => emitting({}));

    el = new PageConfigValuesList();
    document.body.appendChild(el);
    await el.updateComplete;
    await flushAsync();
  }

  beforeEach(() => {
    mockConfigGet.mockClear();
    mockConfigPut.mockClear();
    mockRolesGet.mockClear();
  });

  afterEach(() => {
    el?.remove();
    document.body.innerHTML = '';
  });

  it('shows the stored classification', async () => {
    await mount(['Admin']);

    expect(renderVisibility(secure).checked).toBe(false);
    expect(renderVisibility(nonSecure).checked).toBe(true);
  });

  it('lets an administrator change it on a secure value', async () => {
    await mount(['Admin']);

    expect(renderVisibility(secure).disabled).toBe(false);
  });

  it('does not offer it on a non-secure value', async () => {
    await mount(['Admin']);

    // Not a permissions question: a non-secure value is visible to scripts by
    // definition and the server forces it true, so an editable checkbox here would
    // suggest a restriction that does not exist.
    expect(renderVisibility(nonSecure).disabled).toBe(true);
  });

  it('does not offer it to a non-administrator', async () => {
    await mount([]);

    expect(renderVisibility(secure).disabled).toBe(true);
  });

  it('sends the changed classification and nothing else', async () => {
    await mount(['Admin']);

    const checkbox = renderVisibility(secure);
    checkbox.checked = true;
    checkbox.dispatchEvent(new Event('change'));
    await flushAsync();

    expect(mockConfigPut).toHaveBeenCalledTimes(1);
    const sent = mockConfigPut.mock.calls[0][0] as any;
    expect(sent.id).toBe(secure.Id);
    expect(sent.configValueApiModel.VisibleToScripts).toBe(true);
    // The server applies one change per request, so the rest of the value has to
    // arrive unchanged or a visibility toggle would be read as an edit of something
    // else — the encrypted value, most damagingly.
    expect(sent.configValueApiModel.Value).toBe(secure.Value);
    expect(sent.configValueApiModel.Secure).toBe(secure.Secure);
    expect(sent.configValueApiModel.IsForProd).toBe(secure.IsForProd);
  });
});
