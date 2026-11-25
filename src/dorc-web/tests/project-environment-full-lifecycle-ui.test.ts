import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { ComponentType } from '../src/apis/dorc-api';
import { dialogIn, expect, fixture, html, inDialog, settle } from './_helpers';

const lifecycleMocks = vi.hoisted(() => ({
  refDataGet: vi.fn(),
  refDataPut: vi.fn(),
  projectsGet: vi.fn(),
  projectEnvsGet: vi.fn(),
  requestBuildDefsGet: vi.fn(),
  requestComponentsGet: vi.fn(),
  requestPost: vi.fn(),
  requestStatusesGet: vi.fn(),
  resultStatusesGet: vi.fn(),
  resultStatusesLogGet: vi.fn(),
  requestAttemptsGet: vi.fn(),
  propertyValuesPagedGet: vi.fn(),
  scopedPropertyValuesPut: vi.fn(),
  propertyValuesScopeOptionsGet: vi.fn(),
  propertyValuesGet: vi.fn(),
  propertyValuesPost: vi.fn(),
  propertiesGet: vi.fn(),
  projectAuditPut: vi.fn(),
  propertyValuesAuditPut: vi.fn(),
  serverAuditPut: vi.fn(),
  databaseAuditPut: vi.fn(),
  hubConnection: {
    state: 'Disconnected',
    start: vi.fn(() => Promise.resolve()),
    stop: vi.fn(() => Promise.resolve()),
    invoke: vi.fn(() => Promise.resolve()),
    on: vi.fn(),
    off: vi.fn(),
    onclose: vi.fn(),
    onreconnected: vi.fn(),
    onreconnecting: vi.fn()
  },
  aceSetValue: vi.fn(),
  aceSetOptions: vi.fn(),
  aceDestroy: vi.fn()
}));

vi.mock('@vaadin/notification', () => ({
  Notification: { show: vi.fn() }
}));

vi.mock('ace-builds', () => ({
  edit: () => ({
    destroy: lifecycleMocks.aceDestroy,
    renderer: { attachToShadowRoot: vi.fn() },
    setTheme: vi.fn(),
    session: { setMode: vi.fn(), setUseWorker: vi.fn() },
    getSession: () => ({ setUseWorker: vi.fn(), setAnnotations: vi.fn() }),
    setReadOnly: vi.fn(),
    setHighlightActiveLine: vi.fn(),
    setOptions: lifecycleMocks.aceSetOptions,
    setValue: lifecycleMocks.aceSetValue,
    gotoLine: vi.fn(),
    clearSelection: vi.fn(),
    resize: vi.fn(),
    on: vi.fn(),
    getValue: () => ''
  })
}));

vi.mock('../src/services/ServerEvents', () => ({
  DeploymentHub: {
    getConnection: () => lifecycleMocks.hubConnection
  },
  getReceiverRegister: vi.fn(() => ({
    register: vi.fn(() => ({ dispose: vi.fn() }))
  })),
  getHubProxyFactory: vi.fn(() => ({
    createHubProxy: vi.fn(() => ({
      joinRequestGroup: vi.fn(() => Promise.resolve())
    }))
  }))
}));

vi.mock('../src/apis/dorc-api', async importOriginal => {
  const actual = await importOriginal<Record<string, unknown>>();
  return {
    ...actual,
    RefDataApi: class {
      refDataIdGet = lifecycleMocks.refDataGet;
      refDataPut = lifecycleMocks.refDataPut;
    },
    PowerShellVersionsApi: class {
      powerShellVersionsGet = () => of([{ Value: '5.1' }, { Value: '7.4' }]);
    },
    RefDataProjectsApi: class {
      refDataProjectsGet = lifecycleMocks.projectsGet;
      refDataProjectsProjectNameGet = () =>
        of({
          ProjectId: 701,
          ProjectName: `DOrc_Lifecycle_Proj_e2e701`,
          ArtefactsUrl: 'file://depapp01dv/DropFolder',
          ArtefactsSubPaths: '.'
        });
    },
    RefDataEnvironmentsApi: class {
      refDataEnvironmentsGet = () =>
        of([
          {
            EnvironmentId: 502,
            EnvironmentName: `DOrc_Lifecycle_ChildEnv_e2e701`,
            ParentId: 501,
            EnvironmentSecure: false,
            EnvironmentIsProd: false,
            UserEditable: true,
            Details: {
              EnvironmentOwner: 'test-owner',
              Description: 'Lifecycle Child Environment'
            }
          }
        ]);
    },
    RefDataEnvironmentsDetailsApi: class {
      refDataEnvironmentsDetailsIdGet = () =>
        of({
          EnvironmentId: 502,
          EnvironmentName: `DOrc_Lifecycle_ChildEnv_e2e701`,
          Builds: []
        });
    },
    RefDataUsersApi: class {
      refDataUsersGet = () => of([]);
    },
    RefDataRolesApi: class {
      refDataRolesGet = () => of(['Admin']);
    },
    RefDataProjectEnvironmentMappingsApi: class {
      refDataProjectEnvironmentMappingsGet = lifecycleMocks.projectEnvsGet;
    },
    DeploymentDefinitionsApi: class {
      deploymentDefinitionsHistoryGet = () => of([]);
      deploymentDefinitionsGet = () => of([]);
    },
    PropertiesApi: class {
      propertiesGet = lifecycleMocks.propertiesGet;
    },
    RefDataScopedPropertyValuesApi: class {
      refDataScopedPropertyValuesPut = lifecycleMocks.scopedPropertyValuesPut;
    },
    PropertyValuesApi: class {
      propertyValuesPagedGet = lifecycleMocks.propertyValuesPagedGet;
      propertyValuesScopeOptionsGet =
        lifecycleMocks.propertyValuesScopeOptionsGet;
      propertyValuesGet = lifecycleMocks.propertyValuesGet;
      propertyValuesPost = lifecycleMocks.propertyValuesPost;
    },
    RequestApi: class {
      requestBuildDefinitionsGet = lifecycleMocks.requestBuildDefsGet;
      requestBuildsGet = () => of([]);
      requestComponentsGet = lifecycleMocks.requestComponentsGet;
      requestPost = lifecycleMocks.requestPost;
      requestRequestIdAttemptsGet = lifecycleMocks.requestAttemptsGet;
    },
    RequestStatusesApi: class {
      requestStatusesGet = lifecycleMocks.requestStatusesGet;
    },
    ResultStatusesApi: class {
      resultStatusesGet = lifecycleMocks.resultStatusesGet;
      resultStatusesLogGet = lifecycleMocks.resultStatusesLogGet;
    },
    RefDataProjectAuditApi: class {
      refDataProjectAuditPut = lifecycleMocks.projectAuditPut;
    },
    PropertyValuesAuditApi: class {
      propertyValuesAuditPut = lifecycleMocks.propertyValuesAuditPut;
    },
    ServerAuditApi: class {
      serverAuditPut = lifecycleMocks.serverAuditPut;
    },
    DatabaseAuditApi: class {
      databaseAuditPut = lifecycleMocks.databaseAuditPut;
    },
    RefDataGroupsApi: class {
      refDataGroupsGet = () =>
        of([{ GroupId: 1, GroupName: 'DOMAIN\\EndurAdmins' }]);
    },
    RefDataDaemonsApi: class {
      refDataDaemonsGet = () => of([]);
    }
  };
});

await import('../src/pages/page-environment');
await import('../src/components/attached-servers');
await import('../src/components/attached-databases');
await import('../src/pages/page-project-envs');
await import('../src/pages/page-project-ref-data');
await import('../src/components/environment-tabs/env-variables');
await import('../src/pages/page-deploy');
await import('../src/components/deploy/deploy-env');
await import('../src/pages/page-monitor-result');
await import('../src/components/component-deployment-results');
await import('../src/components/log-dialog');
await import('../src/pages/page-projects-audit');
await import('../src/pages/page-variables-audit');
await import('../src/pages/page-servers-audit');
await import('../src/pages/page-databases-audit');

describe('Project & Environment Full Lifecycle UI E2E', () => {
  const suffix = 'e2e701';
  const projectName = `DOrc_Lifecycle_Proj_${suffix}`;
  const parentEnvName = `DOrc_Lifecycle_ParentEnv_${suffix}`;
  const childEnvName = `DOrc_Lifecycle_ChildEnv_${suffix}`;
  const serverName = `dorc-life-srv-${suffix}`;
  const databaseName = `dorc_life_db_${suffix}`;
  const componentName = `DOrc_Lifecycle_VerifyComponent_${suffix}`;
  const scriptRelativePath = `DOrc_Lifecycle\\Lifecycle-Verify-${suffix}.ps1`;
  const requestId = 9401;

  let consoleErrorSpy: ReturnType<typeof vi.spyOn>;
  let unhandledErrors: Error[] = [];
  const onWindowError = (event: ErrorEvent) => {
    if (event.error instanceof Error) {
      unhandledErrors.push(event.error);
    } else {
      unhandledErrors.push(new Error(String(event.message)));
    }
  };

  beforeEach(() => {
    vi.clearAllMocks();
    unhandledErrors = [];
    window.addEventListener('error', onWindowError);
    consoleErrorSpy = vi.spyOn(console, 'error');

    lifecycleMocks.projectsGet.mockReturnValue(
      of([
        {
          ProjectId: 701,
          ProjectName: projectName,
          ProjectDescription: 'Full lifecycle E2E test project',
          ArtefactsUrl: 'file://depapp01dv/DropFolder',
          ArtefactsSubPaths: '.'
        }
      ])
    );

    lifecycleMocks.projectEnvsGet.mockReturnValue(
      of({
        Items: [
          {
            EnvironmentId: 501,
            EnvironmentName: parentEnvName,
            EnvironmentSecure: false,
            EnvironmentIsProd: false,
            UserEditable: true,
            Details: {
              EnvironmentOwner: 'test-owner',
              Description: 'Lifecycle Parent Environment'
            }
          },
          {
            EnvironmentId: 502,
            EnvironmentName: childEnvName,
            ParentId: 501,
            EnvironmentSecure: false,
            EnvironmentIsProd: false,
            UserEditable: true,
            ParentEnvironment: {
              EnvironmentId: 501,
              EnvironmentName: parentEnvName
            },
            Details: {
              EnvironmentOwner: 'test-owner',
              Description: 'Lifecycle Child Environment'
            }
          }
        ]
      })
    );

    lifecycleMocks.refDataGet.mockReturnValue(
      of({
        Project: {
          ProjectId: 701,
          ProjectName: projectName,
          ProjectDescription: 'Full lifecycle E2E test project',
          ArtefactsUrl: 'file://depapp01dv/DropFolder',
          ArtefactsSubPaths: '.'
        },
        Revision: 'rev-sha256-initial-1',
        CanEdit: true,
        CanDeploy: true,
        Components: [
          {
            ComponentId: 801,
            ComponentName: componentName,
            ComponentType: ComponentType.PowerShell,
            ScriptPath: scriptRelativePath,
            PSVersion: '7.4',
            IsEnabled: true,
            NonProdOnly: true,
            StopOnFailure: true,
            Children: []
          }
        ]
      })
    );

    lifecycleMocks.refDataPut.mockImplementation(({ refDataApiModel }) =>
      of({
        ...refDataApiModel,
        Revision: 'rev-sha256-updated-2'
      })
    );

    lifecycleMocks.propertiesGet.mockReturnValue(
      of([
        { Id: 1, Name: `Lifecycle_Global_${suffix}`, Secure: false },
        { Id: 2, Name: `Lifecycle_ParentInherited_${suffix}`, Secure: false },
        { Id: 3, Name: `Lifecycle_ChildScoped_${suffix}`, Secure: false },
        { Id: 4, Name: `Lifecycle_Secret_${suffix}`, Secure: true },
        { Id: 5, Name: `Lifecycle_RequestOverride_${suffix}`, Secure: false }
      ])
    );

    lifecycleMocks.propertyValuesScopeOptionsGet.mockReturnValue(
      of([
        { ValueOption: childEnvName, SampleResolvedValue: childEnvName },
        { ValueOption: parentEnvName, SampleResolvedValue: parentEnvName }
      ])
    );
    lifecycleMocks.propertyValuesGet.mockReturnValue(of([]));
    lifecycleMocks.propertyValuesPost.mockReturnValue(
      of([{ Status: 'success' }])
    );
    lifecycleMocks.scopedPropertyValuesPut.mockReturnValue(
      of({
        CurrentPage: 1,
        TotalPages: 1,
        TotalItems: 3,
        Items: [
          {
            PropertyId: 2,
            Property: `Lifecycle_ParentInherited_${suffix}`,
            PropertyValueId: 301,
            PropertyValue: `ParentVal_${suffix}`,
            PropertyValueScope: parentEnvName,
            PropertyValueScopeId: 501,
            Secure: false,
            UserEditable: true
          },
          {
            PropertyId: 3,
            Property: `Lifecycle_ChildScoped_${suffix}`,
            PropertyValueId: 302,
            PropertyValue: `ChildVal_V2_${suffix}`,
            PropertyValueScope: childEnvName,
            PropertyValueScopeId: 502,
            Secure: false,
            UserEditable: true
          },
          {
            PropertyId: 4,
            Property: `Lifecycle_Secret_${suffix}`,
            PropertyValueId: 303,
            PropertyValue: '••••••••',
            PropertyValueScope: childEnvName,
            PropertyValueScopeId: 502,
            Secure: true,
            UserEditable: true
          }
        ]
      })
    );
    lifecycleMocks.propertyValuesPagedGet.mockReturnValue(
      of({
        CurrentPage: 1,
        TotalPages: 1,
        TotalItems: 3,
        Items: [
          {
            Id: 301,
            Value: `ParentVal_${suffix}`,
            PropertyValueFilter: parentEnvName,
            PropertyValueFilterId: 501,
            Secure: false,
            UserEditable: true,
            Property: {
              Id: 2,
              Name: `Lifecycle_ParentInherited_${suffix}`,
              Secure: false
            }
          },
          {
            Id: 302,
            Value: `ChildVal_V2_${suffix}`,
            PropertyValueFilter: childEnvName,
            PropertyValueFilterId: 502,
            Secure: false,
            UserEditable: true,
            Property: {
              Id: 3,
              Name: `Lifecycle_ChildScoped_${suffix}`,
              Secure: false
            }
          },
          {
            Id: 303,
            Value: '••••••••',
            PropertyValueFilter: childEnvName,
            PropertyValueFilterId: 502,
            Secure: true,
            UserEditable: true,
            Property: {
              Id: 4,
              Name: `Lifecycle_Secret_${suffix}`,
              Secure: true
            }
          }
        ]
      })
    );

    lifecycleMocks.requestBuildDefsGet.mockReturnValue(of([]));
    lifecycleMocks.requestComponentsGet.mockReturnValue(
      of([
        {
          Id: 801,
          Name: componentName,
          ParentId: 0,
          NumOfChildren: 0,
          IsEnabled: true
        }
      ])
    );
    lifecycleMocks.requestPost.mockReturnValue(of({ Id: requestId }));

    lifecycleMocks.requestStatusesGet.mockReturnValue(
      of({
        Id: requestId,
        Project: projectName,
        EnvironmentName: childEnvName,
        BuildNumber: `DOrc Lifecycle Build ${suffix}`,
        Components: componentName,
        Status: 'Pending',
        UserName: 'test-owner',
        UserEditable: true
      })
    );

    lifecycleMocks.resultStatusesGet.mockReturnValue(
      of([
        {
          Id: 601,
          RequestId: requestId,
          ComponentId: 801,
          Component: componentName,
          Status: 'Pending',
          BuildUri: 'file://depapp01dv/DropFolder/Build1'
        }
      ])
    );

    lifecycleMocks.requestAttemptsGet.mockReturnValue(of([]));
    lifecycleMocks.resultStatusesLogGet.mockReturnValue(
      of(
        [
          `2026-04-16 12:00:01 [INFO] Starting ${componentName} on ${childEnvName}`,
          `2026-04-16 12:00:02 [INFO] Decrypted secret verification: [REDACTED]`,
          `2026-04-16 12:00:03 [INFO] LIFECYCLE_SCRIPT_EXECUTION_VERIFIED_${suffix}`
        ].join('\n')
      )
    );

    lifecycleMocks.projectAuditPut.mockReturnValue(
      of({
        CurrentPage: 1,
        TotalPages: 1,
        TotalItems: 1,
        Items: [
          {
            Id: 11,
            ProjectId: 701,
            Username: 'DOMAIN\\test-owner',
            Date: '2026-04-16T12:00:00Z',
            Type: 'Update',
            Project: { ProjectId: 701, ProjectName: projectName },
            FromValue: JSON.stringify(
              { Project: { ProjectName: projectName }, Components: [] },
              null,
              2
            ),
            ToValue: JSON.stringify(
              {
                Project: { ProjectName: projectName },
                Components: [
                  { ComponentName: componentName, ScriptPath: scriptRelativePath }
                ]
              },
              null,
              2
            )
          }
        ]
      })
    );

    lifecycleMocks.propertyValuesAuditPut.mockReturnValue(
      of({
        CurrentPage: 1,
        TotalPages: 1,
        TotalItems: 2,
        Items: [
          {
            Id: 21,
            PropertyName: `Lifecycle_ChildScoped_${suffix}`,
            EnvironmentName: childEnvName,
            UpdatedBy: 'DOMAIN\\test-owner',
            UpdatedDate: '2026-04-16T12:01:00Z',
            Type: 'Insert',
            FromValue: '',
            ToValue: `ChildVal_V1_${suffix}`
          },
          {
            Id: 22,
            PropertyName: `Lifecycle_ChildScoped_${suffix}`,
            EnvironmentName: childEnvName,
            UpdatedBy: 'DOMAIN\\test-owner',
            UpdatedDate: '2026-04-16T12:02:00Z',
            Type: 'Update',
            FromValue: `ChildVal_V1_${suffix}`,
            ToValue: `ChildVal_V2_${suffix}`
          }
        ]
      })
    );

    lifecycleMocks.serverAuditPut.mockReturnValue(
      of({
        CurrentPage: 1,
        TotalPages: 1,
        TotalItems: 2,
        Items: [
          {
            Id: 31,
            ServerId: 901,
            ServerName: serverName,
            UpdatedBy: 'DOMAIN\\test-owner',
            UpdatedDate: '2026-04-16T12:03:00Z',
            Type: 'Create',
            EnvironmentNames: childEnvName,
            FromValue: '',
            ToValue: `{"Name":"${serverName}","Tags":["appserv","lifecycle-${suffix}"]}`
          },
          {
            Id: 32,
            ServerId: 901,
            ServerName: serverName,
            UpdatedBy: 'DOMAIN\\test-owner',
            UpdatedDate: '2026-04-16T12:04:00Z',
            Type: 'Update',
            EnvironmentNames: childEnvName,
            FromValue: `{"Name":"${serverName}","Tags":["appserv","lifecycle-${suffix}"]}`,
            ToValue: `{"Name":"${serverName}","Tags":["appserv","lifecycle-${suffix}","patched"]}`
          }
        ]
      })
    );

    lifecycleMocks.databaseAuditPut.mockReturnValue(
      of({
        CurrentPage: 1,
        TotalPages: 1,
        TotalItems: 2,
        Items: [
          {
            Id: 41,
            DatabaseId: 951,
            DatabaseName: databaseName,
            ServerName: serverName,
            UpdatedBy: 'DOMAIN\\test-owner',
            UpdatedDate: '2026-04-16T12:05:00Z',
            Type: 'Create',
            EnvironmentNames: childEnvName,
            FromValue: '',
            ToValue: `{"Name":"${databaseName}","Tags":"Endur;Report"}`
          },
          {
            Id: 42,
            DatabaseId: 951,
            DatabaseName: databaseName,
            ServerName: serverName,
            UpdatedBy: 'DOMAIN\\test-owner',
            UpdatedDate: '2026-04-16T12:06:00Z',
            Type: 'Update',
            EnvironmentNames: childEnvName,
            FromValue: `{"Name":"${databaseName}","Tags":"Endur;Report"}`,
            ToValue: `{"Name":"${databaseName}","Tags":"Endur;Report;Analytics"}`
          }
        ]
      })
    );
  });

  afterEach(() => {
    window.removeEventListener('error', onWindowError);
    expect(
      unhandledErrors,
      `Expected zero unhandled page errors, got: ${unhandledErrors.map(e => e.message).join('; ')}`
    ).to.have.length(0);
    expect(
      consoleErrorSpy.mock.calls,
      `Expected zero console.error calls during UI lifecycle execution, got: ${JSON.stringify(
        consoleErrorSpy.mock.calls.map((c: unknown[]) => ({
          raw: String(c[0])
        }))
      )}`
    ).to.have.length(0);
    consoleErrorSpy.mockRestore();
  });

  it('Phase 1: renders environment hierarchy parent badge and attached servers/databases with normalized tag chips', async () => {
    const pageEnv = await fixture<HTMLElement>(
      html`<page-environment></page-environment>`
    );
    pageEnv.dispatchEvent(
      new CustomEvent('environment-loaded', {
        detail: {
          environment: {
            EnvironmentId: 502,
            EnvironmentName: childEnvName,
            ParentId: 501,
            ParentEnvironment: {
              EnvironmentId: 501,
              EnvironmentName: parentEnvName
            }
          }
        },
        bubbles: true,
        composed: true
      })
    );
    await pageEnv.updateComplete;
    await settle();

    const headerTitle = pageEnv.shadowRoot?.querySelector('h2');
    expect(headerTitle?.textContent?.trim()).to.equal(childEnvName);
    const childBadge = pageEnv.shadowRoot?.querySelector(
      'vaadin-icon[icon="vaadin:child"]'
    );
    expect(childBadge, 'Child environment icon badge is rendered').to.not.equal(
      null
    );
    expect(childBadge?.getAttribute('title')).to.equal(
      `Child of ${parentEnvName}`
    );

    const serversWrapper = await fixture<HTMLDivElement>(html`
      <div style="width: 1100px; height: 320px; display: flex;">
        <attached-servers style="width: 1050px; height: 300px;"></attached-servers>
      </div>
    `);
    const serversEl = serversWrapper.querySelector('attached-servers') as HTMLElement & {
      servers: unknown[];
      readonly: boolean;
      _narrowScreen: boolean;
      updateComplete: Promise<unknown>;
    };
    serversEl._narrowScreen = false;
    serversEl.readonly = false;
    serversEl.servers = [
      {
        ServerId: 901,
        Name: serverName,
        OsName: 'Windows Server 2022',
        Tags: ['appserv', `lifecycle-${suffix}`, 'patched'],
        UserEditable: true
      }
    ];
    await serversEl.updateComplete;
    await settle(150);

    const serverTagButtons = Array.from(
      serversEl.shadowRoot?.querySelectorAll('vaadin-grid button.tag') ?? []
    ).map(btn => btn.textContent?.trim());
    expect(serverTagButtons).to.include.members([
      'appserv',
      `lifecycle-${suffix}`,
      'patched'
    ]);

    const databasesWrapper = await fixture<HTMLDivElement>(html`
      <div style="width: 1100px; height: 320px; display: flex;">
        <attached-databases style="width: 1050px; height: 300px;"></attached-databases>
      </div>
    `);
    const databasesEl = databasesWrapper.querySelector(
      'attached-databases'
    ) as HTMLElement & {
      databases: unknown[];
      readonly: boolean;
      _narrowScreen: boolean;
      updateComplete: Promise<unknown>;
    };
    databasesEl._narrowScreen = false;
    databasesEl.readonly = false;
    databasesEl.databases = [
      {
        Id: 951,
        Name: databaseName,
        ServerName: serverName,
        Type: 'SQL Server',
        Tags: 'Endur;Report;Analytics',
        AdGroup: 'DOMAIN\\EndurAdmins',
        UserEditable: true
      }
    ];
    await databasesEl.updateComplete;
    await settle(150);

    const dbTagButtons = Array.from(
      databasesEl.shadowRoot?.querySelectorAll('vaadin-grid button.tag') ?? []
    ).map(btn => btn.textContent?.trim());
    expect(dbTagButtons).to.include.members(['Endur', 'Report', 'Analytics']);
  });

  it('Phase 2: loads project environments, edits PowerShell component tree with Revision token, and surfaces 409 Conflict without data loss', async () => {
    history.replaceState(null, '', '/project-ref-data/701');
    const refDataPage = await fixture<
      HTMLElement & {
        projectId: number;
        document?: {
          Revision?: string;
          Components?: Array<{ ComponentName?: string; ScriptPath?: string }>;
        };
        error: string;
        load(): void;
        updateComplete: Promise<unknown>;
      }
    >(html`<page-project-ref-data></page-project-ref-data>`);
    await refDataPage.updateComplete;
    await settle(150);

    expect(refDataPage.document?.Revision).to.equal('rev-sha256-initial-1');
    expect(refDataPage.document?.Components?.[0]?.ComponentName).to.equal(
      componentName
    );
    expect(refDataPage.document?.Components?.[0]?.ScriptPath).to.equal(
      scriptRelativePath
    );

    const clickLabel = (label: string) => {
      const btn = [
        ...(refDataPage.shadowRoot?.querySelectorAll<HTMLElement>(
          'vaadin-button, button'
        ) ?? [])
      ].find(el => el.textContent?.trim() === label);
      expect(btn, `button ${label}`).to.not.equal(undefined);
      btn!.click();
    };

    const backendsDetails =
      refDataPage.shadowRoot!.querySelector<HTMLDetailsElement>('.backends')!;
    backendsDetails.open = true;
    await settle(60);
    clickLabel('Add backend');
    await settle(60);
    clickLabel('Review changes');
    await settle(60);
    clickLabel('Confirm save');
    await refDataPage.updateComplete;
    await settle(120);

    expect(lifecycleMocks.refDataPut.mock.calls).to.have.length(1);
    const savedPayload = (
      lifecycleMocks.refDataPut.mock.calls[0] as Array<{
        refDataApiModel: { Revision: string };
      }>
    )[0].refDataApiModel;
    expect(savedPayload.Revision).to.equal('rev-sha256-initial-1');
    expect(refDataPage.document?.Revision).to.equal('rev-sha256-updated-2');

    // Simulate a stale concurrent save returning 409 Conflict
    lifecycleMocks.refDataPut.mockReturnValueOnce(
      throwError(() => ({
        status: 409,
        response:
          'The project definition was modified by another user. Reload and reapply your changes.'
      }))
    );

    backendsDetails.open = true;
    await settle(60);
    clickLabel('Add backend');
    await settle(60);
    clickLabel('Review changes');
    await settle(60);
    clickLabel('Confirm save');
    await refDataPage.updateComplete;
    await settle(120);

    expect(refDataPage.error).to.contain('modified by another user');
    history.replaceState(null, '', '/');
  });

  it('Phase 3: renders environment variables grid, keeps secret values masked, and enters inline edit mode cleanly', async () => {
    history.replaceState(null, '', `/environment/${childEnvName}/variables`);
    const envVarsWrapper = await fixture<HTMLDivElement>(html`
      <div style="width: 1100px; height: 480px; display: flex;">
        <env-variables style="width: 1050px; height: 440px;"></env-variables>
      </div>
    `);
    const envVars = envVarsWrapper.querySelector('env-variables') as HTMLElement & {
      environmentName: string;
      environment: unknown;
      envLoaded: boolean;
      loading: boolean;
      _narrowScreen: boolean;
      grid?: { clearCache(): void };
      notifyEnvironmentReady(): void;
      updateComplete: Promise<unknown>;
    };
    envVars._narrowScreen = false;
    envVars.environmentName = childEnvName;
    envVars.environment = {
      EnvironmentId: 502,
      EnvironmentName: childEnvName,
      ParentId: 501,
      UserEditable: true
    };
    envVars.envLoaded = true;
    envVars.notifyEnvironmentReady();
    envVars.loading = false;
    await envVars.updateComplete;
    envVars.grid?.clearCache();
    await envVars.updateComplete;
    await settle(250);

    const gridText =
      envVars.shadowRoot?.querySelector('vaadin-grid')?.textContent ?? '';
    expect(gridText).to.contain(`Lifecycle_ParentInherited_${suffix}`);
    expect(gridText).to.contain(`Lifecycle_ChildScoped_${suffix}`);
    expect(gridText).to.contain(`Lifecycle_Secret_${suffix}`);
    expect(gridText).to.not.contain(`Sup3rS3cr3t_${suffix}!`);

    envVars.dispatchEvent(
      new CustomEvent('editing-started', {
        detail: { id: 302 },
        bubbles: true,
        composed: true
      })
    );
    await envVars.updateComplete;
    await settle(450);

    const controls = Array.from(
      envVars.shadowRoot
        ?.querySelector('vaadin-grid')
        ?.querySelectorAll('variable-value-controls') ?? []
    ) as Array<
      HTMLElement & {
        editing: boolean;
        value?: { Id: number; Value?: string };
      }
    >;
    const controlValues = controls.map(c => c.value?.Value);
    expect(controlValues).to.include(`ChildVal_V2_${suffix}`);
    expect(controlValues).to.include('••••••••');
    expect(controlValues).to.not.include(`Sup3rS3cr3t_${suffix}!`);

    const editedControl = controls.find(c => c.value?.Id === 302);
    expect(editedControl?.editing, 'Target variable row entered edit mode').to.equal(
      true
    );
    history.replaceState(null, '', '/');
  });

  it('Phase 4: selects project, environment, build, component, and inline property override in <deploy-env>, confirms dialog, and posts /Request', async () => {
    const deploy = await fixture<
      HTMLElement & {
        project: unknown;
        envName: string;
        selectedBuildId: string;
        selectedBuild: string;
        propertyOverrides: Array<{ PropertyName: string; PropertyValue: string }>;
        req: { requestDto: Record<string, unknown> };
        dialogOpened: boolean;
        updateComplete: Promise<unknown>;
      }
    >(html`<deploy-env></deploy-env>`);
    await deploy.updateComplete;
    await settle();

    deploy.project = {
      ProjectId: 701,
      ProjectName: projectName,
      ArtefactsUrl: 'file://depapp01dv/DropFolder',
      ArtefactsSubPaths: '.'
    };
    await deploy.updateComplete;
    deploy.envName = childEnvName;
    deploy.selectedBuildId = `file://depapp01dv/DropFolder/Build_${suffix}`;
    deploy.selectedBuild = `DOrc Lifecycle Build ${suffix}`;
    (deploy as unknown as { buildDef: string }).buildDef = 'FileShareBuild';
    deploy.propertyOverrides = [
      {
        PropertyName: `Lifecycle_RequestOverride_${suffix}`,
        PropertyValue: `OverriddenAtRequest_${suffix}`
      }
    ];
    (
      deploy.shadowRoot?.querySelector('hegs-tree') as HTMLElement & {
        getCheckedComponents(): Array<{ data: { name: string } }>;
      }
    ).getCheckedComponents = () => [{ data: { name: componentName } }];

    const deployButton = Array.from(
      deploy.shadowRoot?.querySelectorAll('vaadin-button') ?? []
    ).find(
      button =>
        button.textContent?.trim() === 'Deploy' &&
        button.closest('vaadin-confirm-dialog') === null
    ) as HTMLElement;
    deployButton.click();
    await deploy.updateComplete;
    await settle();

    const confirmDialog = deploy.shadowRoot?.querySelector(
      'vaadin-confirm-dialog#dialog'
    ) as HTMLElement & { opened: boolean };
    expect(confirmDialog.opened, 'Deploy confirm dialog opened').to.equal(true);
    expect(deploy.req).to.deep.equal({
      requestDto: {
        Project: projectName,
        Environment: childEnvName,
        BuildUrl: `file://depapp01dv/DropFolder/Build_${suffix}`,
        BuildText: 'FileShareBuild',
        BuildNum: `DOrc Lifecycle Build ${suffix}`,
        RequestProperties: [
          {
            PropertyName: `Lifecycle_RequestOverride_${suffix}`,
            PropertyValue: `OverriddenAtRequest_${suffix}`
          }
        ],
        Components: [componentName]
      }
    });

    confirmDialog.dispatchEvent(new CustomEvent('confirm'));
    await deploy.updateComplete;
    await settle();

    expect(lifecycleMocks.requestPost.mock.calls).to.have.length(1);
    const toast = deploy.shadowRoot?.querySelector(
      'successful-deploy-notification'
    ) as HTMLElement | null;
    expect(toast).to.not.equal(null);
    expect(toast?.getAttribute('requestedDeploymentId')).to.equal(
      String(requestId)
    );

    confirmDialog.opened = false;
    await settle();
    const innerNotification = toast?.shadowRoot?.querySelector(
      'vaadin-notification'
    ) as (HTMLElement & { opened: boolean }) | null;
    if (innerNotification) {
      innerNotification.opened = false;
    }
    await settle();
    toast?.remove();
  });

  it('Phase 5: updates <page-monitor-result> via SignalR Pending -> Running -> Completed, opens redacted component log dialog, and renders all 4 audit diff views', async () => {
    window.history.pushState({}, '', `/monitor-result/${requestId}`);
    const monitorPage = await fixture<
      HTMLElement & {
        deployRequest?: { Id?: number; Status?: string };
        resultItems?: Array<{ Id?: number; Status?: string }>;
        onDeploymentRequestStatusChanged(data: {
          requestId: number;
          status: string;
        }): Promise<void>;
        onDeploymentResultStatusChanged(data: {
          requestId: number;
          resultId: number;
          status: string;
        }): void;
        updateComplete: Promise<unknown>;
      }
    >(html`<page-monitor-result></page-monitor-result>`);
    await monitorPage.updateComplete;
    await settle(250);

    expect(monitorPage.deployRequest?.Id).to.equal(requestId);
    expect(monitorPage.deployRequest?.Status).to.equal('Pending');

    // SignalR broadcast: Pending -> Running
    lifecycleMocks.requestStatusesGet.mockReturnValueOnce(
      of({
        Id: requestId,
        Project: projectName,
        EnvironmentName: childEnvName,
        BuildNumber: `DOrc Lifecycle Build ${suffix}`,
        Components: componentName,
        Status: 'Running',
        UserName: 'test-owner',
        UserEditable: true
      })
    );
    lifecycleMocks.resultStatusesGet.mockReturnValueOnce(
      of([
        {
          Id: 601,
          RequestId: requestId,
          ComponentId: 801,
          Component: componentName,
          Status: 'Running',
          BuildUri: 'file://depapp01dv/DropFolder/Build1'
        }
      ])
    );
    await monitorPage.onDeploymentRequestStatusChanged({
      requestId,
      status: 'Running'
    });
    monitorPage.onDeploymentResultStatusChanged({
      requestId,
      resultId: 601,
      status: 'Running'
    });
    await monitorPage.updateComplete;
    await settle(150);

    expect(monitorPage.deployRequest?.Status).to.equal('Running');
    expect(monitorPage.resultItems?.[0]?.Status).to.equal('Running');

    // SignalR broadcast: Running -> Completed
    lifecycleMocks.requestStatusesGet.mockReturnValueOnce(
      of({
        Id: requestId,
        Project: projectName,
        EnvironmentName: childEnvName,
        BuildNumber: `DOrc Lifecycle Build ${suffix}`,
        Components: componentName,
        Status: 'Completed',
        UserName: 'test-owner',
        UserEditable: true
      })
    );
    lifecycleMocks.resultStatusesGet.mockReturnValueOnce(
      of([
        {
          Id: 601,
          RequestId: requestId,
          ComponentId: 801,
          Component: componentName,
          Status: 'Completed',
          BuildUri: 'file://depapp01dv/DropFolder/Build1'
        }
      ])
    );
    await monitorPage.onDeploymentRequestStatusChanged({
      requestId,
      status: 'Completed'
    });
    monitorPage.onDeploymentResultStatusChanged({
      requestId,
      resultId: 601,
      status: 'Completed'
    });
    await monitorPage.updateComplete;
    await settle(150);

    expect(monitorPage.deployRequest?.Status).to.equal('Completed');
    expect(monitorPage.resultItems?.[0]?.Status).to.equal('Completed');

    // Open component deployment log dialog and verify redacted log output
    const compResults = monitorPage.shadowRoot?.querySelector(
      'component-deployment-results'
    ) as
      | (HTMLElement & {
          selectedLog?: string;
          dialogOpened: boolean;
          updateComplete: Promise<unknown>;
        })
      | null;
    expect(compResults).to.not.equal(null);

    compResults!.dispatchEvent(
      new CustomEvent('open-result-log', {
        detail: {
          result: {
            Id: 601,
            RequestId: requestId,
            Component: componentName,
            Status: 'Completed'
          }
        },
        bubbles: true,
        composed: true
      })
    );
    await compResults!.updateComplete;
    await settle(250);

    expect(lifecycleMocks.resultStatusesLogGet.mock.calls).to.have.length(1);
    expect(compResults!.dialogOpened).to.equal(true);
    expect(compResults!.selectedLog).to.contain(
      `LIFECYCLE_SCRIPT_EXECUTION_VERIFIED_${suffix}`
    );
    expect(compResults!.selectedLog).to.contain('[REDACTED]');
    expect(compResults!.selectedLog).to.not.contain(`Sup3rS3cr3t_${suffix}!`);

    const logDialog = compResults!.shadowRoot?.querySelector(
      'log-dialog'
    ) as HTMLElement | null;
    const dialog = dialogIn(logDialog!);
    (inDialog(dialog, 'vaadin-button') as HTMLElement | null)?.click();
    await settle();

    // Verify all 4 Audit UI pages render their diff rows accurately
    const projectsAudit = await fixture<
      HTMLElement & { loading: boolean; updateComplete: Promise<unknown> }
    >(
      html`<page-projects-audit style="width: 1000px; height: 320px; display: block;"></page-projects-audit>`
    );
    projectsAudit.loading = false;
    await projectsAudit.updateComplete;
    (
      projectsAudit.shadowRoot?.querySelector('vaadin-grid') as
        | { clearCache(): void }
        | null
    )?.clearCache();
    await settle(200);
    expect(
      projectsAudit.shadowRoot?.querySelector('vaadin-grid')?.textContent ?? ''
    ).to.contain(projectName);

    const varsAudit = await fixture<
      HTMLElement & { loading: boolean; updateComplete: Promise<unknown> }
    >(
      html`<page-variables-audit style="width: 1000px; height: 320px; display: block;"></page-variables-audit>`
    );
    varsAudit.loading = false;
    await varsAudit.updateComplete;
    (
      varsAudit.shadowRoot?.querySelector('vaadin-grid') as
        | { clearCache(): void }
        | null
    )?.clearCache();
    await settle(200);
    const varsAuditText =
      varsAudit.shadowRoot?.querySelector('vaadin-grid')?.textContent ?? '';
    expect(varsAuditText).to.contain(`Lifecycle_ChildScoped_${suffix}`);
    expect(varsAuditText).to.contain(`ChildVal_V1_${suffix}`);
    expect(varsAuditText).to.contain(`ChildVal_V2_${suffix}`);

    const serversAudit = await fixture<
      HTMLElement & { loading: boolean; updateComplete: Promise<unknown> }
    >(
      html`<page-servers-audit style="width: 1000px; height: 320px; display: block;"></page-servers-audit>`
    );
    serversAudit.loading = false;
    await serversAudit.updateComplete;
    (
      serversAudit.shadowRoot?.querySelector('vaadin-grid') as
        | { clearCache(): void }
        | null
    )?.clearCache();
    await settle(200);
    const serversAuditText =
      serversAudit.shadowRoot?.querySelector('vaadin-grid')?.textContent ?? '';
    expect(serversAuditText).to.contain(serverName);
    expect(serversAuditText).to.contain('patched');

    const dbsAudit = await fixture<
      HTMLElement & { loading: boolean; updateComplete: Promise<unknown> }
    >(
      html`<page-databases-audit style="width: 1000px; height: 320px; display: block;"></page-databases-audit>`
    );
    dbsAudit.loading = false;
    await dbsAudit.updateComplete;
    (
      dbsAudit.shadowRoot?.querySelector('vaadin-grid') as
        | { clearCache(): void }
        | null
    )?.clearCache();
    await settle(200);
    const dbsAuditText =
      dbsAudit.shadowRoot?.querySelector('vaadin-grid')?.textContent ?? '';
    expect(dbsAuditText).to.contain(databaseName);
    expect(dbsAuditText).to.contain('Analytics');
  });
});
