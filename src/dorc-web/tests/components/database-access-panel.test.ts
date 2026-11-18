import { of, Subject, throwError } from 'rxjs';
import { AjaxError } from 'rxjs/ajax';
import { vi } from 'vitest';
import type { ComboBox } from '@vaadin/combo-box';
import type { Details } from '@vaadin/details';
import type { Checkbox } from '@vaadin/checkbox';
import type { RadioGroup } from '@vaadin/radio-group';
import type { TextField } from '@vaadin/text-field';
import { expect, fixture, html } from '../_helpers';
import type {
  DatabaseAccessAuditApiModel,
  DatabaseAccessDirectoryIdentity,
  DatabaseAccessState
} from '../../src/apis/dorc-api/models';

const mocks = vi.hoisted(() => ({
  status: vi.fn(),
  get: vi.fn(),
  audit: vi.fn(),
  save: vi.fn(),
  directory: vi.fn(),
  resolve: vi.fn(),
  logins: vi.fn(),
  confirm: vi.fn()
}));

vi.mock('../../src/apis/dorc-api/apis/DatabaseAccessApi', () => ({
  DatabaseAccessApi: class {
    databaseAccessStatus = mocks.status;
    databaseAccessGet = mocks.get;
    databaseAccessAudit = mocks.audit;
    databaseAccessSave = mocks.save;
    databaseAccessDirectory = mocks.directory;
    databaseAccessResolve = mocks.resolve;
    databaseAccessSqlLogins = mocks.logins;
  }
}));

vi.mock('../../src/components/confirm-prompt', () => ({
  confirmPrompt: mocks.confirm
}));

import '../../src/components/database-access-panel';
import {
  databaseAccessError,
  type DatabaseAccessPanel
} from '../../src/components/database-access-panel';

const empty: DatabaseAccessState = {
  DatabaseId: 1,
  Revision: 0,
  Provider: 'sql-server',
  Principals: [],
  Roles: [],
  Memberships: []
};

const directoryGroup: DatabaseAccessDirectoryIdentity = {
  Id: 'S-1-5-21-123',
  DisplayName: 'Deployment readers',
  IsGroup: true,
  Status: 'Resolved'
};

const directoryUser: DatabaseAccessDirectoryIdentity = {
  Id: 'S-1-5-21-456',
  DisplayName: 'Joe Bloggs',
  AccountName: 'CORP\\jbloggs',
  IsGroup: false,
  Status: 'Resolved'
};

function click(panel: DatabaseAccessPanel, text: string) {
  const button = [...panel.shadowRoot!.querySelectorAll('vaadin-button')].find(
    b => b.textContent?.trim() === text
  );
  if (!button) throw new Error(`Missing button: ${text}`);
  (button as HTMLElement).click();
}

function radio(panel: DatabaseAccessPanel, value: string) {
  const group =
    panel.shadowRoot!.querySelector<RadioGroup>('vaadin-radio-group')!;
  group.value = value;
}

function userName(panel: DatabaseAccessPanel) {
  return [
    ...panel.shadowRoot!.querySelectorAll<TextField>('vaadin-text-field')
  ].find(f => f.label === 'Database user name')!;
}

function role(panel: DatabaseAccessPanel, name: string) {
  const checkbox = panel.shadowRoot!.querySelector<Checkbox>(
    `#grant-access vaadin-checkbox[label="${name}"]`
  );
  if (!checkbox) throw new Error(`Missing role checkbox: ${name}`);
  return checkbox;
}

function normalizedText(el: Element | ShadowRoot | null | undefined) {
  return (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
}

async function settled(panel: DatabaseAccessPanel) {
  await vi.waitFor(() =>
    expect((panel as unknown as { busy: boolean }).busy).to.equal(false)
  );
  await panel.updateComplete;
}

async function load(canApproveDbOwner = false) {
  mocks.status.mockReturnValue(
    of({
      Enabled: true,
      ExecutionEnabled: true,
      CanApproveDbOwner: canApproveDbOwner,
      Providers: ['sql-server'],
      Limitations: ['Managed via DOrc deployments.']
    })
  );
  const before = mocks.status.mock.calls.length;
  const panel = await fixture<DatabaseAccessPanel>(
    html`<database-access-panel
      .envId=${10}
      .readonly=${false}
      .databases=${[
        { Id: 1, Name: 'App', ServerName: 'Server' },
        { Name: 'MissingId', ServerName: 'Server' }
      ]}
    ></database-access-panel>`
  );
  const select = panel.shadowRoot!.querySelector<ComboBox>('#database')!;
  select.value = '1';
  await panel.updateComplete;
  click(panel, 'Load database access');
  await vi.waitFor(() =>
    expect(mocks.status.mock.calls.length).to.equal(before + 1)
  );
  await settled(panel);
  return panel;
}

describe('databaseAccessError', () => {
  it('formats ProblemDetails AjaxError, generic AjaxError, Error, and unknown values', () => {
    const withDetail = Object.create(AjaxError.prototype) as AjaxError;
    Object.assign(withDetail, {
      status: 400,
      response: { detail: 'Explicit problem detail.' }
    });
    expect(databaseAccessError(withDetail)).to.equal(
      'Explicit problem detail.'
    );

    const nonStringDetail = Object.create(AjaxError.prototype) as AjaxError;
    Object.assign(nonStringDetail, {
      status: 422,
      response: { detail: 123 }
    });
    expect(databaseAccessError(nonStringDetail)).to.equal(
      'Request failed (422). Reload and check your access or the service logs.'
    );

    const nullResponse = Object.create(AjaxError.prototype) as AjaxError;
    Object.assign(nullResponse, { status: 503, response: null });
    expect(databaseAccessError(nullResponse)).to.equal(
      'Request failed (503). Reload and check your access or the service logs.'
    );

    expect(databaseAccessError(new Error('Network down'))).to.equal(
      'Network down'
    );
    expect(databaseAccessError('unexpected')).to.equal(
      'Database access request failed.'
    );
  });
});

describe('Database access panel', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mocks.status.mockReturnValue(
      of({
        Enabled: true,
        ExecutionEnabled: true,
        CanApproveDbOwner: false,
        Providers: ['sql-server'],
        Limitations: []
      })
    );
    mocks.get.mockReturnValue(of(structuredClone(empty)));
    mocks.audit.mockReturnValue(of([]));
    mocks.confirm.mockResolvedValue(true);
  });

  it('loads the database selected by its row automatically and handles rapid updates or disconnect', async () => {
    const panel = await fixture<DatabaseAccessPanel>(
      html`<database-access-panel
        .envId=${10}
        .readonly=${false}
        .databases=${[
          { Id: 1, Name: 'App', ServerName: 'Server' },
          { Id: 2, Name: 'Reporting', ServerName: 'Server' }
        ]}
        .selectedDatabaseId=${1}
      ></database-access-panel>`
    );

    await vi.waitFor(() => expect(mocks.status.mock.calls.length).to.equal(1));
    await settled(panel);
    expect(mocks.status.mock.calls[0][0]).to.deep.equal({
      envId: 10,
      databaseId: 1
    });
    expect(panel.shadowRoot!.querySelector('#database')).to.equal(null);
    expect(
      panel.shadowRoot!.querySelector('#selected-database')!.textContent
    ).to.contain('Server / App');

    // Updating databases while keeping the same selectedDatabaseId re-triggers load without resetting databaseId twice
    panel.databases = [{ Id: 1, Name: 'App', ServerName: 'Server' }];
    await vi.waitFor(() => expect(mocks.status.mock.calls.length).to.equal(2));
    await settled(panel);

    // Reload button works when selectedDatabaseId is set
    click(panel, 'Reload database access');
    await vi.waitFor(() => expect(mocks.status.mock.calls.length).to.equal(3));
    await settled(panel);

    // Disconnecting before queued microtask runs cancels the pending load
    panel.databases = [{ Id: 1, Name: 'App2', ServerName: 'Server' }];
    panel.remove();
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(mocks.status.mock.calls.length).to.equal(3);
  });

  it('does not query the API until a database is selected and loaded', async () => {
    const panel = await fixture<DatabaseAccessPanel>(
      html`<database-access-panel
        .envId=${10}
        .databases=${[{ Id: 1, Name: 'App', ServerName: 'Server' }]}
      ></database-access-panel>`
    );
    expect(mocks.status.mock.calls.length).to.equal(0);
    expect(mocks.get.mock.calls.length).to.equal(0);

    // Calling load button while no database is selected is a no-op
    click(panel, 'Load database access');
    expect(mocks.status.mock.calls.length).to.equal(0);

    // Selecting the same empty value is a no-op; selecting a database then removing it from .databases resets selection
    const select = panel.shadowRoot!.querySelector<ComboBox>('#database')!;
    select.dispatchEvent(
      new CustomEvent('value-changed', { detail: { value: '0' } })
    );
    select.value = '1';
    await panel.updateComplete;
    panel.databases = [{ Id: 2, Name: 'Other', ServerName: 'Server' }];
    await panel.updateComplete;
    expect(select.value).to.equal('');
  });

  it('displays configuration notice when database access management is disabled', async () => {
    const panel = await fixture<DatabaseAccessPanel>(
      html`<database-access-panel
        .envId=${10}
        .readonly=${false}
        .databases=${[{ Id: 1, Name: 'App', ServerName: 'Server' }]}
      ></database-access-panel>`
    );
    mocks.status.mockReturnValue(
      of({
        Enabled: false,
        ExecutionEnabled: false,
        Providers: [],
        Limitations: []
      })
    );
    panel.shadowRoot!.querySelector<ComboBox>('#database')!.value = '1';
    await panel.updateComplete;
    click(panel, 'Load database access');
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain(
        'Database access management is disabled.'
      )
    );
    expect(mocks.get.mock.calls.length).to.equal(0);
    expect(
      panel.shadowRoot!.querySelector('.notice')!.textContent
    ).to.contain('DatabaseAccess:Enabled');
  });

  it('renders only modern desired-state and audit sections with no migration UI', async () => {
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain(
        'No managed principals yet'
      )
    );
    expect(panel.shadowRoot!.querySelector('#legacy-import')).to.equal(null);
    expect(
      panel.shadowRoot!.querySelector('#desired-state-editor')
    ).not.to.equal(null);
    const text = panel.shadowRoot!.textContent!;
    expect(text).not.to.contain('Load legacy access');
    expect(text).not.to.contain('Preview legacy import');
    expect(text).to.contain('Managed via DOrc deployments.');
  });

  it('loads AD display data live, marks unresolved identities, and ignores stale responses', async () => {
    const resolveGroup = new Subject<DatabaseAccessDirectoryIdentity>();
    const resolveStaleError = new Subject<DatabaseAccessDirectoryIdentity>();
    mocks.get.mockReturnValue(
      of({
        ...empty,
        Principals: [
          {
            Name: 'sql_only',
            Kind: 'Native',
            LoginName: null,
            Present: true
          },
          {
            Name: 'no_sid_user',
            Kind: 'DirectoryUser',
            DirectoryId: null,
            Present: true
          },
          {
            Name: 'resolved_user',
            Kind: 'DirectoryUser',
            DirectoryId: 'sid-ok',
            Present: true
          },
          {
            Name: 'missing_user',
            Kind: 'DirectoryUser',
            DirectoryId: 'sid-err',
            Present: true
          },
          {
            Name: 'group_pending',
            Kind: 'DirectoryGroup',
            DirectoryId: 'sid-group',
            Present: true
          },
          {
            Name: 'after_pending',
            Kind: 'DirectoryUser',
            DirectoryId: 'sid-after',
            Present: true
          }
        ]
      })
    );
    mocks.resolve.mockImplementation(({ id }: { id: string }) => {
      if (id === 'sid-ok') return of(directoryUser);
      if (id === 'sid-err')
        return throwError(() => new Error('Directory unavailable'));
      if (id === 'sid-group') return resolveGroup;
      return resolveStaleError;
    });

    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain('Joe Bloggs (AD user)')
    );
    expect(panel.shadowRoot!.textContent).to.contain(
      'Unresolved: Directory unavailable (AD user)'
    );
    // Before sid-group resolves, table falls back to raw DirectoryId and marks AD group
    expect(panel.shadowRoot!.textContent).to.contain('sid-group (AD group)');
    expect(panel.shadowRoot!.textContent).to.contain('(AD user)');
    expect(panel.shadowRoot!.textContent).to.contain('SQL login');

    // Switch environment while sid-group is still in-flight -> stale resolve completion is ignored
    panel.envId = 20;
    await panel.updateComplete;
    resolveGroup.next(directoryGroup);
    resolveGroup.complete();
    await new Promise(resolve => setTimeout(resolve, 0));

    // Also verify stale error in resolve catch block is ignored when disconnected
    mocks.get.mockReturnValue(
      of({
        ...empty,
        Principals: [
          {
            Name: 'err_pending',
            Kind: 'DirectoryUser',
            DirectoryId: 'sid-stale-err',
            Present: true
          }
        ]
      })
    );
    const beforeResolve = mocks.resolve.mock.calls.length;
    const panel2 = await fixture<DatabaseAccessPanel>(
      html`<database-access-panel
        .envId=${10}
        .readonly=${false}
        .databases=${[{ Id: 1, Name: 'App', ServerName: 'Server' }]}
        .selectedDatabaseId=${1}
      ></database-access-panel>`
    );
    await vi.waitFor(() =>
      expect(mocks.resolve.mock.calls.length).to.be.greaterThan(beforeResolve)
    );
    panel2.remove();
    resolveStaleError.error(new Error('Stale directory failure'));
    await new Promise(resolve => setTimeout(resolve, 0));
  });

  it('surfaces dependency errors and ignores stale status or get responses', async () => {
    mocks.get.mockReturnValue(
      throwError(() => new Error('Provider unavailable'))
    );
    const panel = await load();
    await vi.waitFor(() =>
      expect(
        panel.shadowRoot!.querySelector('[role="alert"]')?.textContent
      ).to.contain('Provider unavailable')
    );

    // Stale status response after environment switch
    const pendingStatus = new Subject<{
      Enabled: boolean;
      ExecutionEnabled: boolean;
      Providers: string[];
      Limitations: string[];
    }>();
    mocks.status.mockReturnValueOnce(pendingStatus);
    panel.shadowRoot!.querySelector<ComboBox>('#database')!.value = '1';
    await panel.updateComplete;
    click(panel, 'Load database access');
    panel.envId = 30;
    await panel.updateComplete;
    pendingStatus.error(new Error('Stale status error'));
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(panel.shadowRoot!.querySelector('[role="alert"]')).to.equal(null);

    // Stale get response after environment switch
    const pendingGet = new Subject<DatabaseAccessState>();
    mocks.get.mockReturnValueOnce(pendingGet);
    const panel2 = await fixture<DatabaseAccessPanel>(
      html`<database-access-panel
        .envId=${10}
        .readonly=${false}
        .databases=${[{ Id: 1, Name: 'App', ServerName: 'Server' }]}
        .selectedDatabaseId=${1}
      ></database-access-panel>`
    );
    await vi.waitFor(() => expect(mocks.get.mock.calls.length).to.equal(2));
    panel2.envId = 40;
    await panel2.updateComplete;
    pendingGet.next(empty);
    pendingGet.complete();
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(normalizedText(panel2.shadowRoot)).not.to.contain(
      'No managed principals yet'
    );
  });

  it('grants an existing SQL login a role, caches logins, and handles empty or failed login queries', async () => {
    mocks.get.mockReturnValue(
      of({
        ...empty,
        Principals: [
          {
            Name: 'orphan_sql',
            Kind: 'Native',
            LoginName: null,
            Present: true
          }
        ]
      })
    );
    mocks.logins.mockReturnValue(of(['app_login', 'reporting']));
    mocks.save.mockImplementation(
      ({ databaseAccessState }: { databaseAccessState: DatabaseAccessState }) =>
        of({ ...databaseAccessState, Revision: 1 })
    );
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.querySelector('#current-access')).not.to.equal(
        null
      )
    );

    // Dispatching empty/unchanged radio value is a no-op
    const radioGroup =
      panel.shadowRoot!.querySelector<RadioGroup>('vaadin-radio-group')!;
    radioGroup.dispatchEvent(
      new CustomEvent('value-changed', { detail: { value: 'directory' } })
    );

    radio(panel, 'sql');
    await vi.waitFor(() => expect(mocks.logins.mock.calls.length).to.equal(1));

    // Toggling back to directory and sql reuses cached logins without re-fetching
    radio(panel, 'directory');
    await panel.updateComplete;
    radio(panel, 'sql');
    await panel.updateComplete;
    expect(mocks.logins.mock.calls.length).to.equal(1);

    // Missing SQL login validation
    click(panel, 'Grant access');
    await panel.updateComplete;
    expect(
      panel.shadowRoot!.querySelector('#grant-error')!.textContent
    ).to.contain('Choose an existing SQL login.');

    const login = panel.shadowRoot!.querySelector<ComboBox>('#grant-login')!;
    expect(login.items).to.deep.equal(['app_login', 'reporting']);
    login.value = 'app_login';
    // Dispatching duplicate value is a no-op
    login.dispatchEvent(
      new CustomEvent('value-changed', { detail: { value: 'app_login' } })
    );
    await panel.updateComplete;
    expect(userName(panel).value).to.equal('app_login');

    // Cancel clears the selected login
    click(panel, 'Cancel');
    await panel.updateComplete;
    expect(panel.shadowRoot!.querySelector('#grant-search')).not.to.equal(null);

    // Re-select SQL login and grant access
    radio(panel, 'sql');
    await panel.updateComplete;
    panel.shadowRoot!.querySelector<ComboBox>('#grant-login')!.value =
      'app_login';
    await panel.updateComplete;
    role(panel, 'db_datareader').checked = true;
    await panel.updateComplete;
    click(panel, 'Grant access');
    await vi.waitFor(() => expect(mocks.save.mock.calls.length).to.equal(1));
    await settled(panel);

    const saved = mocks.save.mock.calls[0][0].databaseAccessState;
    expect(saved.Principals).to.deep.include({
      Name: 'app_login',
      Kind: 'Native',
      LoginName: 'app_login',
      DirectoryId: null,
      Present: true
    });
    expect(saved.Roles).to.deep.equal([
      { Name: 'db_datareader', Managed: false, Present: true }
    ]);
    expect(saved.Memberships).to.deep.equal([
      { Principal: 'app_login', Role: 'db_datareader', Present: true }
    ]);

    // Selecting an already-configured SQL login shows replacement hint and pre-selects its role
    radio(panel, 'sql');
    await panel.updateComplete;
    panel.shadowRoot!.querySelector<ComboBox>('#grant-login')!.value =
      'app_login';
    await panel.updateComplete;
    expect(normalizedText(panel.shadowRoot)).to.contain(
      'This account already has access as app_login'
    );
    expect(role(panel, 'db_datareader').checked).to.equal(true);
  });

  it('handles empty SQL login list, server errors, and stale SQL login responses', async () => {
    mocks.logins.mockReturnValueOnce(of([]));
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.querySelector('#grant-search')).not.to.equal(
        null
      )
    );
    radio(panel, 'sql');
    await vi.waitFor(() =>
      expect(
        panel.shadowRoot!.querySelector('#sql-logins-message')?.textContent
      ).to.contain('No enabled SQL logins were found on this server')
    );

    mocks.logins.mockReturnValueOnce(
      throwError(() => new Error('Login failed for user'))
    );
    const panel2 = await load();
    await vi.waitFor(() =>
      expect(panel2.shadowRoot!.querySelector('#grant-search')).not.to.equal(
        null
      )
    );
    radio(panel2, 'sql');
    await vi.waitFor(() =>
      expect(
        panel2.shadowRoot!.querySelector('#sql-logins-message')?.textContent
      ).to.contain('You can still type the name of an existing login')
    );

    // Stale success & error responses when disconnected during loadSqlLogins
    const pendingSuccess = new Subject<string[]>();
    mocks.logins.mockReturnValueOnce(pendingSuccess);
    const panel3 = await load();
    await vi.waitFor(() =>
      expect(panel3.shadowRoot!.querySelector('#grant-search')).not.to.equal(
        null
      )
    );
    radio(panel3, 'sql');
    panel3.remove();
    pendingSuccess.next(['late_login']);
    pendingSuccess.complete();

    const pendingErr = new Subject<string[]>();
    mocks.logins.mockReturnValueOnce(pendingErr);
    const panel4 = await load();
    await vi.waitFor(() =>
      expect(panel4.shadowRoot!.querySelector('#grant-search')).not.to.equal(
        null
      )
    );
    radio(panel4, 'sql');
    panel4.remove();
    pendingErr.error(new Error('late failure'));
    await new Promise(resolve => setTimeout(resolve, 0));
  });

  it('searches AD via Enter or button, supports multi-result selection, manual user-name overrides, and validations', async () => {
    mocks.get.mockReturnValue(
      of({
        ...empty,
        Principals: [
          {
            Name: 'existing_name',
            Kind: 'Native',
            LoginName: 'other_login',
            Present: true
          }
        ]
      })
    );
    mocks.save.mockImplementation(
      ({ databaseAccessState }: { databaseAccessState: DatabaseAccessState }) =>
        of({ ...databaseAccessState, Revision: 1 })
    );
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.querySelector('#grant-search')).not.to.equal(
        null
      )
    );

    // Short search validation & non-Enter keydown ignored
    const searchField =
      panel.shadowRoot!.querySelector<TextField>('#grant-search')!;
    searchField.value = 'ab';
    searchField.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    searchField.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await panel.updateComplete;
    expect(
      panel.shadowRoot!.querySelector('#grant-error')!.textContent
    ).to.contain('Enter at least 3 characters to search AD.');

    // Zero AD matches
    mocks.directory.mockReturnValueOnce(of([]));
    searchField.value = 'nobody';
    await panel.updateComplete;
    click(panel, 'Search');
    await settled(panel);
    expect(
      panel.shadowRoot!.querySelector('#grant-error')!.textContent
    ).to.contain("No AD users or groups match 'nobody'.");

    // Missing AD selection error on Grant access
    click(panel, 'Grant access');
    await panel.updateComplete;
    expect(
      panel.shadowRoot!.querySelector('#grant-error')!.textContent
    ).to.contain('Search for and choose an AD user or group.');

    // Single AD group without AccountName opens Advanced and validates missing/invalid/duplicate names
    mocks.directory.mockReturnValueOnce(of([directoryGroup]));
    searchField.value = 'readers';
    await panel.updateComplete;
    searchField.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await settled(panel);
    expect(
      panel.shadowRoot!.querySelector<Details>('#grant-advanced')!.opened
    ).to.equal(true);
    click(panel, 'Grant access');
    await panel.updateComplete;
    expect(
      panel.shadowRoot!.querySelector('#grant-error')!.textContent
    ).to.contain('Enter a database user name under Advanced');

    // Control character / >128 char validation & duplicate value-changed guard
    const nameField = userName(panel);
    nameField.value = 'bad\u0007name';
    nameField.dispatchEvent(
      new CustomEvent('value-changed', { detail: { value: 'bad\u0007name' } })
    );
    await panel.updateComplete;
    click(panel, 'Grant access');
    await panel.updateComplete;
    expect(
      panel.shadowRoot!.querySelector('#grant-error')!.textContent
    ).to.contain('1-128 characters with no control characters');

    // Duplicate principal name validation
    nameField.value = 'EXISTING_NAME';
    await panel.updateComplete;
    click(panel, 'Grant access');
    await panel.updateComplete;
    expect(
      panel.shadowRoot!.querySelector('#grant-error')!.textContent
    ).to.contain("already exists for a different account");

    // Multiple AD results keep manual userName when grantUserNameEdited is true, then save DirectoryGroup
    mocks.directory.mockReturnValueOnce(of([directoryGroup, directoryUser]));
    nameField.value = 'custom_readers';
    searchField.value = 'dep';
    await panel.updateComplete;
    click(panel, 'Search');
    await settled(panel);
    expect(
      panel.shadowRoot!.querySelector<ComboBox>('#grant-identity')!.items
    ).to.have.length(2);
    const identityCombo =
      panel.shadowRoot!.querySelector<ComboBox>('#grant-identity')!;
    identityCombo.value = directoryGroup.Id;
    identityCombo.dispatchEvent(
      new CustomEvent('value-changed', { detail: { value: directoryGroup.Id } })
    );
    await panel.updateComplete;
    // Because userName was manually edited, switching identity keeps 'custom_readers'
    expect(userName(panel).value).to.equal('custom_readers');

    role(panel, 'db_datareader').checked = true;
    await panel.updateComplete;
    click(panel, 'Grant access');
    await vi.waitFor(() => expect(mocks.save.mock.calls.length).to.equal(1));
    await settled(panel);
    expect(
      mocks.save.mock.calls[0][0].databaseAccessState.Principals
    ).to.deep.include({
      Name: 'custom_readers',
      Kind: 'DirectoryGroup',
      LoginName: null,
      DirectoryId: directoryGroup.Id,
      Present: true
    });

    // Now search for single AD user -> auto-fills DOMAIN\account and saves DirectoryUser; also verify failed save keeps form state
    mocks.directory.mockReturnValueOnce(of([directoryUser]));
    mocks.save.mockReturnValueOnce(throwError(() => new Error('Save rejected')));
    panel.shadowRoot!.querySelector<TextField>('#grant-search')!.value =
      'bloggs';
    await panel.updateComplete;
    click(panel, 'Search');
    await settled(panel);
    expect(userName(panel).value).to.equal('CORP\\jbloggs');
    click(panel, 'Grant access');
    await vi.waitFor(() => expect(mocks.save.mock.calls.length).to.equal(2));
    await settled(panel);
    expect(
      panel.shadowRoot!.querySelector('[role="alert"]')?.textContent
    ).to.contain('Save rejected');
    expect(userName(panel).value).to.equal('CORP\\jbloggs');

    mocks.save.mockImplementation(
      ({ databaseAccessState }: { databaseAccessState: DatabaseAccessState }) =>
        of({ ...databaseAccessState, Revision: 2 })
    );
    click(panel, 'Grant access');
    await vi.waitFor(() => expect(mocks.save.mock.calls.length).to.equal(3));
    await settled(panel);
    expect(
      mocks.save.mock.calls[2][0].databaseAccessState.Principals
    ).to.deep.include({
      Name: 'CORP\\jbloggs',
      Kind: 'DirectoryUser',
      LoginName: null,
      DirectoryId: directoryUser.Id,
      Present: true
    });

    // Selecting an already-saved AD identity pre-populates existing principal
    mocks.directory.mockReturnValueOnce(of([directoryUser]));
    panel.shadowRoot!.querySelector<TextField>('#grant-search')!.value =
      'bloggs';
    await panel.updateComplete;
    click(panel, 'Search');
    await settled(panel);
    expect(normalizedText(panel.shadowRoot)).to.contain(
      'This account already has access as CORP\\jbloggs'
    );
  });

  it('manages db_owner grants only when CanApproveDbOwner is true and prompts before adding db_owner', async () => {
    mocks.logins.mockReturnValue(of(['owner_login']));
    mocks.save.mockImplementation(
      ({ databaseAccessState }: { databaseAccessState: DatabaseAccessState }) =>
        of({ ...databaseAccessState, Revision: 1 })
    );
    const panel = await load(true);
    await vi.waitFor(() =>
      expect(role(panel, 'db_owner')).not.to.equal(undefined)
    );

    radio(panel, 'sql');
    await vi.waitFor(() => expect(mocks.logins.mock.calls.length).to.equal(1));
    panel.shadowRoot!.querySelector<ComboBox>('#grant-login')!.value =
      'owner_login';
    role(panel, 'db_owner').checked = true;
    await panel.updateComplete;

    // Declining confirmPrompt cancels the save
    mocks.confirm.mockResolvedValueOnce(false);
    click(panel, 'Grant access');
    await vi.waitFor(() => expect(mocks.confirm.mock.calls.length).to.equal(1));
    await settled(panel);
    expect(mocks.save.mock.calls.length).to.equal(0);

    // Accepting confirmPrompt saves db_owner membership
    mocks.confirm.mockResolvedValueOnce(true);
    click(panel, 'Grant access');
    await vi.waitFor(() => expect(mocks.save.mock.calls.length).to.equal(1));
    await settled(panel);
    expect(mocks.confirm.mock.calls[1][0]).to.contain(
      "Grant db_owner membership to 'owner_login'?"
    );
    expect(
      mocks.save.mock.calls[0][0].databaseAccessState.Memberships
    ).to.deep.equal([
      { Principal: 'owner_login', Role: 'db_owner', Present: true }
    ]);

    // Editing an account that already has db_owner does NOT prompt again when adding another role
    click(panel, 'Edit');
    await panel.updateComplete;
    expect(role(panel, 'db_owner').checked).to.equal(true);
    role(panel, 'db_datareader').checked = true;
    await panel.updateComplete;
    click(panel, 'Save access');
    await vi.waitFor(() => expect(mocks.save.mock.calls.length).to.equal(2));
    await settled(panel);
    expect(mocks.confirm.mock.calls.length).to.equal(2);

    // Disconnecting while confirmPrompt is open aborts safely after await
    let resolveConfirm!: (value: boolean) => void;
    mocks.confirm.mockImplementationOnce(
      () =>
        new Promise<boolean>(resolve => {
          resolveConfirm = resolve;
        })
    );
    radio(panel, 'sql');
    await panel.updateComplete;
    panel.shadowRoot!.querySelector<ComboBox>('#grant-login')!.value =
      'second_owner';
    role(panel, 'db_owner').checked = true;
    await panel.updateComplete;
    click(panel, 'Grant access');
    await vi.waitFor(() => expect(mocks.confirm.mock.calls.length).to.equal(3));
    panel.remove();
    resolveConfirm(true);
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(mocks.save.mock.calls.length).to.equal(2);
  });

  it('edits principals (SQL, AD user, AD group), preserves unmanageable db_owner, revokes roles, and removes/restores principals', async () => {
    const assigned: DatabaseAccessState = {
      ...empty,
      Principals: [
        { Name: 'app', Kind: 'Native', LoginName: null, Present: true },
        {
          Name: 'CORP\\grp',
          Kind: 'DirectoryGroup',
          DirectoryId: 'sid-grp',
          Present: true
        }
      ],
      Roles: [
        { Name: 'db_datareader', Managed: false, Present: true },
        { Name: 'db_datawriter', Managed: false, Present: false },
        { Name: 'db_owner', Managed: false, Present: true }
      ],
      Memberships: [
        { Principal: 'app', Role: 'db_datareader', Present: true },
        { Principal: 'app', Role: 'db_owner', Present: true }
      ]
    };
    mocks.get.mockReturnValue(of(structuredClone(assigned)));
    mocks.resolve.mockReturnValue(
      throwError(() => new Error('Directory offline'))
    );
    mocks.save.mockImplementation(
      ({ databaseAccessState }: { databaseAccessState: DatabaseAccessState }) =>
        of({ ...databaseAccessState, Revision: 1 })
    );

    const panel = await load(false);
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.querySelector('#current-access')).not.to.equal(
        null
      )
    );
    // Without CanApproveDbOwner, db_owner is hidden from checkboxes
    expect(
      panel.shadowRoot!.querySelectorAll('vaadin-checkbox[label="db_owner"]')
    ).to.have.length(0);

    // Edit AD group first (tests editAccess with DirectoryGroup & fallback display name when directoryNames not yet set or resolved)
    const editButtons = [
      ...panel.shadowRoot!.querySelectorAll('#current-access vaadin-button')
    ].filter(b => b.textContent?.trim() === 'Edit');
    (editButtons[1] as HTMLElement).click();
    await panel.updateComplete;
    expect(
      panel.shadowRoot!.querySelector('#grant-access legend')!.textContent
    ).to.contain('Edit access for CORP\\grp');

    // Also test editAccess when directoryNames lacks the DirectoryId key entirely
    (panel as unknown as { directoryNames: Record<string, string> }).directoryNames =
      {};
    (editButtons[1] as HTMLElement).click();
    await panel.updateComplete;

    // Edit Native SQL user (LoginName: null fallback) and replace roles while preserving db_owner
    (editButtons[0] as HTMLElement).click();
    await panel.updateComplete;
    expect(role(panel, 'db_datareader').checked).to.equal(true);
    expect(userName(panel).disabled).to.equal(true);
    role(panel, 'db_datareader').checked = false;
    role(panel, 'db_datawriter').checked = true;
    await panel.updateComplete;
    click(panel, 'Save access');
    await vi.waitFor(() => expect(mocks.save.mock.calls.length).to.equal(1));
    await settled(panel);

    const saved = mocks.save.mock.calls[0][0].databaseAccessState;
    expect(saved.Roles.find((r: { Name: string }) => r.Name === 'db_datawriter').Present).to.equal(true);
    expect(saved.Memberships).to.deep.equal([
      { Principal: 'app', Role: 'db_datareader', Present: false },
      { Principal: 'app', Role: 'db_owner', Present: true },
      { Principal: 'app', Role: 'db_datawriter', Present: true }
    ]);

    // Revoke chip action
    (
      panel.shadowRoot!.querySelector(
        '[aria-label="Revoke db_datawriter from app"]'
      ) as HTMLElement
    ).click();
    await vi.waitFor(() => expect(mocks.save.mock.calls.length).to.equal(2));
    await settled(panel);

    // Remove principal marks principal and its memberships Present=false, then Restore marks principal Present=true
    const removeButtons = () =>
      [
        ...panel.shadowRoot!.querySelectorAll('#current-access vaadin-button')
      ].filter(b => ['Remove', 'Restore'].includes(b.textContent?.trim() ?? ''));
    (removeButtons()[0] as HTMLElement).click();
    await vi.waitFor(() => expect(mocks.save.mock.calls.length).to.equal(3));
    await settled(panel);
    expect(
      mocks.save.mock.calls[2][0].databaseAccessState.Principals[0].Present
    ).to.equal(false);
    expect(
      mocks.save.mock.calls[2][0].databaseAccessState.Memberships.every(
        (m: { Present: boolean }) => !m.Present
      )
    ).to.equal(true);

    // Editing a removed principal and saving marks principal.Present = true again
    const editRemoved = [
      ...panel.shadowRoot!.querySelectorAll('#current-access vaadin-button')
    ].find(b => b.textContent?.trim() === 'Edit') as HTMLElement;
    editRemoved.click();
    await panel.updateComplete;
    role(panel, 'db_datareader').checked = true;
    await panel.updateComplete;
    click(panel, 'Save access');
    await vi.waitFor(() => expect(mocks.save.mock.calls.length).to.equal(4));
    await settled(panel);
    expect(
      mocks.save.mock.calls[3][0].databaseAccessState.Principals[0].Present
    ).to.equal(true);

    // Remove then click Restore directly
    (removeButtons()[0] as HTMLElement).click();
    await vi.waitFor(() => expect(mocks.save.mock.calls.length).to.equal(5));
    await settled(panel);
    (removeButtons()[0] as HTMLElement).click();
    await vi.waitFor(() => expect(mocks.save.mock.calls.length).to.equal(6));
    await settled(panel);
    expect(
      mocks.save.mock.calls[5][0].databaseAccessState.Principals[0].Present
    ).to.equal(true);
  });

  it('validates, creates, removes, and restores custom roles and exposes active custom roles as grant options', async () => {
    mocks.get.mockReturnValue(
      of({
        ...empty,
        Roles: [
          { Name: 'public', Managed: false, Present: true },
          { Name: 'db_datareader', Managed: false, Present: true },
          { Name: 'legacy_removed', Managed: true, Present: false }
        ],
        Memberships: [
          { Principal: 'app', Role: 'app_reader', Present: true }
        ]
      })
    );
    mocks.save.mockImplementation(
      ({ databaseAccessState }: { databaseAccessState: DatabaseAccessState }) =>
        of({ ...databaseAccessState, Revision: 1 })
    );
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.querySelector('#custom-roles')).not.to.equal(
        null
      )
    );
    const name = [
      ...panel.shadowRoot!.querySelectorAll<TextField>('vaadin-text-field')
    ].find(f => f.label === 'Role name')!;

    // Empty name
    click(panel, 'Create role');
    await panel.updateComplete;
    expect(
      panel.shadowRoot!.querySelector('#custom-roles')!.textContent
    ).to.contain('Enter a role name.');

    // >128 chars
    name.value = 'r'.repeat(129);
    await panel.updateComplete;
    click(panel, 'Create role');
    await panel.updateComplete;
    expect(
      panel.shadowRoot!.querySelector('#custom-roles')!.textContent
    ).to.contain('Role names must be 1-128 characters.');

    // Reserved db_ prefix
    name.value = 'db_custom';
    await panel.updateComplete;
    click(panel, 'Create role');
    await panel.updateComplete;
    expect(
      panel.shadowRoot!.querySelector('#custom-roles')!.textContent
    ).to.contain("cannot start with 'db_'");

    // Save error keeps customRoleName
    mocks.save.mockReturnValueOnce(throwError(() => new Error('Role save error')));
    name.value = 'app_reader';
    await panel.updateComplete;
    click(panel, 'Create role');
    await vi.waitFor(() => expect(mocks.save.mock.calls.length).to.equal(1));
    await settled(panel);
    expect(name.value).to.equal('app_reader');

    // Valid custom role creation clears input and adds role to grant checkboxes
    mocks.save.mockImplementation(
      ({ databaseAccessState }: { databaseAccessState: DatabaseAccessState }) =>
        of({ ...databaseAccessState, Revision: 2 })
    );
    click(panel, 'Create role');
    await vi.waitFor(() => expect(mocks.save.mock.calls.length).to.equal(2));
    await settled(panel);
    expect(name.value).to.equal('');
    expect(role(panel, 'app_reader')).not.to.equal(undefined);

    // Duplicate role validation
    name.value = 'APP_READER';
    await panel.updateComplete;
    click(panel, 'Create role');
    await panel.updateComplete;
    expect(
      panel.shadowRoot!.querySelector('#custom-roles')!.textContent
    ).to.contain("A role named 'app_reader' is already configured.");

    // Restore removed custom role, then remove active custom role (clearing its memberships)
    const customRoleButtons = () => [
      ...panel.shadowRoot!.querySelectorAll('#custom-roles ul vaadin-button')
    ];
    // First item is legacy_removed -> Restore
    (customRoleButtons()[0] as HTMLElement).click();
    await vi.waitFor(() => expect(mocks.save.mock.calls.length).to.equal(3));
    await settled(panel);
    expect(
      mocks.save.mock.calls[2][0].databaseAccessState.Roles.find(
        (r: { Name: string }) => r.Name === 'legacy_removed'
      ).Present
    ).to.equal(true);

    // Second item is app_reader -> Remove (also marks app_reader membership Present=false)
    (customRoleButtons()[1] as HTMLElement).click();
    await vi.waitFor(() => expect(mocks.save.mock.calls.length).to.equal(4));
    await settled(panel);
    const afterRemove = mocks.save.mock.calls[3][0].databaseAccessState;
    expect(
      afterRemove.Roles.find((r: { Name: string }) => r.Name === 'app_reader')
        .Present
    ).to.equal(false);
    expect(afterRemove.Memberships[0].Present).to.equal(false);
  });

  it('renders permissions view, audit view (empty and populated with refresh), and unsupported-provider notices', async () => {
    const auditEntries: DatabaseAccessAuditApiModel[] = [
      {
        Id: 1,
        CreatedUtc: '2026-04-17T10:00:00Z',
        Actor: 'CORP\\admin',
        Action: 'SaveDesiredState',
        Status: 'Succeeded',
        Detail: 'Added db_datareader'
      }
    ];
    mocks.audit.mockReturnValueOnce(of([])).mockReturnValueOnce(of(auditEntries));

    const auditPanel = await fixture<DatabaseAccessPanel>(
      html`<database-access-panel
        .envId=${10}
        .readonly=${false}
        .databases=${[{ Id: 1, Name: 'App', ServerName: 'Server' }]}
        .selectedDatabaseId=${1}
        .view=${'audit'}
      ></database-access-panel>`
    );
    await vi.waitFor(() => expect(mocks.audit.mock.calls.length).to.equal(1));
    await settled(auditPanel);
    expect(normalizedText(auditPanel.shadowRoot)).to.contain(
      'No audit entries yet.'
    );
    expect(auditPanel.shadowRoot!.querySelector('#grant-access')).to.equal(
      null
    );

    click(auditPanel, 'Refresh audit');
    await vi.waitFor(() => expect(mocks.audit.mock.calls.length).to.equal(2));
    await settled(auditPanel);
    expect(normalizedText(auditPanel.shadowRoot)).to.contain(
      'SaveDesiredState: Succeeded'
    );

    // Switch to permissions view
    auditPanel.view = 'permissions';
    await auditPanel.updateComplete;
    expect(
      auditPanel.shadowRoot!.querySelector('section#desired-state-editor')
    ).not.to.equal(null);
    expect(
      auditPanel.shadowRoot!.querySelector('#database-access-audit')
    ).to.equal(null);

    // Unsupported provider with configured Providers list and empty Providers list, in permissions and audit views
    mocks.status.mockReturnValueOnce(
      of({
        Enabled: true,
        ExecutionEnabled: true,
        Providers: ['sql-server'],
        Limitations: []
      })
    );
    mocks.get.mockReturnValueOnce(of({ ...empty, Provider: 'postgresql' }));
    const pgPanel = await fixture<DatabaseAccessPanel>(
      html`<database-access-panel
        .envId=${10}
        .readonly=${false}
        .databases=${[{ Id: 1, Name: 'analytics', ServerName: 'proj/pg' }]}
        .selectedDatabaseId=${1}
        .view=${'permissions'}
      ></database-access-panel>`
    );
    await vi.waitFor(() =>
      expect(normalizedText(pgPanel.shadowRoot)).to.contain(
        'Database access management is not yet supported for provider postgresql.'
      )
    );
    await settled(pgPanel);
    expect(normalizedText(pgPanel.shadowRoot)).to.contain(
      'Supported providers: sql-server.'
    );
    expect(pgPanel.shadowRoot!.querySelector('#grant-access')).to.equal(null);

    // Switch unsupported provider panel to audit view -> shows audit section beneath notice; also test empty Providers fallback 'none'
    mocks.status.mockReturnValueOnce(
      of({
        Enabled: true,
        ExecutionEnabled: false,
        Providers: [],
        Limitations: []
      })
    );
    mocks.get.mockReturnValueOnce(of({ ...empty, Provider: 'postgresql' }));
    mocks.audit.mockReturnValueOnce(of(auditEntries));
    pgPanel.view = 'audit';
    await pgPanel.updateComplete;
    click(pgPanel, 'Reload database access');
    await vi.waitFor(() =>
      expect(normalizedText(pgPanel.shadowRoot)).to.contain(
        'Supported providers: none.'
      )
    );
    expect(
      pgPanel.shadowRoot!.querySelector('#database-access-audit')
    ).not.to.equal(null);
  });

  it('disables all editor controls when read-only and keeps narrow panels horizontally scrollable', async () => {
    const panel = await load();
    await vi.waitFor(() =>
      expect(panel.shadowRoot!.textContent).to.contain(
        'No managed principals yet'
      )
    );

    // Narrow layout check
    panel.style.width = '320px';
    panel.shadowRoot!.querySelector<Details>('#desired-state-editor')!.opened =
      true;
    panel.shadowRoot!.querySelector<Details>('#grant-advanced')!.opened = true;
    const tableContainer = panel.shadowRoot!.querySelector('.scroll')!;
    await vi.waitFor(() =>
      expect(tableContainer.clientWidth).to.be.greaterThan(0)
    );
    expect(panel.scrollWidth).to.be.at.most(panel.clientWidth);
    expect(tableContainer.scrollWidth).to.be.greaterThan(
      tableContainer.clientWidth
    );

    // Read-only guard prevents saves even if invoked directly
    panel.readonly = true;
    await panel.updateComplete;
    const fields = panel.shadowRoot!.querySelectorAll(
      'fieldset vaadin-text-field, fieldset vaadin-combo-box, fieldset vaadin-checkbox'
    );
    expect(fields.length).to.be.greaterThan(0);
    for (const field of fields)
      expect(field.hasAttribute('disabled')).to.equal(true);
    const saved = await (
      panel as unknown as { save: (fn: () => void) => Promise<boolean> }
    ).save(() => {});
    expect(saved).to.equal(false);
    expect(mocks.save.mock.calls.length).to.equal(0);
  });
});
