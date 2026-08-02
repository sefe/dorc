/* eslint-disable @typescript-eslint/no-unused-vars */
 
import { html } from 'lit';
import '@vaadin/grid/vaadin-grid-sorter';
import {
  chipFromPath,
  hostCell,
  ListRowTemplate,
  pathDetails,
  pathText,
  renderListBar
} from '../components/dorc-list-row';

/**
 * Post-pilot batch: per-view row templates for the remaining grid views
 * (U-20 rule; named export per view keeps SC3's one-reviewable-declaration
 * property). `hostCell` reuses each view's existing imperative renderers for
 * actions and custom cells, so behaviour is preserved rather than
 * re-implemented. Views whose renderers read column properties receive a
 * stub carrying the host under the property names those renderers expect.
 *
 * Batch decisions, recorded once here:
 * - Disclosure is used only where fields exceed the row (details from
 *   labelled paths); two-field views render everything inline.
 * - DP views with header-hosted filter inputs keep them at wide width;
 *   narrow filter affordances for batch views are deferred (bar carries
 *   sorters) — logged in the delivery record as follow-up debt.
 */

type Host = any;
type Bar = Parameters<typeof renderListBar>[0];
type Wiring<T> = { template: ListRowTemplate<T>; bar?: Bar };

// Every column-property name the reused renderers dereference (found by
// grep for `_column.` across the batch views) — the stub carries the host
// under all of them.
const stub = (host: Host) => ({
  attachedDbsControl: host,
  attachedPageEnvironmentHistory: host,
  ACControl: host,
  altThis: host,
  attachedAppDaemonControl: host,
  attachedServersControl: host,
  mappingControl: host,
  attachedPPLControl: host
});

const sorters = (defs: Array<[string, string]>): Bar => ({
  sort: html`${defs.map(
    ([path, label]) =>
      html`<vaadin-grid-sorter path="${path}">${label}</vaadin-grid-sorter>`
  )}`
});

const meta = (paths: string[]) => (item: any) =>
  html`${paths
    .map(p => pathText(item, p))
    .filter(Boolean)
    .join(' · ')}`;

export const accessControlNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: hostCell(host, 'acNameRenderer'),
    metadata: item =>
      html`Write ${hostCell(host, 'acCanWrite')(item)} · Read secrets
      ${hostCell(host, 'acCanReadSecrets')(item)} · Owner
      ${hostCell(host, 'acCanOwner')(item)}`,
    actions: hostCell(host, '_boundACButtonsRenderer', stub(host))
  }
});

export const applicationDaemonsNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: item => html`${pathText(item, 'DaemonName')}`,
    metadata: meta(['ServerName']),
    chip: chipFromPath('Status'),
    actions: hostCell(host, '_boundDaemonsButtonsRenderer', stub(host))
  },
  bar: sorters([
    ['DaemonName', 'Daemon'],
    ['ServerName', 'Server'],
    ['Status', 'Status']
  ])
});

export const attachedAppUsersNarrow = (_host?: Host): Wiring<any> => ({
  template: {
    primary: item => html`${pathText(item, 'DisplayName')}`,
    metadata: meta(['LoginId', 'LanId', 'Team']),
    details: pathDetails([
      { label: 'Login type', path: 'LoginType' },
      { label: 'LAN ID type', path: 'LanIdType' }
    ])
  },
  bar: sorters([
    ['DisplayName', 'Name'],
    ['LoginId', 'Login']
  ])
});

export const attachedDatabasesNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: item => html`${pathText(item, 'Name')}`,
    metadata: meta(['ServerName', 'ArrayName']),
    actions: hostCell(host, '_boundDatabasesButtonsRenderer', stub(host))
  }
});

export const attachedEnvTenantsNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: item => html`${pathText(item, 'EnvironmentName')}`,
    actions: hostCell(host, 'environmentActionsRenderer', stub(host))
  }
});

export const attachedServersNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: item => html`${pathText(item, 'Name')}`,
    metadata: meta(['OsName']),
    actions: hostCell(host, '_boundServersButtonsRenderer', stub(host))
  }
});

export const previousAttemptsNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: hostCell(host, 'componentNameRenderer'),
    metadata: item =>
      html`${hostCell(host, 'componentTimingsRenderer')(item)}
      ${hostCell(host, 'componentStatusRenderer')(item)}`,
    actions: hostCell(host, 'componentLogRenderer')
  }
});

export const deployEnvPropsNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: item => html`${pathText(item, 'PropertyName')}`,
    metadata: meta(['PropertyValue']),
    actions: hostCell(host, '_boundPropOverridesButtonsRenderer', stub(host))
  },
  bar: sorters([['PropertyName', 'Name']])
});

export const envDeploymentsCardNarrow = (_host?: Host): Wiring<any> => ({
  template: {
    primary: item => html`${pathText(item, 'ComponentName')}`,
    metadata: meta(['RequestDetails', 'UpdateDate']),
    chip: chipFromPath('State')
  },
  bar: sorters([
    ['ComponentName', 'Component'],
    ['UpdateDate', 'Date']
  ])
});

export const envDeploymentsTabNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: item =>
      html`${hostCell(host, '_idRenderer')(item)}
      ${pathText(item, 'ComponentName')}`,
    metadata: item =>
      html`${pathText(item, 'RequestBuildNum')} ·
      ${hostCell(host, '_dateRenderer')(item)}`,
    chip: chipFromPath('State')
  },
  bar: sorters([
    ['RequestId', 'Request'],
    ['UpdatedDate', 'Date']
  ])
});

export const envVariablesNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: item => html`${pathText(item, 'Property')}`,
    metadata: meta(['PropertyValueScope']),
    actions: hostCell(host, 'variableValueControlsRenderer', stub(host))
  },
  bar: sorters([['Property', 'Name']])
});

export const mapDaemonsNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: item => html`${pathText(item, 'Name')}`,
    metadata: meta(['DisplayName', 'ServiceType']),
    actions: hostCell(host, '_detachRenderer', stub(host))
  }
});

export const configValuesNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: item => html`${pathText(item, 'Key')}`,
    metadata: item =>
      html`${pathText(item, 'Secure') === 'true' ? 'secure' : ''}
      ${pathText(item, 'IsForProd') === 'true' ? 'prod' : ''}`,
    actions: hostCell(host, 'variableValueControlsRenderer', stub(host))
  },
  bar: sorters([['Key', 'Name']])
});

const auditNarrow =
  (namePath: string) =>
  (_host: Host): Wiring<any> => ({
    template: {
      primary: item =>
        html`${pathText(item, namePath)} — ${pathText(item, 'Action')}`,
      metadata: meta(['Username', 'Date']),
      details: pathDetails([
        { label: 'From', path: 'FromValue' },
        { label: 'To', path: 'ToValue' }
      ])
    },
    bar: sorters([['Date', 'Date']])
  });

export const daemonsAuditNarrow = auditNarrow('DaemonName');
export const databasesAuditNarrow = auditNarrow('DatabaseName');
export const serversAuditNarrow = auditNarrow('ServerName');

export const daemonsListNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: item => html`${pathText(item, 'Name')}`,
    metadata: meta(['DisplayName', 'AccountName', 'ServiceType']),
    details: pathDetails([{ label: 'Last seen', path: 'LastSeenDate' }]),
    actions: hostCell(host, '_rowActionsRenderer', stub(host))
  },
  bar: sorters([
    ['Name', 'Name'],
    ['LastSeenDate', 'Last seen']
  ])
});

export const environmentsListNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: item => html`${pathText(item, 'EnvironmentName')}`,
    metadata: meta(['Details.EnvironmentOwner', 'Details.Description']),
    details: pathDetails([
      { label: 'Secure', path: 'EnvironmentSecure' },
      { label: 'Prod', path: 'EnvironmentIsProd' },
      { label: 'File share', path: 'Details.FileShare' },
      { label: 'Notes', path: 'Details.Notes' }
    ]),
    actions: hostCell(host, '_envDetailsButtonsRenderer', stub(host))
  },
  bar: sorters([['EnvironmentName', 'Name']])
});

export const projectBundlesNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: item =>
      html`${pathText(item, 'BundleName')} — ${pathText(item, 'RequestName')}`,
    metadata: item =>
      html`Sequence ${pathText(item, 'Sequence')}
      ${hostCell(host, '_typeRenderer')(item)}`,
    details: pathDetails([{ label: 'Request', path: 'Request' }]),
    actions: hostCell(host, 'bundleControlsRenderer', stub(host))
  },
  bar: sorters([['Sequence', 'Sequence']])
});

export const projectComponentsNarrow = (_host?: Host): Wiring<any> => ({
  template: {
    primary: item => html`${pathText(item, 'environmentName')}`,
    metadata: meta(['buildNumber', 'updateDate']),
    chip: chipFromPath('status')
  },
  bar: sorters([
    ['environmentName', 'Environment'],
    ['updateDate', 'Date']
  ])
});

export const projectsAuditNarrow = (_host: Host): Wiring<any> => ({
  // Keeps the view's existing row-details diff (chevron/activation logic is
  // the grid-menu-a11y outcome) — no details slot here.
  template: {
    primary: item =>
      html`${pathText(item, 'Project.ProjectName')} —
      ${pathText(item, 'Action')}`,
    metadata: meta(['Username', 'Date'])
  },
  bar: sorters([['Date', 'Date']])
});

export const projectsListNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: hostCell(host, '_projectNameRenderer'),
    metadata: meta(['ProjectDescription']),
    details: pathDetails([
      { label: 'Artefacts URL', path: 'ArtefactsUrl' },
      { label: 'Sub-paths', path: 'ArtefactsSubPaths' },
      { label: 'Build regex', path: 'ArtefactsBuildRegex' },
      { label: 'LeanIX', path: 'LeanIXUrl' }
    ]),
    actions: hostCell(host, '_projectEnvsButtonsRenderer', stub(host))
  },
  bar: sorters([['ProjectName', 'Name']])
});

export const scriptsAuditNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: item => html`${pathText(item, 'ScriptName')}`,
    metadata: meta(['ProjectNames', 'UpdatedBy', 'UpdatedDate']),
    actions: hostCell(host, 'valueRenderer', stub(host))
  },
  bar: sorters([['UpdatedDate', 'Updated']])
});

export const scriptsListNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: item => html`${pathText(item, 'Name')}`,
    metadata: meta(['ProjectNames', 'Path']),
    details: pathDetails([
      { label: 'Non-prod only', path: 'NonProdOnly' },
      { label: 'PS version', path: 'PowerShellVersionNumber' }
    ]),
    actions: hostCell(host, 'enabledRenderer', stub(host))
  },
  bar: sorters([['NonProdOnly', 'Non-prod']])
});

export const sqlPortsNarrow = (_host?: Host): Wiring<any> => ({
  template: {
    primary: item => html`${pathText(item, 'InstanceName')}`,
    metadata: meta(['SqlPort'])
  },
  bar: sorters([
    ['InstanceName', 'Instance'],
    ['SqlPort', 'Port']
  ])
});

export const usersListNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: item =>
      html`${hostCell(host, 'renderLoginType')(item)}
      ${pathText(item, 'DisplayName')}`,
    metadata: meta(['LanId'])
  },
  bar: sorters([
    ['DisplayName', 'Name'],
    ['LanId', 'Id']
  ])
});

export const variablesAuditNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: item => html`${pathText(item, 'PropertyName')}`,
    metadata: meta(['EnvironmentName', 'UpdatedBy', 'UpdatedDate']),
    actions: hostCell(host, 'valueRenderer', stub(host))
  },
  bar: sorters([['UpdatedDate', 'Updated']])
});

export const variablesValueLookupNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: item => html`${pathText(item, 'Property')}`,
    metadata: meta(['PropertyValueScope']),
    actions: hostCell(host, 'valueRenderer', stub(host))
  }
});

export const variablesNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: hostCell(host, 'scopeValueRenderer'),
    actions: hostCell(host, 'variableValueControlsRenderer', stub(host))
  }
});

export const serversListNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: item => html`${pathText(item, 'Name')}`,
    metadata: item =>
      html`${pathText(item, 'OsName')}
      ${hostCell(host, 'environmentNamesRenderer', stub(host))(item)}`,
    details: (item: any) =>
      hostCell(host, 'applicationTagsRenderer', stub(host))(item) as any,
    actions: hostCell(host, '_boundServersButtonsRenderer', stub(host))
  },
  bar: sorters([['Name', 'Name']])
});

export const databasesListNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: item => html`${pathText(item, 'Name')}`,
    metadata: item =>
      html`${pathText(item, 'ServerName')}
      ${hostCell(host, 'environmentNamesRenderer', stub(host))(item)}`,
    details: (item: any) =>
      hostCell(host, 'applicationTagsRenderer', stub(host))(item) as any,
    actions: hostCell(host, '_boundDatabasesButtonsRenderer', stub(host))
  },
  bar: sorters([['Name', 'Name'], ['ServerName', 'Instance']])
});

export const permissionsListNarrow = (host: Host): Wiring<any> => ({
  template: {
    primary: item => html`${pathText(item, 'DisplayName')}`,
    metadata: meta(['PermissionName']),
    actions: hostCell(host, '_permissionActionsRenderer', stub(host))
  },
  bar: sorters([['DisplayName', 'Name']])
});
