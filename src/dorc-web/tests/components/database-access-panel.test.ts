import { of, Subject, throwError } from 'rxjs';
import { vi } from 'vitest';
import type { ComboBox } from '@vaadin/combo-box';
import type { Details } from '@vaadin/details';
import type { Checkbox } from '@vaadin/checkbox';
import { expect, fixture, html } from '../_helpers';
import type {
  DatabaseAccessDirectoryIdentity,
  DatabaseAccessImportRequest,
  DatabaseAccessState,
  UserDbPermissionApiModel
} from '../../src/apis/dorc-api/models';
import type { HegsJsonViewer } from '../../src/components/hegs-json-viewer';

const mocks = vi.hoisted(() => ({
  status: vi.fn(),
  get: vi.fn(),
  audit: vi.fn(),
  save: vi.fn(),
  preview: vi.fn(),
  apply: vi.fn(),
  directory: vi.fn(),
  resolve: vi.fn(),
  lookup: vi.fn(),
  importPreview: vi.fn(),
  import: vi.fn(),
  legacy: vi.fn(),
  confirm: vi.fn()
}));
vi.mock('../../src/apis/dorc-api/apis/RefDataDatabaseUsersApi', () => ({
  RefDataDatabaseUsersApi: class {
    refDataDatabaseUsersGetDbUsersPermissionsGet = mocks.legacy;
  }
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
    databaseAccessLookup = mocks.lookup;
    databaseAccessImportPreview = mocks.importPreview;
    databaseAccessExecuteImport = mocks.import;
  }
}));
vi.mock('../../src/components/confirm-prompt', () => ({
  confirmPrompt: mocks.confirm
}));

import '../../src/components/database-access-panel';
import type { DatabaseAccessPanel } from '../../src/components/database-access-panel';

const empty: DatabaseAccessState = {
  DatabaseId: 1,
  Revision: 0,
  Provider: 'sql-server',
  Principals: [],
  Roles: [],
  Memberships: []
};

const legacy: UserDbPermissionApiModel = {
  UserId: 7,
  UserLoginId: 'app',
  UserLoginType: 'Endur',
  PermissionName: 'Read',
  DbId: 1
};

const directoryIdentity: DatabaseAccessDirectoryIdentity = {
  Id: 'S-1-5-21-123',
  DisplayName: 'Deployment readers',
  IsGroup: true,
  Status: 'Resolved'
};

const directoryState: DatabaseAccessState = {
  ...empty,
  Principals: [
    {
      Name: 'DOMAIN\\readers',
      Kind: 'DirectoryGroup',
      DirectoryId: directoryIdentity.Id,
      LegacyUserId: 7,
      Present: true
    }
  ]
};

const ownerState: DatabaseAccessState = {
  ...empty,
  Principals: [
    {
      Name: 'app',
      Kind: 'Native',
      LoginName: 'app',
      LegacyUserId: 7,
      Present: true
    }
  ],
  Roles: [{ Name: 'db_owner', Managed: false, Present: true }],
  Memberships: [{ Principal: 'app', Role: 'db_owner', Present: true }]
};

function mockDbOwnerImport() {
  mocks.legacy.mockReturnValue(of([{ ...legacy, PermissionName: 'db_owner' }]));
  mocks.importPreview.mockImplementation(
    ({
      databaseAccessImportRequest: request
    }: {
      databaseAccessImportRequest: DatabaseAccessImportRequest;
    }) =>
      of({
        Token: request.ApprovedDbOwnerLegacyUserIds?.length
          ? 'owner-approved'
          : 'owner-blocked',
        SourceAssignments: 1,
        Proposed: request.ApprovedDbOwnerLegacyUserIds?.length
          ? structuredClone(ownerState)
          : empty,
        Errors: request.ApprovedDbOwnerLegacyUserIds?.length
          ? []
          : ['Legacy user 7 requires explicit db_owner membership approval.'],
        ApprovedDbOwnerLegacyUserIds: request.ApprovedDbOwnerLegacyUserIds ?? []
      })
  );
}

async function prepareImport(panel: DatabaseAccessPanel) {
  await vi.waitFor(() =>
    expect(panel.shadowRoot!.textContent).to.contain('Load legacy access')
  );
  click(panel, 'Load legacy access');
  await vi.waitFor(() =>
    expect(mocks.importPreview.mock.calls.length).to.equal(1)
  );
  await vi.waitFor(() =>
    expect(
      panel.shadowRoot!.querySelector('#import-proposed-access')
    ).not.to.equal(null)
  );
  await panel.updateComplete;
}

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
  const select = panel.shadowRoot!.querySelector('vaadin-combo-box')!;
  select.value = '1';
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
        CanApproveDbOwner: true,
        Providers: ['sql-server'],
        Limitations: []
      })
    );
    mocks.get.mockReturnValue(of(structuredClone(empty)));
    mocks.audit.mockReturnValue(of([]));
    mocks.legacy.mockReturnValue(of([legacy]));
    mocks.lookup.mockReturnValue(
      throwError(
        () =>
          new Error('Exact directory lookup could not resolve this account.')
      )
    );
    mocks.importPreview.mockReturnValue(
      of({
        Token: 'import-token',
        Proposed: structuredClone(empty),
        Errors: [],
        SourceAssignments: 1
      })
    );
    mocks.preview.mockReturnValue(
      of({
        Token: 'preview-1',
        Revision: 0,
        Observed: {
          Principals: [],
          Roles: [
            { Name: 'Read', Managed: false, Present: true },
            { Name: 'db_datareader', Managed: false, Present: true }
          ],
          Memberships: [],
          Logins: []
        },
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
        'Database access management is disabled'
      )
    );
    expect(mocks.get.mock.calls.length).to.equal(0);
    const notice = panel.shadowRoot!.querySelector('.notice')!;
    expect(notice.textContent).to.contain('DatabaseAccess:Enabled');
    expect(notice.textContent).not.to.contain('DatabaseAccess:DatabaseIds');
    expect(notice.textContent?.replace(/\s+/g, ' ')).to.contain(
      'existing Users and Permissions'
    );
  });

  it('offers only metadata import and saved configuration, with no target preview or apply', async () => {
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain(
        'No managed principals yet'
      )
    );
    expect(panel.shadowRoot!.querySelector('#legacy-import')).not.to.equal(
      null
    );
    expect(
      panel.shadowRoot!.querySelector('#desired-state-editor')
    ).not.to.equal(null);
    expect(panel.shadowRoot!.querySelector('.workflow-step h3')).to.equal(null);
    const text = panel.shadowRoot!.textContent!;
    expect(text).not.to.contain('Discover and preview target changes');
    expect(text).not.to.contain('Apply confirmed');
    expect(mocks.legacy.mock.calls.length).to.equal(0);
    expect(mocks.preview.mock.calls.length).to.equal(0);
    expect(mocks.apply.mock.calls.length).to.equal(0);
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
    const fields = [...panel.shadowRoot!.querySelectorAll('vaadin-text-field')];
    const name = fields.find(f => f.label === 'Database user name')!;
    const login = fields.find(f =>
      f.label?.includes('Existing SQL server login')
    )!;
    name.value = 'app';
    login.value = 'existing_login';
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

  it('saves a role assignment chosen from the principal and role pickers', async () => {
    const assigned: DatabaseAccessState = {
      ...empty,
      Principals: [
        { Name: 'app', Kind: 'Native', LoginName: 'app', Present: true }
      ],
      Roles: [{ Name: 'db_datareader', Managed: false, Present: true }]
    };
    mocks.get.mockReturnValue(of(structuredClone(assigned)));
    mocks.save.mockImplementation(
      ({ databaseAccessState }: { databaseAccessState: DatabaseAccessState }) =>
        of({ ...databaseAccessState, Revision: 1 })
    );
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain('Assign a role')
    );
    const combos = [...panel.shadowRoot!.querySelectorAll('vaadin-combo-box')];
    const principal = combos.find(c => c.label === 'Principal')!;
    const role = combos.find(c => c.label === 'Role')!;
    expect(principal.disabled).to.equal(false);
    const assign = [
      ...panel.shadowRoot!.querySelectorAll('vaadin-button')
    ].find(b => b.textContent?.trim() === 'Save assignment')!;
    expect(assign.hasAttribute('disabled')).to.equal(true);
    principal.value = 'app';
    role.value = 'db_datareader';
    await panel.updateComplete;
    expect(assign.hasAttribute('disabled')).to.equal(false);
    click(panel, 'Save assignment');
    await vi.waitFor(() => expect(mocks.save.mock.calls.length).to.equal(1));
    expect(
      mocks.save.mock.calls[0][0].databaseAccessState.Memberships
    ).to.deep.equal([
      { Principal: 'app', Role: 'db_datareader', Present: true }
    ]);
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain('app → db_datareader')
    );
  });

  it('disables all editor controls when the environment is read-only', async () => {
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain(
        'No managed principals yet'
      )
    );
    panel.readonly = true;
    await panel.updateComplete;
    const fields = panel.shadowRoot!.querySelectorAll(
      'fieldset vaadin-text-field, fieldset vaadin-combo-box, fieldset vaadin-checkbox, fieldset vaadin-text-area'
    );
    expect(fields.length).to.be.greaterThan(0);
    for (const field of fields)
      expect(field.hasAttribute('disabled')).to.equal(true);
    click(panel, 'Save principal');
    expect(mocks.save.mock.calls.length).to.equal(0);
  });

  it('clears the editor and selection when switching environments', async () => {
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain(
        'No managed principals yet'
      )
    );
    panel.envId = 20;
    await panel.updateComplete;
    expect(panel.shadowRoot!.querySelector('vaadin-combo-box')!.value).to.equal(
      ''
    );
    expect(panel.shadowRoot!.querySelector('fieldset')).to.equal(null);
    click(panel, 'Load database access');
    expect(mocks.status.mock.calls.length).to.equal(1);
  });

  it('keeps long section headings and tables inside a narrow panel', async () => {
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain(
        'No managed principals yet'
      )
    );
    panel.style.width = '320px';
    const details = panel.shadowRoot!.querySelector('vaadin-details')!;
    details.opened = true;
    panel.shadowRoot!.querySelector<Details>('#desired-state-editor')!.opened =
      true;
    const tableContainer = panel.shadowRoot!.querySelector('.scroll')!;
    await vi.waitFor(() =>
      expect(tableContainer.clientWidth).to.be.greaterThan(0)
    );
    expect(panel.scrollWidth).to.be.at.most(panel.clientWidth);
    expect(tableContainer.scrollWidth).to.be.greaterThan(
      tableContainer.clientWidth
    );
  });

  it('loads, previews and imports metadata without contacting an unavailable target', async () => {
    const imported = {
      Token: 'import-token',
      Proposed: empty,
      Errors: [],
      SourceAssignments: 1
    };
    mocks.importPreview.mockReturnValue(of(imported));
    mocks.import.mockReturnValue(of(imported));
    mocks.preview.mockReturnValue(
      throwError(() => new Error('Target is unavailable'))
    );
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain(
        'No managed principals yet'
      )
    );
    await prepareImport(panel);
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
        RoleMappings: { Read: 'Read' },
        DirectoryMappings: {},
        ExcludedLegacyUserIds: [],
        ApprovedDbOwnerLegacyUserIds: [],
        Token: 'import-token'
      }
    });
    expect(mocks.preview.mock.calls.length).to.equal(0);
    expect(mocks.apply.mock.calls.length).to.equal(0);
    expect(mocks.confirm.mock.calls[0][0]).to.contain(
      'Target access has not been checked'
    );
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain('Imported desired state')
    );
    expect(mocks.preview.mock.calls.length).to.equal(0);
    expect(mocks.apply.mock.calls.length).to.equal(0);
  });

  it('accepts a custom role without discovery and invalidates the import preview when edited', async () => {
    mocks.importPreview.mockReturnValue(
      of({
        Token: 'import-token',
        Proposed: empty,
        Errors: [],
        SourceAssignments: 1
      })
    );
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain(
        'No managed principals yet'
      )
    );
    await prepareImport(panel);
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent?.replace(/\s+/g, ' ')).to.contain(
        '1 legacy assignments'
      )
    );
    const mappings = panel.shadowRoot!.querySelector<ComboBox<string>>(
      'vaadin-combo-box[data-permission="Read"]'
    )!;
    expect(mappings.items).not.to.include('db_datareader');
    const input = mappings.inputElement!;
    if (!(input instanceof HTMLInputElement))
      throw new Error('Role mapping requires a text input.');
    input.value = 'db_datareader';
    input.dispatchEvent(new Event('input', { bubbles: true, composed: true }));
    input.dispatchEvent(
      new KeyboardEvent('keydown', {
        key: 'Enter',
        code: 'Enter',
        bubbles: true,
        composed: true
      })
    );
    await vi.waitFor(() => expect(mappings.value).to.equal('db_datareader'));
    await panel.updateComplete;
    click(panel, 'Import confirmed preview');
    expect(mocks.import.mock.calls.length).to.equal(0);
    click(panel, 'Preview legacy import');
    await vi.waitFor(() =>
      expect(mocks.importPreview.mock.calls.length).to.equal(2)
    );
    expect(
      mocks.importPreview.mock.calls[1][0].databaseAccessImportRequest
        .RoleMappings
    ).to.deep.equal({ Read: 'db_datareader' });
    expect(mocks.preview.mock.calls).to.have.length(0);
  });

  it('suggests metadata roles and verifies saved DOrc directory bindings without target discovery', async () => {
    mocks.legacy.mockReturnValue(
      of([
        { ...legacy, UserLoginId: 'DOMAIN\\readers', UserLoginType: 'Windows' },
        {
          ...legacy,
          UserLoginId: 'DOMAIN\\readers',
          UserLoginType: 'Windows',
          PermissionName: 'Audit'
        }
      ])
    );
    mocks.get.mockReturnValue(
      of({
        ...structuredClone(directoryState),
        Roles: [{ Name: 'read', Managed: false, Present: true }]
      })
    );
    mocks.resolve.mockReturnValue(of(directoryIdentity));
    mocks.importPreview.mockReturnValue(
      of({
        Token: 'import-token',
        Proposed: empty,
        Errors: [],
        SourceAssignments: 2
      })
    );
    const panel = await load();
    await prepareImport(panel);
    expect(mocks.legacy.mock.calls[0][0]).to.deep.equal({
      serverName: 'Server',
      databaseName: 'App'
    });
    expect(mocks.resolve.mock.calls).to.have.length(2);
    expect(mocks.resolve.mock.calls[1][0]).to.deep.equal({
      envId: 10,
      databaseId: 1,
      id: directoryIdentity.Id
    });
    const mappings = {
      RoleMappings: { Audit: 'Audit', Read: 'read' },
      DirectoryMappings: { '7': directoryIdentity.Id },
      ExcludedLegacyUserIds: [],
      ApprovedDbOwnerLegacyUserIds: []
    };
    expect(
      mocks.importPreview.mock.calls[0][0].databaseAccessImportRequest
    ).to.deep.equal({ ...mappings, Token: '' });
    expect(
      panel.shadowRoot!.querySelector('#import-account-review')!.textContent
    ).to.contain('DOMAIN\\readers');
    expect(
      panel.shadowRoot!.querySelector('#import-account-review')!.textContent
    ).to.contain('Deployment readers');
    expect(
      panel.shadowRoot!.querySelectorAll('vaadin-text-area')
    ).to.have.length(0);
    const viewer = panel.shadowRoot!.querySelector<HegsJsonViewer>(
      '#import-mappings-json'
    )!;
    expect(viewer.data).to.deep.equal(mappings);
    expect(viewer.closest('vaadin-details')!.opened).to.equal(false);
    expect(mocks.save.mock.calls).to.have.length(0);
    expect(mocks.import.mock.calls).to.have.length(0);
    expect(mocks.apply.mock.calls).to.have.length(0);
    expect(mocks.preview.mock.calls).to.have.length(0);
    expect(panel.shadowRoot!.textContent).not.to.contain(
      'Exclude from migration'
    );
  });

  it('explicitly excludes unresolved accounts, previews every skipped assignment and confirms without applying SQL', async () => {
    const missing = {
      ...legacy,
      UserId: 8,
      UserLoginId: 'DOMAIN\\missing',
      UserLoginType: 'Windows'
    };
    const exclusion = {
      LegacyUserId: 8,
      LoginName: missing.UserLoginId,
      Permissions: ['Read', 'OldRole'],
      AssignmentCount: 2
    };
    mocks.legacy.mockReturnValue(
      of([legacy, missing, { ...missing, PermissionName: 'OldRole' }])
    );
    mocks.importPreview.mockImplementation(
      ({
        databaseAccessImportRequest: request
      }: {
        databaseAccessImportRequest: DatabaseAccessImportRequest;
      }) =>
        of({
          Token: request.ExcludedLegacyUserIds?.length
            ? 'excluded-token'
            : 'blocked-token',
          Proposed: empty,
          SourceAssignments: 3,
          Errors: request.ExcludedLegacyUserIds?.length
            ? []
            : ['Windows user 8 requires an explicit live directory reference.'],
          ExcludedAccounts: request.ExcludedLegacyUserIds?.length
            ? [exclusion]
            : []
        })
    );
    mocks.import.mockReturnValue(of({ Proposed: empty }));
    const panel = await load();
    await prepareImport(panel);
    expect(
      mocks.importPreview.mock.calls[0][0].databaseAccessImportRequest
        .ExcludedLegacyUserIds
    ).to.deep.equal([]);
    click(panel, 'Exclude from migration');
    await panel.updateComplete;
    expect(panel.shadowRoot!.textContent).to.contain(
      'Excluded from this import'
    );
    expect(panel.shadowRoot!.textContent?.replace(/\s+/g, ' ')).to.contain(
      'Not imported: all accounts with this permission are excluded.'
    );
    click(panel, 'Import confirmed preview');
    expect(mocks.import.mock.calls).to.have.length(0);
    click(panel, 'Preview legacy import');
    await vi.waitFor(() =>
      expect(
        panel.shadowRoot!.querySelector('#import-exclusions')?.textContent
      ).to.contain('DOMAIN\\missing')
    );
    expect(
      panel
        .shadowRoot!.querySelector('#import-exclusions')
        ?.textContent?.replace(/\s+/g, ' ')
    ).to.contain('2 assignments; Read, OldRole');
    click(panel, 'Import confirmed preview');
    await vi.waitFor(() => expect(mocks.import.mock.calls).to.have.length(1));
    expect(mocks.confirm.mock.calls[0][0]).to.contain(
      'DOMAIN\\missing (legacy ID 8, 2 assignments)'
    );
    expect(
      mocks.import.mock.calls[0][0].databaseAccessImportRequest
    ).to.deep.equal({
      RoleMappings: { OldRole: 'OldRole', Read: 'Read' },
      DirectoryMappings: {},
      ExcludedLegacyUserIds: [8],
      ApprovedDbOwnerLegacyUserIds: [],
      Token: 'excluded-token'
    });
    expect(mocks.apply.mock.calls).to.have.length(0);
    expect(mocks.save.mock.calls).to.have.length(0);
  });

  it('invalidates exclusions on reinclusion and reload, and disables the choice for read-only users', async () => {
    mocks.legacy.mockReturnValue(
      of([
        { ...legacy, UserLoginId: 'DOMAIN\\missing', UserLoginType: 'Windows' }
      ])
    );
    const panel = await load();
    await prepareImport(panel);
    panel.readonly = true;
    await panel.updateComplete;
    const exclude = [
      ...panel.shadowRoot!.querySelectorAll('vaadin-button')
    ].find(b => b.textContent?.trim() === 'Exclude from migration')!;
    expect(exclude.disabled).to.equal(true);
    exclude.click();
    await panel.updateComplete;
    expect(panel.shadowRoot!.textContent).not.to.contain(
      'Excluded from this import'
    );
    panel.readonly = false;
    await panel.updateComplete;
    click(panel, 'Exclude from migration');
    await panel.updateComplete;
    click(panel, 'Include in migration');
    await panel.updateComplete;
    click(panel, 'Import confirmed preview');
    expect(mocks.import.mock.calls).to.have.length(0);
    click(panel, 'Preview legacy import');
    await vi.waitFor(() =>
      expect(mocks.importPreview.mock.calls).to.have.length(2)
    );
    expect(
      mocks.importPreview.mock.calls[1][0].databaseAccessImportRequest
        .ExcludedLegacyUserIds
    ).to.deep.equal([]);
    await panel.updateComplete;
    click(panel, 'Exclude from migration');
    await panel.updateComplete;
    click(panel, 'Reload legacy access');
    await vi.waitFor(() =>
      expect(mocks.importPreview.mock.calls).to.have.length(3)
    );
    expect(
      mocks.importPreview.mock.calls[2][0].databaseAccessImportRequest
        .ExcludedLegacyUserIds
    ).to.deep.equal([]);
  });

  it.each(['unsupported', 'wrong-count', 'still-proposed'])(
    'refuses import when exclusion acknowledgement is %s',
    async acknowledgement => {
      mocks.legacy.mockReturnValue(
        of([
          {
            ...legacy,
            UserLoginId: 'DOMAIN\\missing',
            UserLoginType: 'Windows'
          }
        ])
      );
      const panel = await load();
      await prepareImport(panel);
      click(panel, 'Exclude from migration');
      await panel.updateComplete;
      mocks.importPreview.mockReturnValue(
        of({
          Token: 'invalid-exclusion',
          SourceAssignments: 1,
          Errors: [],
          Proposed:
            acknowledgement === 'still-proposed'
              ? {
                  ...empty,
                  Principals: [{ Name: 'DOMAIN\\missing', LegacyUserId: 7 }]
                }
              : empty,
          ExcludedAccounts:
            acknowledgement === 'unsupported'
              ? undefined
              : [
                  {
                    LegacyUserId: 7,
                    LoginName: 'DOMAIN\\missing',
                    Permissions: ['Read'],
                    AssignmentCount: acknowledgement === 'wrong-count' ? 2 : 1
                  }
                ]
        })
      );
      click(panel, 'Preview legacy import');
      await vi.waitFor(() =>
        expect(panel.shadowRoot!.textContent).to.contain(
          'The API did not confirm the selected account exclusions'
        )
      );
      click(panel, 'Import confirmed preview');
      expect(mocks.import.mock.calls).to.have.length(0);
    }
  );

  it('keeps unresolved accounts and protected roles visible with named, deduplicated blockers', async () => {
    mocks.legacy.mockReturnValue(
      of([
        {
          ...legacy,
          UserLoginId: 'DOMAIN\\missing',
          UserLoginType: 'Windows',
          PermissionName: 'db_owner'
        },
        {
          ...legacy,
          UserLoginId: 'DOMAIN\\missing',
          UserLoginType: 'Windows',
          PermissionName: 'Read'
        }
      ])
    );
    mocks.get.mockReturnValue(
      of({
        ...structuredClone(directoryState),
        Principals: directoryState.Principals.map(p => ({
          ...p,
          Name: 'DOMAIN\\missing'
        }))
      })
    );
    mocks.resolve.mockReturnValue(
      throwError(() => new Error('Directory account is unavailable'))
    );
    mocks.importPreview.mockReturnValue(
      of({
        Token: 'blocked',
        Proposed: empty,
        SourceAssignments: 2,
        Errors: [
          'Windows user 7 requires an explicit live directory reference.',
          'Windows user 7 requires an explicit live directory reference.',
          "Duplicate, conflicting or protected role 'db_owner'."
        ]
      })
    );
    const panel = await load();
    await prepareImport(panel);
    expect(
      panel.shadowRoot!.querySelectorAll('[data-legacy-user-id]')
    ).to.have.length(1);
    expect(panel.shadowRoot!.textContent).to.contain(
      'Directory account is unavailable'
    );
    const errors = [...panel.shadowRoot!.querySelectorAll('p[role="alert"]')];
    expect(
      errors.filter(e =>
        e.textContent?.includes("Windows account 'DOMAIN\\missing'")
      )
    ).to.have.length(1);
    expect(panel.shadowRoot!.textContent).to.contain(
      'Role mapping blocked by the API'
    );
    expect(
      mocks.importPreview.mock.calls[0][0].databaseAccessImportRequest
    ).to.deep.equal({
      RoleMappings: { Read: 'Read', db_owner: 'db_owner' },
      DirectoryMappings: {},
      ExcludedLegacyUserIds: [],
      ApprovedDbOwnerLegacyUserIds: [],
      Token: ''
    });
    click(panel, 'Import confirmed preview');
    expect(mocks.import.mock.calls).to.have.length(0);
  });

  it('requires per-account db_owner approval and includes it in the confirmed import', async () => {
    mockDbOwnerImport();
    mocks.import.mockReturnValue(of({ Proposed: ownerState }));
    const panel = await load();
    await prepareImport(panel);
    const approval = panel.shadowRoot!.querySelector<Checkbox>(
      '[data-db-owner-id="7"]'
    )!;
    expect(approval.checked).to.equal(false);
    expect(
      mocks.importPreview.mock.calls[0][0].databaseAccessImportRequest
        .ApprovedDbOwnerLegacyUserIds
    ).to.deep.equal([]);
    click(panel, 'Import confirmed preview');
    expect(mocks.import.mock.calls).to.have.length(0);
    approval.checked = true;
    await panel.updateComplete;
    expect(panel.shadowRoot!.querySelector('#import-proposed-access')).to.equal(
      null
    );
    click(panel, 'Preview legacy import');
    await vi.waitFor(() =>
      expect(
        panel.shadowRoot!.querySelector('#import-db-owner-approvals')
          ?.textContent
      ).to.contain('app')
    );
    expect(approval.checked).to.equal(true);
    click(panel, 'Import confirmed preview');
    await vi.waitFor(() => expect(mocks.import.mock.calls).to.have.length(1));
    expect(mocks.confirm.mock.calls[0][0]).to.contain(
      'Manage db_owner membership for: app'
    );
    expect(mocks.confirm.mock.calls[0][0]).to.contain('full database control');
    expect(
      mocks.import.mock.calls[0][0].databaseAccessImportRequest
    ).to.deep.equal({
      RoleMappings: { db_owner: 'db_owner' },
      DirectoryMappings: {},
      ExcludedLegacyUserIds: [],
      ApprovedDbOwnerLegacyUserIds: [7],
      Token: 'owner-approved'
    });
    expect(mocks.apply.mock.calls).to.have.length(0);
  });

  it('clears db_owner approval when mappings change and invalidates unchecked approval', async () => {
    mockDbOwnerImport();
    const panel = await load();
    await prepareImport(panel);
    let approval = panel.shadowRoot!.querySelector<Checkbox>(
      '[data-db-owner-id="7"]'
    )!;
    approval.checked = true;
    await panel.updateComplete;
    click(panel, 'Preview legacy import');
    await vi.waitFor(() =>
      expect(
        panel.shadowRoot!.querySelector('#import-db-owner-approvals')
      ).not.to.equal(null)
    );
    approval.checked = false;
    await panel.updateComplete;
    click(panel, 'Import confirmed preview');
    expect(mocks.import.mock.calls).to.have.length(0);
    approval.checked = true;
    await panel.updateComplete;
    const role = panel.shadowRoot!.querySelector<ComboBox<string>>(
      '[data-permission="db_owner"]'
    )!;
    role.value = 'Read';
    await panel.updateComplete;
    role.value = 'db_owner';
    await panel.updateComplete;
    approval = panel.shadowRoot!.querySelector<Checkbox>(
      '[data-db-owner-id="7"]'
    )!;
    expect(approval.checked).to.equal(false);
    panel.readonly = true;
    await panel.updateComplete;
    expect(approval.disabled).to.equal(true);
  });

  it('does not submit db_owner approval without privileged approval rights', async () => {
    mockDbOwnerImport();
    mocks.status.mockReturnValue(
      of({
        Enabled: true,
        ExecutionEnabled: true,
        CanApproveDbOwner: false,
        Providers: ['sql-server'],
        Limitations: []
      })
    );
    const panel = await load();
    await prepareImport(panel);
    const approval = panel.shadowRoot!.querySelector<Checkbox>(
      '[data-db-owner-id="7"]'
    )!;
    expect(approval.disabled).to.equal(true);
    approval.checked = true;
    await panel.updateComplete;
    click(panel, 'Preview legacy import');
    await vi.waitFor(() =>
      expect(mocks.importPreview.mock.calls).to.have.length(2)
    );
    expect(
      mocks.importPreview.mock.calls[1][0].databaseAccessImportRequest
        .ApprovedDbOwnerLegacyUserIds
    ).to.deep.equal([]);
    click(panel, 'Import confirmed preview');
    expect(mocks.import.mock.calls).to.have.length(0);
  });

  it.each([true, false])(
    'rejects unacknowledged or unsolicited db_owner proposals (requested: %s)',
    async requested => {
      mockDbOwnerImport();
      const panel = await load();
      await prepareImport(panel);
      if (requested) {
        panel.shadowRoot!.querySelector<Checkbox>(
          '[data-db-owner-id="7"]'
        )!.checked = true;
        await panel.updateComplete;
      }
      mocks.importPreview.mockReturnValue(
        of({
          Token: 'unsafe',
          SourceAssignments: 1,
          Proposed: ownerState,
          Errors: [],
          ApprovedDbOwnerLegacyUserIds: []
        })
      );
      click(panel, 'Preview legacy import');
      await vi.waitFor(() =>
        expect(panel.shadowRoot!.textContent).to.contain(
          'The API did not confirm the db_owner approvals'
        )
      );
      click(panel, 'Import confirmed preview');
      expect(mocks.import.mock.calls).to.have.length(0);
    }
  );

  it('reveals and focuses the identity picker below a long account list and requires explicit verification', async () => {
    const accounts = [
      { ...legacy, UserLoginId: 'DOMAIN\\readers', UserLoginType: 'Windows' },
      ...Array.from({ length: 30 }, (_, index) => ({
        ...legacy,
        UserId: 100 + index,
        UserLoginId: `zzz-app-${index}`
      }))
    ];
    mocks.legacy.mockReturnValue(of(accounts));
    mocks.importPreview.mockReturnValue(
      of({
        Token: 'blocked',
        Proposed: empty,
        SourceAssignments: accounts.length,
        Errors: [
          'Windows user 7 requires an explicit live directory reference.'
        ]
      })
    );
    mocks.directory.mockReturnValue(of([directoryIdentity]));
    mocks.resolve.mockReturnValue(of(directoryIdentity));
    const panel = await load();
    await prepareImport(panel);
    panel.style.height = '320px';
    panel.style.overflow = 'auto';
    panel.shadowRoot!.querySelector<Details>('.panel')!.opened = true;
    panel.shadowRoot!.querySelector<Details>('#legacy-import')!.opened = true;
    await vi.waitFor(() =>
      expect(panel.scrollHeight).to.be.greaterThan(panel.clientHeight)
    );
    panel
      .shadowRoot!.querySelector('[data-legacy-user-id="7"]')!
      .scrollIntoView({
        block: 'center',
        inline: 'nearest'
      });
    expect(mocks.resolve.mock.calls).to.have.length(0);
    expect(panel.shadowRoot!.textContent).to.contain(
      'Exact directory lookup could not resolve this account.'
    );
    click(panel, 'Choose identity');
    await vi.waitFor(() => {
      const search = panel.shadowRoot!.querySelector<HTMLElement>(
        '#import-directory-picker vaadin-text-field'
      )!;
      expect(search).not.to.equal(null);
      expect(search.matches(':focus-within')).to.equal(true);
      const bounds = search.getBoundingClientRect();
      const viewport = panel.getBoundingClientRect();
      expect(bounds.top).to.be.at.least(viewport.top);
      expect(bounds.bottom).to.be.at.most(viewport.bottom);
    });
    click(panel, 'Search directory');
    await vi.waitFor(() =>
      expect(mocks.directory.mock.calls).to.have.length(1)
    );
    await panel.updateComplete;
    click(panel, 'Use selected identity');
    expect(mocks.resolve.mock.calls).to.have.length(0);
    const picker = panel.shadowRoot!.querySelector<ComboBox>(
      '#import-directory-picker vaadin-combo-box'
    )!;
    picker.value = directoryIdentity.Id;
    await panel.updateComplete;
    click(panel, 'Use selected identity');
    await vi.waitFor(() => expect(mocks.resolve.mock.calls).to.have.length(1));
    await vi.waitFor(() =>
      expect(
        panel.shadowRoot!.querySelector('#import-directory-picker')
      ).to.equal(null)
    );
    expect(panel.shadowRoot!.textContent).to.contain(
      'Manually selected and verified'
    );
    click(panel, 'Import confirmed preview');
    expect(mocks.import.mock.calls).to.have.length(0);
    click(panel, 'Preview legacy import');
    await vi.waitFor(() =>
      expect(mocks.importPreview.mock.calls).to.have.length(2)
    );
    expect(
      mocks.importPreview.mock.calls[1][0].databaseAccessImportRequest
        .DirectoryMappings
    ).to.deep.equal({ '7': directoryIdentity.Id });
  });

  it.each([
    { ...legacy, DbId: 2 },
    { ...legacy, UserId: undefined },
    { ...legacy, PermissionName: '' }
  ])(
    'rejects out-of-scope or incomplete legacy assignments',
    async assignment => {
      mocks.legacy.mockReturnValue(of([assignment]));
      const panel = await load();
      await vi.waitFor(() =>
        expect(panel.shadowRoot!.textContent).to.contain(
          'No managed principals yet'
        )
      );
      click(panel, 'Load legacy access');
      await vi.waitFor(() =>
        expect(panel.shadowRoot!.textContent).to.contain(
          'No mappings were prepared'
        )
      );
      expect(mocks.importPreview.mock.calls).to.have.length(0);
      expect(mocks.import.mock.calls).to.have.length(0);
    }
  );

  it('automatically matches exact Windows users and groups once per account without approving db_owner or querying SQL', async () => {
    const group = {
      ...legacy,
      UserLoginId: 'DOMAIN\\readers',
      UserLoginType: 'Windows'
    };
    mocks.legacy.mockReturnValue(
      of([
        group,
        { ...group, PermissionName: 'db_owner' },
        { ...group, UserId: 8, UserLoginId: 'DOMAIN\\person' }
      ])
    );
    mocks.lookup.mockImplementation(
      ({ accountName }: { accountName: string }) =>
        of(
          accountName === 'DOMAIN\\readers'
            ? directoryIdentity
            : {
                ...directoryIdentity,
                Id: 'S-1-5-21-456',
                DisplayName: 'Person',
                IsGroup: false
              }
        )
    );
    mocks.importPreview.mockReturnValue(
      of({
        Token: 'needs-approval',
        Proposed: empty,
        SourceAssignments: 3,
        Errors: [
          'Legacy user 7 requires explicit db_owner membership approval.'
        ]
      })
    );
    const panel = await load();
    await prepareImport(panel);
    expect(mocks.lookup.mock.calls.map(call => call[0])).to.deep.equal([
      { envId: 10, databaseId: 1, accountName: 'DOMAIN\\person' },
      { envId: 10, databaseId: 1, accountName: 'DOMAIN\\readers' }
    ]);
    const request =
      mocks.importPreview.mock.calls[0][0].databaseAccessImportRequest;
    expect(request.DirectoryMappings).to.deep.equal({
      '7': directoryIdentity.Id,
      '8': 'S-1-5-21-456'
    });
    expect(request.ApprovedDbOwnerLegacyUserIds).to.deep.equal([]);
    const approval = panel.shadowRoot!.querySelector<Checkbox>(
      '[data-db-owner-id="7"]'
    )!;
    expect(approval.disabled).to.equal(false);
    expect(approval.checked).to.equal(false);
    expect(panel.shadowRoot!.textContent).to.contain('Automatically matched');
    expect(mocks.resolve.mock.calls).to.have.length(0);
    expect(mocks.preview.mock.calls).to.have.length(0);
    expect(mocks.import.mock.calls).to.have.length(0);
    expect(mocks.apply.mock.calls).to.have.length(0);
  });

  it.each([
    { ...directoryIdentity, Id: '' },
    { ...directoryIdentity, Status: 'Unavailable' }
  ])('requires a verified identity from automatic lookup', async identity => {
    mocks.legacy.mockReturnValue(
      of([
        { ...legacy, UserLoginId: 'DOMAIN\\readers', UserLoginType: 'Windows' }
      ])
    );
    mocks.lookup.mockReturnValue(of(identity));
    mocks.importPreview.mockReturnValue(
      of({
        Token: 'blocked',
        Proposed: empty,
        SourceAssignments: 1,
        Errors: [
          'Windows user 7 requires an explicit live directory reference.'
        ]
      })
    );
    const panel = await load();
    await prepareImport(panel);
    expect(panel.shadowRoot!.textContent).to.contain(
      'did not return a verified directory identity'
    );
    expect(
      mocks.importPreview.mock.calls[0][0].databaseAccessImportRequest
        .DirectoryMappings
    ).to.deep.equal({});
    click(panel, 'Import confirmed preview');
    expect(mocks.import.mock.calls).to.have.length(0);
  });

  it('discards automatic directory lookup after switching environments', async () => {
    const pending = new Subject<DatabaseAccessDirectoryIdentity>();
    mocks.legacy.mockReturnValue(
      of([
        { ...legacy, UserLoginId: 'DOMAIN\\readers', UserLoginType: 'Windows' }
      ])
    );
    mocks.lookup.mockReturnValue(pending);
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain('Load legacy access')
    );
    click(panel, 'Load legacy access');
    await vi.waitFor(() => expect(mocks.lookup.mock.calls).to.have.length(1));
    panel.envId = 20;
    await panel.updateComplete;
    pending.next(directoryIdentity);
    pending.complete();
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(mocks.importPreview.mock.calls).to.have.length(0);
    expect(panel.shadowRoot!.querySelector('#import-account-review')).to.equal(
      null
    );
  });

  it('discards directory resolution after switching targets and does not preview the new target with old mappings', async () => {
    const pending = new Subject<DatabaseAccessDirectoryIdentity>();
    mocks.legacy.mockReturnValue(
      of([
        { ...legacy, UserLoginId: 'DOMAIN\\readers', UserLoginType: 'Windows' }
      ])
    );
    mocks.get.mockReturnValue(of(structuredClone(directoryState)));
    mocks.resolve
      .mockReturnValueOnce(of(directoryIdentity))
      .mockReturnValue(pending);
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain('Load legacy access')
    );
    click(panel, 'Load legacy access');
    await vi.waitFor(() => expect(mocks.resolve.mock.calls).to.have.length(2));
    panel.envId = 20;
    await panel.updateComplete;
    pending.next(directoryIdentity);
    pending.complete();
    await new Promise(resolve => setTimeout(resolve, 0));
    await panel.updateComplete;
    expect(mocks.importPreview.mock.calls).to.have.length(0);
    expect(panel.shadowRoot!.querySelector('#import-account-review')).to.equal(
      null
    );
  });

  it('requires reloading when the source assignment count changes before preview', async () => {
    mocks.importPreview.mockReturnValue(
      of({
        Token: 'changed',
        Proposed: empty,
        Errors: [],
        SourceAssignments: 2
      })
    );
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain(
        'No managed principals yet'
      )
    );
    click(panel, 'Load legacy access');
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain(
        'Legacy assignments changed'
      )
    );
    click(panel, 'Import confirmed preview');
    expect(mocks.import.mock.calls).to.have.length(0);
  });

  it('keeps the guided review and standard JSON viewers inside a narrow panel', async () => {
    const panel = await load();
    await prepareImport(panel);
    panel.style.width = '320px';
    panel.shadowRoot!.querySelector('vaadin-details')!.opened = true;
    panel.shadowRoot!.querySelector<Details>('#legacy-import')!.opened = true;
    const viewer = panel.shadowRoot!.querySelector<HegsJsonViewer>(
      '#import-mappings-json'
    )!;
    viewer.closest('vaadin-details')!.opened = true;
    await vi.waitFor(() => expect(viewer.clientWidth).to.be.greaterThan(0));
    expect(panel.scrollWidth).to.be.at.most(panel.clientWidth);
    const review = panel.shadowRoot!.querySelector(
      '#import-account-review'
    )!.parentElement!;
    expect(review.scrollWidth).to.be.greaterThan(review.clientWidth);
    expect(viewer.scrollWidth).to.be.at.least(viewer.clientWidth);
  });

  it.each([
    { ...directoryIdentity, Id: 'S-1-5-21-999' },
    { ...directoryIdentity, IsGroup: false }
  ])(
    'rejects directory results that change the saved DOrc identity or type',
    async identity => {
      mocks.legacy.mockReturnValue(
        of([
          {
            ...legacy,
            UserLoginId: 'DOMAIN\\readers',
            UserLoginType: 'Windows'
          }
        ])
      );
      mocks.get.mockReturnValue(of(structuredClone(directoryState)));
      mocks.resolve.mockReturnValue(of(identity));
      mocks.importPreview.mockReturnValue(
        of({
          Token: 'blocked',
          Proposed: empty,
          SourceAssignments: 1,
          Errors: [
            'Windows user 7 requires an explicit live directory reference.'
          ]
        })
      );
      const panel = await load();
      await prepareImport(panel);
      expect(panel.shadowRoot!.textContent).to.contain(
        'does not match the saved DOrc identity'
      );
      expect(
        mocks.importPreview.mock.calls[0][0].databaseAccessImportRequest
          .DirectoryMappings
      ).to.deep.equal({});
      expect(mocks.lookup.mock.calls).to.have.length(0);
      click(panel, 'Import confirmed preview');
      expect(mocks.import.mock.calls).to.have.length(0);
    }
  );

  it('disables the guided import when the environment becomes read-only', async () => {
    const panel = await load();
    await prepareImport(panel);
    panel.readonly = true;
    await panel.updateComplete;
    for (const control of panel.shadowRoot!.querySelectorAll(
      '#legacy-import vaadin-combo-box, #legacy-import vaadin-button'
    ))
      expect(control.hasAttribute('disabled')).to.equal(true);
    click(panel, 'Reload legacy access');
    click(panel, 'Preview legacy import');
    click(panel, 'Import confirmed preview');
    expect(mocks.legacy.mock.calls).to.have.length(1);
    expect(mocks.importPreview.mock.calls).to.have.length(1);
    expect(mocks.import.mock.calls).to.have.length(0);
  });
});
