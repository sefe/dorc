import { of, Subject, throwError } from 'rxjs';
import { vi } from 'vitest';
import { expect, fixture, html } from '../_helpers';
import type { DatabaseAccessState } from '../../src/apis/dorc-api/models';

const mocks = vi.hoisted(() => ({
  status: vi.fn(),
  get: vi.fn(),
  audit: vi.fn(),
  save: vi.fn(),
  preview: vi.fn(),
  apply: vi.fn(),
  directory: vi.fn(),
  resolve: vi.fn(),
  importPreview: vi.fn(),
  import: vi.fn(),
  confirm: vi.fn()
}));

vi.mock('../../src/apis/dorc-api/apis/DatabaseAccessApi', () => ({
  DatabaseAccessApi: class {
    databaseAccessStatus = mocks.status;
    databaseAccessGet = mocks.get;
    databaseAccessAudit = mocks.audit;
    databaseAccessSave = mocks.save;
    databaseAccessPreview = mocks.preview;
    databaseAccessApply = mocks.apply;
    databaseAccessDirectory = mocks.directory;
    databaseAccessResolve = mocks.resolve;
    databaseAccessImportPreview = mocks.importPreview;
    databaseAccessExecuteImport = mocks.import;
  }
}));
vi.mock('../../src/components/confirm-prompt', () => ({
  confirmPrompt: mocks.confirm
}));

import {
  DatabaseAccessPanel,
  parseAccessMappings
} from '../../src/components/database-access-panel';

const empty: DatabaseAccessState = {
  DatabaseId: 1,
  Revision: 0,
  Provider: 'sql-server',
  Principals: [],
  Roles: [],
  Memberships: []
};

function click(panel: DatabaseAccessPanel, text: string) {
  const button = [...panel.shadowRoot!.querySelectorAll('vaadin-button')].find(
    b => b.textContent?.trim() === text
  );
  if (!button) throw new Error(`Missing button: ${text}`);
  (button as HTMLElement).click();
}

async function load() {
  const panel = await fixture<DatabaseAccessPanel>(
    html`<database-access-panel
      .envId=${10}
      .readonly=${false}
      .databases=${[{ Id: 1, Name: 'App', ServerName: 'Server' }]}
    ></database-access-panel>`
  );
  const select = panel.shadowRoot!.querySelector('select')!;
  select.value = '1';
  select.dispatchEvent(new Event('change'));
  await panel.updateComplete;
  click(panel, 'Load database access');
  await vi.waitFor(() => expect(mocks.status.mock.calls.length).to.equal(1));
  await panel.updateComplete;
  return panel;
}

describe('Database access side-by-side panel', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mocks.status.mockReturnValue(
      of({
        Enabled: true,
        ExecutionEnabled: true,
        Providers: ['sql-server'],
        Limitations: []
      })
    );
    mocks.get.mockReturnValue(of(structuredClone(empty)));
    mocks.audit.mockReturnValue(of([]));
    mocks.preview.mockReturnValue(
      of({
        Token: 'preview-1',
        Revision: 0,
        Observed: { Principals: [], Roles: [], Memberships: [], Logins: [] },
        Errors: [],
        Operations: [
          { Action: 'CreateUser', Principal: 'app', Role: '', Login: 'login' }
        ]
      })
    );
    mocks.apply.mockReturnValue(
      of({
        Token: 'after',
        Revision: 0,
        Observed: { Principals: [], Roles: [], Memberships: [], Logins: [] },
        Errors: [],
        Operations: []
      })
    );
    mocks.confirm.mockResolvedValue(true);
  });

  it('does not query the new stack until explicitly loaded', async () => {
    await fixture(
      html`<database-access-panel .envId=${10}></database-access-panel>`
    );
    expect(mocks.status.mock.calls.length).to.equal(0);
    expect(mocks.get.mock.calls.length).to.equal(0);
  });

  it('leaves legacy behaviour intact when disabled', async () => {
    mocks.status.mockReturnValue(
      of({
        Enabled: false,
        ExecutionEnabled: false,
        Providers: [],
        Limitations: []
      })
    );
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain(
        'Not enabled for this database'
      )
    );
    expect(mocks.get.mock.calls.length).to.equal(0);
    expect(panel.shadowRoot!.textContent).to.contain(
      'legacy Users and Permissions'
    );
  });

  it('renders empty state and requires an explicit preview before apply', async () => {
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain(
        'No managed principals yet'
      )
    );
    expect(mocks.apply.mock.calls.length).to.equal(0);
    click(panel, 'Discover and preview target changes');
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain('CreateUser')
    );
    click(panel, 'Apply confirmed preview');
    await vi.waitFor(() => expect(mocks.apply.mock.calls.length).to.equal(1));
    expect(mocks.apply.mock.calls[0][0]).to.deep.equal({
      envId: 10,
      databaseId: 1,
      databaseAccessConfirmation: { Token: 'preview-1' }
    });
  });

  it('does not apply a confirmation after the environment changes', async () => {
    let confirm!: (value: boolean) => void;
    mocks.confirm.mockReturnValue(
      new Promise<boolean>(resolve => {
        confirm = resolve;
      })
    );
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain(
        'No managed principals yet'
      )
    );
    click(panel, 'Discover and preview target changes');
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain('CreateUser')
    );
    click(panel, 'Apply confirmed preview');
    await vi.waitFor(() => expect(mocks.confirm.mock.calls.length).to.equal(1));
    panel.envId = 20;
    await panel.updateComplete;
    confirm(true);
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(mocks.apply.mock.calls.length).to.equal(0);
  });

  it('keeps reconciliation failures visible instead of clearing them during refresh', async () => {
    mocks.apply.mockReturnValue(
      throwError(() => new Error('Target transaction failed'))
    );
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain(
        'No managed principals yet'
      )
    );
    click(panel, 'Discover and preview target changes');
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain('CreateUser')
    );
    click(panel, 'Apply confirmed preview');
    await vi.waitFor(() =>
      expect(
        panel.shadowRoot!.querySelector('[role="alert"]')?.textContent
      ).to.contain('Target transaction failed')
    );
    expect(mocks.audit.mock.calls.length).to.equal(1);
    expect(panel.shadowRoot!.textContent).not.to.contain(
      'reconciliation completed'
    );
  });

  it('loads AD display data live and explicitly marks unresolved identities', async () => {
    mocks.get.mockReturnValue(
      of({
        ...empty,
        Principals: [
          {
            Name: 'directory_alias',
            Kind: 'DirectoryUser',
            DirectoryId: 'stable-sid',
            Present: true
          }
        ]
      })
    );
    mocks.resolve.mockReturnValue(
      throwError(() => new Error('Directory unavailable'))
    );
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain(
        'Unresolved: Directory unavailable'
      )
    );
    expect(mocks.resolve.mock.calls[0][0]).to.deep.equal({
      envId: 10,
      databaseId: 1,
      id: 'stable-sid'
    });
  });

  it('keeps target execution disabled independently of desired-state management', async () => {
    mocks.status.mockReturnValue(
      of({
        Enabled: true,
        ExecutionEnabled: false,
        Providers: ['sql-server'],
        Limitations: []
      })
    );
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain(
        'No managed principals yet'
      )
    );
    click(panel, 'Discover and preview target changes');
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain('CreateUser')
    );
    const apply = [...panel.shadowRoot!.querySelectorAll('vaadin-button')].find(
      b => b.textContent?.trim() === 'Apply confirmed preview'
    )!;
    expect(apply.hasAttribute('disabled')).to.equal(true);
    expect(mocks.apply.mock.calls.length).to.equal(0);
  });

  it('surfaces dependency errors rather than showing an empty success', async () => {
    mocks.get.mockReturnValue(
      throwError(() => new Error('Provider unavailable'))
    );
    const panel = await load();
    await vi.waitFor(() =>
      expect(
        panel.shadowRoot!.querySelector('[role="alert"]')?.textContent
      ).to.contain('Provider unavailable')
    );
    expect(panel.shadowRoot!.textContent).not.to.contain(
      'No managed principals yet'
    );
  });

  it('ignores stale load responses after switching environments', async () => {
    const pending = new Subject<DatabaseAccessState>();
    mocks.get.mockReturnValue(pending);
    const panel = await load();
    await vi.waitFor(() => expect(mocks.get.mock.calls.length).to.equal(1));
    panel.envId = 20;
    await panel.updateComplete;
    pending.next(empty);
    pending.complete();
    await new Promise(resolve => setTimeout(resolve, 0));
    await panel.updateComplete;
    expect(panel.shadowRoot!.textContent).not.to.contain(
      'No managed principals yet'
    );
  });

  it('validates explicit role mapping objects', () => {
    expect(parseAccessMappings('{"LegacyRead":"db_datareader"}')).to.deep.equal(
      { LegacyRead: 'db_datareader' }
    );
    expect(() => parseAccessMappings('[]')).to.throw();
    expect(() => parseAccessMappings('{"Read":42}')).to.throw();
    expect(() => parseAccessMappings('{"Read":""}')).to.throw();
  });

  it('saves a native principal as desired state without mutating the target', async () => {
    mocks.save.mockReturnValue(
      of({
        ...empty,
        Revision: 1,
        Principals: [
          {
            Name: 'app',
            Kind: 'Native',
            LoginName: 'existing_login',
            Present: true
          }
        ]
      })
    );
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain(
        'No managed principals yet'
      )
    );
    const labels = [...panel.shadowRoot!.querySelectorAll('label')];
    const name = labels
      .find(l => l.textContent?.includes('Database user name'))!
      .querySelector('input')!;
    const login = labels
      .find(l => l.textContent?.includes('Existing SQL server login'))!
      .querySelector('input')!;
    name.value = 'app';
    name.dispatchEvent(new Event('input'));
    login.value = 'existing_login';
    login.dispatchEvent(new Event('input'));
    click(panel, 'Save principal');
    await vi.waitFor(() => expect(mocks.save.mock.calls.length).to.equal(1));
    expect(
      mocks.save.mock.calls[0][0].databaseAccessState.Principals[0]
    ).to.deep.equal({
      Name: 'app',
      Kind: 'Native',
      LoginName: 'existing_login',
      DirectoryId: null,
      Present: true
    });
    expect(mocks.apply.mock.calls.length).to.equal(0);
  });

  it('imports only a confirmed dry run and never applies target changes', async () => {
    const imported = {
      Token: 'import-token',
      Proposed: empty,
      Errors: [],
      SourceAssignments: 1
    };
    mocks.importPreview.mockReturnValue(of(imported));
    mocks.import.mockReturnValue(of(imported));
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain(
        'No managed principals yet'
      )
    );
    click(panel, 'Preview legacy import');
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent?.replace(/\s+/g, ' ')).to.contain(
        '1 legacy assignments'
      )
    );
    click(panel, 'Import confirmed preview');
    await vi.waitFor(() => expect(mocks.import.mock.calls.length).to.equal(1));
    expect(mocks.import.mock.calls[0][0]).to.deep.equal({
      envId: 10,
      databaseId: 1,
      databaseAccessImportRequest: {
        RoleMappings: {},
        DirectoryMappings: {},
        Token: 'import-token'
      }
    });
    expect(mocks.apply.mock.calls.length).to.equal(0);
  });
});
