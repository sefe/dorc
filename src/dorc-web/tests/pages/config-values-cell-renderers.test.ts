import { expect, fixture, html, settle } from '../_helpers';
import { render } from 'lit';
import { Subject, of } from 'rxjs';
import { vi } from 'vitest';
import {
  RefDataConfigApi,
  RefDataRolesApi,
  type ConfigValueApiModel
} from '../../src/apis/dorc-api';
import '../../src/components/add-config-value';
import '../../src/components/grid-button-groups/config-value-controls';
import '../../src/pages/page-config-values-list';

// The Secure, IsForProd, and VisibleToScripts columns are editable checkboxes
// in recycled grid cells, and both halves of that have to hold:
//
//  - `change` rather than `checked-changed`, so committing the next row's value
//    does not fire into the previous row's still-attached listener — which here
//    PUTs the config value.
//  - `live()`, so a tick the user made outside Lit is re-applied from the model
//    when the cell is recycled. Lit dirty-checks against its last committed
//    value otherwise, and the next row inherits the tick — showing a plaintext
//    value as encrypted, a non-prod value as production, or a hidden secret as
//    script-visible.
//
// The renderers are called directly: connectedCallback would drive the page's
// data provider at the API, and `isAdmin` is all these three need.

type ConfigValue = {
  Id?: number;
  Key?: string;
  Value?: string;
  Secure?: boolean;
  IsForProd?: boolean;
  VisibleToScripts?: boolean;
};

type Page = HTMLElement & {
  isAdmin: boolean;
  isSecuredRenderer(configValue: ConfigValue): unknown;
  isForProdRenderer(configValue: ConfigValue): unknown;
  isVisibleToScriptsRenderer(configValue: ConfigValue): unknown;
};

describe('page-config-values-list editable cell renderers', () => {
  let host: HTMLElement;
  let page: Page;

  beforeEach(async () => {
    host = document.createElement('div');
    document.body.appendChild(host);
    page = document.createElement('page-config-values-list') as Page;
    page.isAdmin = true;
    await settle();
  });

  afterEach(() => host.remove());

  const recycles = async (
    renderer: (item: ConfigValue) => unknown,
    field: 'Secure' | 'IsForProd' | 'VisibleToScripts',
    extra: Partial<ConfigValue> = {}
  ) => {
    render(
      renderer({ Id: 1, Key: 'first', ...extra, [field]: false }) as never,
      host
    );
    await settle();

    const checkbox = host.querySelector('vaadin-checkbox') as HTMLElement & {
      checked: boolean;
    };
    // The user ticks it. This does not go through Lit.
    checkbox.checked = true;
    await settle();

    // Recycled onto a row that does not carry the flag.
    render(
      renderer({ Id: 2, Key: 'second', ...extra, [field]: false }) as never,
      host
    );
    await settle();

    expect(host.querySelector('vaadin-checkbox'), 'element reused').to.equal(
      checkbox
    );
    return checkbox.checked;
  };

  it('Secure column shows the new row, not the old tick', async () => {
    expect(
      await recycles(item => page.isSecuredRenderer(item), 'Secure')
    ).to.equal(false);
  });

  it('IsForProd column shows the new row, not the old tick', async () => {
    expect(
      await recycles(item => page.isForProdRenderer(item), 'IsForProd')
    ).to.equal(false);
  });

  it('VisibleToScripts column shows the new row, not the old tick', async () => {
    expect(
      await recycles(
        item => page.isVisibleToScriptsRenderer(item),
        'VisibleToScripts',
        { Secure: true }
      )
    ).to.equal(false);
  });

  it('does not persist when Secure is set programmatically', async () => {
    // A recycled cell sets the property; only a gesture may reach the API.
    const updated: ConfigValue[] = [];
    (
      page as unknown as { updateConfigItem(v: ConfigValue): void }
    ).updateConfigItem = v => updated.push(v);

    render(page.isSecuredRenderer({ Id: 1, Key: 'first' }) as never, host);
    await settle();
    (
      host.querySelector('vaadin-checkbox') as HTMLElement & {
        checked: boolean;
      }
    ).checked = true;
    await settle();

    expect(updated, 'no update from a non-gesture change').to.deep.equal([]);
  });

  it('does not persist when IsForProd is set programmatically', async () => {
    // The file's header promises both halves for both columns; only Secure had
    // the gesture-only half. Flipping IsForProd to `@checked-changed` left the
    // whole suite green, and that regression would PUT a config value to
    // production because a recycled cell committed the next row's flag.
    const updated: ConfigValue[] = [];
    (
      page as unknown as { updateConfigItem(v: ConfigValue): void }
    ).updateConfigItem = v => updated.push(v);

    render(page.isForProdRenderer({ Id: 1, Key: 'first' }) as never, host);
    await settle();
    (
      host.querySelector('vaadin-checkbox') as HTMLElement & {
        checked: boolean;
      }
    ).checked = true;
    await settle();

    expect(updated, 'no update from a non-gesture change').to.deep.equal([]);
  });

  it('does not persist when VisibleToScripts is set programmatically', async () => {
    const updated: ConfigValue[] = [];
    (
      page as unknown as { updateConfigItem(v: ConfigValue): void }
    ).updateConfigItem = v => updated.push(v);

    render(
      page.isVisibleToScriptsRenderer({
        Id: 1,
        Key: 'first',
        Secure: true,
        VisibleToScripts: false
      }) as never,
      host
    );
    await settle();
    (
      host.querySelector('vaadin-checkbox') as HTMLElement & {
        checked: boolean;
      }
    ).checked = true;
    await settle();

    expect(updated, 'no update from a non-gesture change').to.deep.equal([]);
  });

  it('persists a real click on IsForProd with Value and VisibleToScripts omitted', async () => {
    const updated: ConfigValue[] = [];
    (
      page as unknown as { updateConfigItem(v: ConfigValue): void }
    ).updateConfigItem = v => updated.push(v);

    render(
      page.isForProdRenderer({
        Id: 1,
        Key: 'first',
        Value: 'enc:s3cret',
        Secure: true,
        IsForProd: false,
        VisibleToScripts: true
      }) as never,
      host
    );
    await settle();
    (
      host.querySelector('vaadin-checkbox')?.querySelector('input') as
        HTMLInputElement | undefined
    )?.click();
    await settle();

    expect(updated).to.deep.equal([
      {
        Id: 1,
        Key: 'first',
        Value: undefined,
        Secure: true,
        IsForProd: true,
        VisibleToScripts: undefined
      }
    ]);
  });

  it('persists a real click on Secure', async () => {
    const updated: ConfigValue[] = [];
    (
      page as unknown as { updateConfigItem(v: ConfigValue): void }
    ).updateConfigItem = v => updated.push(v);

    render(page.isSecuredRenderer({ Id: 1, Key: 'first' }) as never, host);
    await settle();
    (
      host.querySelector('vaadin-checkbox')?.querySelector('input') as
        HTMLInputElement | undefined
    )?.click();
    await settle();

    expect(updated.length, 'one update').to.equal(1);
    expect(updated[0].Secure, 'carries the new value').to.equal(true);
    expect(
      updated[0].VisibleToScripts,
      'newly secured value defaults to hidden from scripts'
    ).to.equal(false);
  });

  it('resets VisibleToScripts to false when converting a non-secure row to secure', async () => {
    const updated: ConfigValue[] = [];
    (
      page as unknown as { updateConfigItem(v: ConfigValue): void }
    ).updateConfigItem = v => updated.push(v);

    render(
      page.isSecuredRenderer({
        Id: 9,
        Key: 'BecomingSecret',
        Value: 'plain',
        Secure: false,
        IsForProd: true,
        VisibleToScripts: true
      }) as never,
      host
    );
    await settle();
    (
      host.querySelector('vaadin-checkbox')?.querySelector('input') as
        HTMLInputElement | undefined
    )?.click();
    await settle();

    expect(updated).to.deep.equal([
      {
        Id: 9,
        Key: 'BecomingSecret',
        Value: 'plain',
        Secure: true,
        IsForProd: true,
        VisibleToScripts: false
      }
    ]);
  });

  it('restores VisibleToScripts to true and omits ciphertext Value when converting a hidden secure row to non-secure', async () => {
    const updated: ConfigValue[] = [];
    (
      page as unknown as { updateConfigItem(v: ConfigValue): void }
    ).updateConfigItem = v => updated.push(v);

    render(
      page.isSecuredRenderer({
        Id: 10,
        Key: 'NoLongerSecret',
        Value: 'enc:s3cret',
        Secure: true,
        IsForProd: false,
        VisibleToScripts: false
      }) as never,
      host
    );
    await settle();
    (
      host.querySelector('vaadin-checkbox')?.querySelector('input') as
        HTMLInputElement | undefined
    )?.click();
    await settle();

    expect(updated).to.deep.equal([
      {
        Id: 10,
        Key: 'NoLongerSecret',
        Value: undefined,
        Secure: false,
        IsForProd: false,
        VisibleToScripts: true
      }
    ]);
  });

  it('shows the stored script visibility classification', async () => {
    render(
      page.isVisibleToScriptsRenderer({
        Id: 1,
        Secure: true,
        VisibleToScripts: false
      }) as never,
      host
    );
    await settle();

    expect(
      (
        host.querySelector('vaadin-checkbox') as HTMLElement & {
          checked: boolean;
        }
      ).checked
    ).to.equal(false);
  });

  it('only lets administrators change script visibility on secure values', async () => {
    const renderVisibility = async (isAdmin: boolean, secure: boolean) => {
      page.isAdmin = isAdmin;
      render(
        page.isVisibleToScriptsRenderer({
          Id: 1,
          Secure: secure,
          VisibleToScripts: true
        }) as never,
        host
      );
      await settle();
      return (
        host.querySelector('vaadin-checkbox') as HTMLElement & {
          disabled: boolean;
        }
      ).disabled;
    };

    expect(await renderVisibility(true, true)).to.equal(false);
    expect(await renderVisibility(true, false)).to.equal(true);
    expect(await renderVisibility(false, true)).to.equal(true);
  });

  it('persists a real script visibility change with Value omitted to avoid ciphertext re-encryption', async () => {
    const updated: ConfigValue[] = [];
    (
      page as unknown as { updateConfigItem(v: ConfigValue): void }
    ).updateConfigItem = v => updated.push(v);

    const original: ConfigValue = {
      Id: 1,
      Key: 'SomeAccountPassword',
      Value: 'enc:ciphertext',
      Secure: true,
      IsForProd: true,
      VisibleToScripts: false
    };
    render(page.isVisibleToScriptsRenderer(original) as never, host);
    await settle();
    const checkbox = host.querySelector('vaadin-checkbox') as HTMLElement;
    expect(checkbox.getAttribute('title') ?? '').to.include(
      'Reserved DOrc operating credentials'
    );
    (checkbox.querySelector('input') as HTMLInputElement | undefined)?.click();
    await settle();

    expect(updated).to.deep.equal([
      {
        ...original,
        Value: undefined,
        VisibleToScripts: true
      }
    ]);
  });

  it('disables all checkboxes for an in-flight row update and ignores concurrent toggles until refresh settles', async () => {
    const putSubject = new Subject<ConfigValueApiModel>();
    const putCalls: ConfigValueApiModel[] = [];

    const putSpy = vi
      .spyOn(RefDataConfigApi.prototype, 'refDataConfigPut')
      .mockImplementation(({ configValueApiModel }) => {
        putCalls.push({ ...configValueApiModel });
        return putSubject.asObservable() as never;
      });
    const getSpy = vi
      .spyOn(RefDataConfigApi.prototype, 'refDataConfigGet')
      .mockImplementation(
        () =>
          of([
            {
              Id: 1,
              Key: 'RowOne',
              Value: 'enc:1',
              Secure: true,
              IsForProd: false,
              VisibleToScripts: true
            },
            {
              Id: 2,
              Key: 'RowTwo',
              Value: 'enc:2',
              Secure: true,
              IsForProd: false,
              VisibleToScripts: false
            }
          ]) as never
      );

    const hostOther = document.createElement('div');
    document.body.appendChild(hostOther);

    try {
      const row1: ConfigValue = {
        Id: 1,
        Key: 'RowOne',
        Value: 'enc:1',
        Secure: true,
        IsForProd: false,
        VisibleToScripts: false
      };
      const row2: ConfigValue = {
        Id: 2,
        Key: 'RowTwo',
        Value: 'enc:2',
        Secure: true,
        IsForProd: false,
        VisibleToScripts: false
      };

      render(page.isVisibleToScriptsRenderer(row1) as never, host);
      render(page.isForProdRenderer(row2) as never, hostOther);
      await settle();

      (
        host.querySelector('vaadin-checkbox')?.querySelector('input') as
          HTMLInputElement | undefined
      )?.click();
      await settle();

      expect(putCalls.length, 'initial PUT dispatched').to.equal(1);

      // Re-render row 1 and row 2 cells with updated reactive state.
      render(page.isSecuredRenderer(row1) as never, host);
      expect(
        (host.querySelector('vaadin-checkbox') as { disabled: boolean })
          .disabled,
        'row 1 Secure disabled while in flight'
      ).to.equal(true);

      render(page.isForProdRenderer(row1) as never, host);
      expect(
        (host.querySelector('vaadin-checkbox') as { disabled: boolean })
          .disabled,
        'row 1 IsForProd disabled while in flight'
      ).to.equal(true);

      render(page.isVisibleToScriptsRenderer(row1) as never, host);
      expect(
        (host.querySelector('vaadin-checkbox') as { disabled: boolean })
          .disabled,
        'row 1 VisibleToScripts disabled while in flight'
      ).to.equal(true);

      render(page.isForProdRenderer(row2) as never, hostOther);
      expect(
        (hostOther.querySelector('vaadin-checkbox') as { disabled: boolean })
          .disabled,
        'row 2 remains enabled while row 1 is in flight'
      ).to.equal(false);

      // Even if a second updateConfigItem call is attempted for row 1 while in flight, it is ignored.
      (
        page as unknown as { updateConfigItem(v: ConfigValue): void }
      ).updateConfigItem({ ...row1, IsForProd: true });
      expect(putCalls.length, 'duplicate row 1 PUT suppressed').to.equal(1);

      // Complete the PUT; refresh runs synchronously via of([...]) and clears the lock.
      putSubject.next({ ...row1, VisibleToScripts: true });
      putSubject.complete();
      await settle();

      render(page.isVisibleToScriptsRenderer(row1) as never, host);
      expect(
        (host.querySelector('vaadin-checkbox') as { disabled: boolean })
          .disabled,
        'row 1 re-enabled after refresh settles'
      ).to.equal(false);
    } finally {
      hostOther.remove();
      putSpy.mockRestore();
      getSpy.mockRestore();
    }
  });

  it('renders header tooltip on wide screens and inline labeled classifications on narrow screens', async () => {
    const getSpy = vi
      .spyOn(RefDataConfigApi.prototype, 'refDataConfigGet')
      .mockReturnValue(
        of([
          {
            Id: 5,
            Key: 'NarrowSecret',
            Value: 'enc:narrow',
            Secure: true,
            IsForProd: true,
            VisibleToScripts: false
          }
        ]) as never
      );
    const rolesSpy = vi
      .spyOn(RefDataRolesApi.prototype, 'refDataRolesGet')
      .mockReturnValue(of(['Admin']) as never);

    try {
      const el = await fixture<
        HTMLElement & { _narrowScreen: boolean; updateComplete: Promise<void> }
      >(html`<page-config-values-list></page-config-values-list>`);
      await settle();

      el._narrowScreen = false;
      await el.updateComplete;
      await settle();

      const sorter = el.shadowRoot
        ?.querySelector('vaadin-grid')
        ?.querySelector('vaadin-grid-sorter[path="VisibleToScripts"]');
      expect(sorter, 'VisibleToScripts sorter rendered').to.exist;
      expect(sorter?.getAttribute('title') ?? '').to.include(
        'Reserved DOrc operating credentials'
      );

      el._narrowScreen = true;
      await el.updateComplete;
      await settle();

      const narrowGroup = el.shadowRoot
        ?.querySelector('vaadin-grid')
        ?.querySelector('.narrow-classifications');
      expect(narrowGroup, 'inline narrow classification group rendered').to
        .exist;
      const labels = Array.from(
        narrowGroup?.querySelectorAll('vaadin-checkbox') ?? []
      ).map(cb => (cb as HTMLElement & { label: string }).label);
      expect(labels).to.deep.equal([
        'Is Secure',
        'Is For Prod',
        'Visible To Scripts'
      ]);
    } finally {
      getSpy.mockRestore();
      rolesSpy.mockRestore();
    }
  });

  it('omits VisibleToScripts when saving a value edit in config-value-controls', async () => {
    const putPayloads: ConfigValueApiModel[] = [];
    const putSpy = vi
      .spyOn(RefDataConfigApi.prototype, 'refDataConfigPut')
      .mockImplementation(({ configValueApiModel }) => {
        putPayloads.push({ ...configValueApiModel });
        return of(configValueApiModel) as never;
      });

    try {
      const row: ConfigValueApiModel = {
        Id: 12,
        Key: 'RotatingSecret',
        Value: 'enc:old',
        Secure: true,
        IsForProd: true,
        VisibleToScripts: true
      };
      const controls = await fixture<
        HTMLElement & {
          value: ConfigValueApiModel;
          _editClick(): void;
          _saveClick(): void;
        }
      >(html`<config-value-controls .value="${row}"></config-value-controls>`);
      await settle();

      controls._editClick();
      const passwordField = controls.shadowRoot?.querySelector(
        '#propValue12'
      ) as HTMLElement & { value: string };
      passwordField.value = 'new-plaintext-secret';
      passwordField.dispatchEvent(new Event('input', { bubbles: true }));
      controls._saveClick();
      await settle();

      expect(row.Value, 'underlying row ciphertext is not mutated').to.equal(
        'enc:old'
      );
      expect(putPayloads).to.deep.equal([
        {
          Id: 12,
          Key: 'RotatingSecret',
          Value: 'new-plaintext-secret',
          Secure: true,
          IsForProd: true,
          VisibleToScripts: undefined
        }
      ]);
    } finally {
      putSpy.mockRestore();
    }
  });

  it('does not submit refreshed ciphertext when editing a secure row, toggling Visible To Scripts, and clicking Save without typing', async () => {
    let storedVisibleToScripts = false;
    const putPayloads: ConfigValueApiModel[] = [];

    const getSpy = vi
      .spyOn(RefDataConfigApi.prototype, 'refDataConfigGet')
      .mockImplementation(
        () =>
          of([
            {
              Id: 21,
              Key: 'ServiceAccountSecret',
              Value: 'enc:ciphertext-from-api',
              Secure: true,
              IsForProd: true,
              VisibleToScripts: storedVisibleToScripts
            }
          ]) as never
      );
    const rolesSpy = vi
      .spyOn(RefDataRolesApi.prototype, 'refDataRolesGet')
      .mockReturnValue(of(['Admin']) as never);
    const putSpy = vi
      .spyOn(RefDataConfigApi.prototype, 'refDataConfigPut')
      .mockImplementation(({ configValueApiModel }) => {
        if (configValueApiModel) {
          putPayloads.push({ ...configValueApiModel });
          if (configValueApiModel.VisibleToScripts !== undefined) {
            storedVisibleToScripts = Boolean(
              configValueApiModel.VisibleToScripts
            );
          }
        }
        return of(configValueApiModel) as never;
      });

    try {
      const el = await fixture<
        HTMLElement & { _narrowScreen: boolean; updateComplete: Promise<void> }
      >(html`<page-config-values-list></page-config-values-list>`);
      await settle();

      el._narrowScreen = false;
      await el.updateComplete;
      await settle();

      const grid = () => el.shadowRoot?.querySelector('vaadin-grid');
      const controls = () =>
        grid()?.querySelector('config-value-controls') as HTMLElement & {
          value: ConfigValueApiModel;
          saveHidden: boolean;
          editHidden: boolean;
        };
      const passwordField = () =>
        controls().shadowRoot?.querySelector('#propValue21') as HTMLElement & {
          value: string;
          readonly: boolean;
        };
      const checkboxByLabel = (label: string) =>
        grid()?.querySelector(
          `vaadin-checkbox[aria-label="${label}"]`
        ) as HTMLElement & { checked: boolean; disabled: boolean };

      const editButton = controls().shadowRoot?.querySelector(
        '#edit'
      ) as HTMLElement;
      editButton.click();
      await el.updateComplete;
      await settle();

      expect(passwordField().value, 'cleared on Edit').to.equal('');
      expect(passwordField().readonly, 'editable on Edit').to.equal(false);
      expect(
        checkboxByLabel('Is Secure').disabled,
        'Is Secure disabled while value edit is active'
      ).to.equal(true);

      (
        checkboxByLabel('Visible To Scripts').querySelector(
          'input'
        ) as HTMLInputElement
      ).click();
      await el.updateComplete;
      await settle();

      expect(putPayloads.length, 'only the toggle PUT ran').to.equal(1);
      expect(putPayloads[0]).to.deep.equal({
        Id: 21,
        Key: 'ServiceAccountSecret',
        Value: undefined,
        Secure: true,
        IsForProd: true,
        VisibleToScripts: true
      });
      expect(
        controls().value.Value,
        'refreshed row carries ciphertext from API'
      ).to.equal('enc:ciphertext-from-api');
      expect(
        passwordField().value,
        'active draft stays empty instead of adopting ciphertext'
      ).to.equal('');
      expect(
        controls().saveHidden,
        'row remains in edit mode after classification refresh'
      ).to.equal(false);

      const saveButton = controls().shadowRoot?.querySelector(
        'vaadin-button[aria-label="Save"]'
      ) as HTMLElement;
      saveButton.click();
      await el.updateComplete;
      await settle();

      expect(
        putPayloads.length,
        'clicking Save without typing a secret does not submit ciphertext'
      ).to.equal(1);
      expect(controls().saveHidden, 'exited edit mode cleanly').to.equal(true);
      expect(
        passwordField().value,
        'restored masked placeholder after exit'
      ).to.equal('Ex@mplePassw0rd');
      expect(
        checkboxByLabel('Is Secure').disabled,
        'Is Secure re-enabled after exiting edit mode'
      ).to.equal(false);
    } finally {
      getSpy.mockRestore();
      rolesSpy.mockRestore();
      putSpy.mockRestore();
    }
  });

  it('preserves a typed secret draft across Visible To Scripts refresh and submits only the typed plaintext on Save', async () => {
    let storedVisibleToScripts = false;
    let storedCiphertext = 'enc:initial-ciphertext';
    const putPayloads: ConfigValueApiModel[] = [];

    const getSpy = vi
      .spyOn(RefDataConfigApi.prototype, 'refDataConfigGet')
      .mockImplementation(
        () =>
          of([
            {
              Id: 22,
              Key: 'DatabasePassword',
              Value: storedCiphertext,
              Secure: true,
              IsForProd: false,
              VisibleToScripts: storedVisibleToScripts
            }
          ]) as never
      );
    const rolesSpy = vi
      .spyOn(RefDataRolesApi.prototype, 'refDataRolesGet')
      .mockReturnValue(of(['Admin']) as never);
    const putSpy = vi
      .spyOn(RefDataConfigApi.prototype, 'refDataConfigPut')
      .mockImplementation(({ configValueApiModel }) => {
        if (configValueApiModel) {
          putPayloads.push({ ...configValueApiModel });
          if (configValueApiModel.VisibleToScripts !== undefined) {
            storedVisibleToScripts = Boolean(
              configValueApiModel.VisibleToScripts
            );
          }
          if (configValueApiModel.Value !== undefined) {
            storedCiphertext = `enc:${configValueApiModel.Value}`;
          }
        }
        return of(configValueApiModel) as never;
      });

    try {
      const el = await fixture<
        HTMLElement & { _narrowScreen: boolean; updateComplete: Promise<void> }
      >(html`<page-config-values-list></page-config-values-list>`);
      await settle();

      el._narrowScreen = false;
      await el.updateComplete;
      await settle();

      const grid = () => el.shadowRoot?.querySelector('vaadin-grid');
      const controls = () =>
        grid()?.querySelector('config-value-controls') as HTMLElement & {
          value: ConfigValueApiModel;
          saveHidden: boolean;
        };
      const passwordField = () =>
        controls().shadowRoot?.querySelector('#propValue22') as HTMLElement & {
          value: string;
        };
      const checkboxByLabel = (label: string) =>
        grid()?.querySelector(
          `vaadin-checkbox[aria-label="${label}"]`
        ) as HTMLElement & { checked: boolean; disabled: boolean };

      const editButton = controls().shadowRoot?.querySelector(
        '#edit'
      ) as HTMLElement;
      editButton.click();
      await el.updateComplete;
      await settle();

      const field = passwordField();
      field.value = 'fresh-rotated-secret';
      field.dispatchEvent(new Event('input', { bubbles: true }));
      await el.updateComplete;
      await settle();

      (
        checkboxByLabel('Visible To Scripts').querySelector(
          'input'
        ) as HTMLInputElement
      ).click();
      await el.updateComplete;
      await settle();

      expect(
        controls().value.Value,
        'refreshed row still holds old ciphertext until Save'
      ).to.equal('enc:initial-ciphertext');
      expect(
        passwordField().value,
        'typed plaintext draft survives refresh without ciphertext clobber'
      ).to.equal('fresh-rotated-secret');

      const saveButton = controls().shadowRoot?.querySelector(
        'vaadin-button[aria-label="Save"]'
      ) as HTMLElement;
      saveButton.click();
      await el.updateComplete;
      await settle();

      expect(putPayloads).to.deep.equal([
        {
          Id: 22,
          Key: 'DatabasePassword',
          Value: undefined,
          Secure: true,
          IsForProd: false,
          VisibleToScripts: true
        },
        {
          Id: 22,
          Key: 'DatabasePassword',
          Value: 'fresh-rotated-secret',
          Secure: true,
          IsForProd: false,
          VisibleToScripts: undefined
        }
      ]);
      expect(controls().saveHidden, 'exited edit mode after Save').to.equal(
        true
      );
      expect(
        controls().value.Value,
        'grid refreshed with single-encrypted value after Save'
      ).to.equal('enc:fresh-rotated-secret');
      expect(
        controls().value.VisibleToScripts,
        'toggled VisibleToScripts preserved after Save'
      ).to.equal(true);
    } finally {
      getSpy.mockRestore();
      rolesSpy.mockRestore();
      putSpy.mockRestore();
    }
  });

  it('falls back gracefully on an invalid search regex like "[" so toggling Visible To Scripts refreshes and unlocks cleanly', async () => {
    let storedVisibleToScripts = false;
    const putSubject = new Subject<ConfigValueApiModel>();
    const putPayloads: ConfigValueApiModel[] = [];

    const currentRows = (): ConfigValueApiModel[] => [
      {
        Id: 31,
        Key: 'Bracket[0]Secret',
        Value: 'enc:bracket',
        Secure: true,
        IsForProd: false,
        VisibleToScripts: storedVisibleToScripts
      },
      {
        Id: 32,
        Key: 'PlainSecret',
        Value: 'enc:plain',
        Secure: true,
        IsForProd: false,
        VisibleToScripts: false
      }
    ];

    const getSpy = vi
      .spyOn(RefDataConfigApi.prototype, 'refDataConfigGet')
      .mockImplementation(() => of(currentRows()) as never);
    const rolesSpy = vi
      .spyOn(RefDataRolesApi.prototype, 'refDataRolesGet')
      .mockReturnValue(of(['Admin']) as never);
    const putSpy = vi
      .spyOn(RefDataConfigApi.prototype, 'refDataConfigPut')
      .mockImplementation(({ configValueApiModel }) => {
        if (configValueApiModel) {
          putPayloads.push({ ...configValueApiModel });
          if (configValueApiModel.VisibleToScripts !== undefined) {
            storedVisibleToScripts = Boolean(
              configValueApiModel.VisibleToScripts
            );
          }
        }
        return putSubject.asObservable() as never;
      });

    try {
      const el = await fixture<
        HTMLElement & {
          _narrowScreen: boolean;
          filteredConfigValues: ConfigValueApiModel[];
          setConfigValues(values: ConfigValueApiModel[]): void;
          updateComplete: Promise<void>;
        }
      >(html`<page-config-values-list></page-config-values-list>`);
      await settle();

      el._narrowScreen = false;
      await el.updateComplete;
      await settle();

      const searchField = el.shadowRoot?.querySelector(
        'vaadin-text-field'
      ) as HTMLElement & { value: string };
      searchField.value = '[';
      await el.updateComplete;
      await settle();

      expect(
        el.filteredConfigValues.map(item => item.Id),
        'invalid regex falls back to case-insensitive literal substring filter'
      ).to.deep.equal([31]);
      expect(
        () => el.setConfigValues(currentRows()),
        'setConfigValues does not throw while searchQuery holds an invalid regex'
      ).to.not.throw();

      const visibleCheckbox = () =>
        el.shadowRoot
          ?.querySelector('vaadin-grid')
          ?.querySelector(
            'vaadin-checkbox[aria-label="Visible To Scripts"]'
          ) as HTMLElement & { checked: boolean; disabled: boolean };

      (visibleCheckbox().querySelector('input') as HTMLInputElement).click();
      await el.updateComplete;
      await settle();

      expect(putPayloads).to.deep.equal([
        {
          Id: 31,
          Key: 'Bracket[0]Secret',
          Value: undefined,
          Secure: true,
          IsForProd: false,
          VisibleToScripts: true
        }
      ]);
      expect(
        visibleCheckbox().disabled,
        'row locks while PUT is in flight'
      ).to.equal(true);

      putSubject.next(currentRows()[0]);
      putSubject.complete();
      await el.updateComplete;
      await settle();

      expect(
        visibleCheckbox().checked,
        'refreshed Visible To Scripts state applied'
      ).to.equal(true);
      expect(
        visibleCheckbox().disabled,
        'row unlocks cleanly after refresh settles with invalid regex in searchQuery'
      ).to.equal(false);
    } finally {
      getSpy.mockRestore();
      rolesSpy.mockRestore();
      putSpy.mockRestore();
    }
  });
});

describe('add-config-value script visibility control', () => {
  type CheckboxEl = HTMLElement & { checked: boolean; disabled: boolean };

  const checkboxIn = (el: HTMLElement, id: string) =>
    el.shadowRoot?.querySelector(`#${id}`) as CheckboxEl;

  const clickCheckbox = (checkbox: CheckboxEl) => {
    (checkbox.querySelector('input') as HTMLInputElement).click();
  };

  it('defaults Visible To Scripts to unchecked and disabled until Is Secure is checked, and posts hidden by default', async () => {
    const posted: ConfigValueApiModel[] = [];
    const postSpy = vi
      .spyOn(RefDataConfigApi.prototype, 'refDataConfigPost')
      .mockImplementation(({ configValueApiModel }) => {
        posted.push({ ...configValueApiModel });
        return of({ Id: 42, ...configValueApiModel }) as never;
      });

    try {
      const el = await fixture<HTMLElement>(
        html`<add-config-value></add-config-value>`
      );
      await settle();

      const secureBox = checkboxIn(el, 'secure');
      const visibleBox = checkboxIn(el, 'visible-to-scripts');

      expect(visibleBox.checked, 'unchecked initially').to.equal(false);
      expect(visibleBox.disabled, 'disabled while non-secure').to.equal(true);

      clickCheckbox(secureBox);
      await settle();

      expect(visibleBox.disabled, 'enabled once secure').to.equal(false);
      expect(
        visibleBox.checked,
        'stays unchecked by default when secured'
      ).to.equal(false);

      const keyField = el.shadowRoot?.querySelector('#key') as HTMLElement & {
        value: string;
      };
      const valueField = el.shadowRoot?.querySelector(
        '#value'
      ) as HTMLElement & { value: string };
      keyField.value = 'NewSecretKey';
      keyField.dispatchEvent(new Event('input', { bubbles: true }));
      valueField.value = 's3cret';
      valueField.dispatchEvent(new Event('input', { bubbles: true }));
      await settle();

      const buttons = el.shadowRoot?.querySelectorAll('vaadin-button') ?? [];
      (buttons[1] as HTMLElement).click();
      await settle();

      expect(posted.length).to.equal(1);
      expect(posted[0]).to.deep.equal({
        Key: 'NewSecretKey',
        Value: 's3cret',
        Secure: true,
        IsForProd: false,
        VisibleToScripts: false
      });
    } finally {
      postSpy.mockRestore();
    }
  });

  it('resets Visible To Scripts when Is Secure is unchecked or Clear is clicked', async () => {
    const el = await fixture<HTMLElement>(
      html`<add-config-value></add-config-value>`
    );
    await settle();

    const secureBox = checkboxIn(el, 'secure');
    const prodBox = checkboxIn(el, 'for-prod');
    const visibleBox = checkboxIn(el, 'visible-to-scripts');

    // Enable Secure and opt in to script visibility.
    clickCheckbox(secureBox);
    await settle();
    clickCheckbox(visibleBox);
    await settle();
    expect(visibleBox.checked).to.equal(true);

    // Unchecking and re-checking Secure must not retain the prior opt-in.
    clickCheckbox(secureBox);
    await settle();
    expect(visibleBox.disabled).to.equal(true);
    expect(visibleBox.checked).to.equal(false);

    clickCheckbox(secureBox);
    await settle();
    expect(visibleBox.disabled).to.equal(false);
    expect(visibleBox.checked, 're-securing starts hidden again').to.equal(
      false
    );

    // Clear resets all checkboxes and disables Visible To Scripts.
    clickCheckbox(prodBox);
    clickCheckbox(visibleBox);
    await settle();
    expect(prodBox.checked).to.equal(true);
    expect(visibleBox.checked).to.equal(true);

    const clearButton = el.shadowRoot?.querySelector(
      'vaadin-button'
    ) as HTMLElement;
    clearButton.click();
    await settle();

    expect(secureBox.checked).to.equal(false);
    expect(prodBox.checked).to.equal(false);
    expect(visibleBox.checked).to.equal(false);
    expect(visibleBox.disabled).to.equal(true);
  });
});
