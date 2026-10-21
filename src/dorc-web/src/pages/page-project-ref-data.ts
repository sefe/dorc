import * as ace from 'ace-builds';
import '@vaadin/button';
import '@vaadin/combo-box';
import '@vaadin/tabs';
import '@vaadin/text-field';
import { Notification } from '@vaadin/notification';
import { css, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { live } from 'lit/directives/live.js';
import {
  ComponentType,
  PowerShellVersionsApi,
  RefDataApi,
  RefDataProjectEnvironmentMappingsApi,
  SourceControlType,
  TerraformSourceType,
  type ComponentApiModel,
  type EnvironmentApiModel,
  type TerraformStateBackendApiModel,
  type RefDataApiModel
} from '../apis/dorc-api';
import { PageElement } from '../helpers/page-element';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';
import { confirmPrompt } from '../components/confirm-prompt';
import '../components/deployment-definition-editor';
import '../components/deployment-definition-history';
import '../components/project-workspace-nav';

type DefinitionNode = ComponentApiModel;
type ReferenceDocument = RefDataApiModel;
interface TreeRow {
  node: DefinitionNode;
  path: string;
  depth: number;
}

function treeRows(nodes: DefinitionNode[], prefix = '', depth = 0): TreeRow[] {
  return nodes.flatMap((node, index) => {
    const path = prefix ? `${prefix}.${index}` : `${index}`;
    return [
      { node, path, depth },
      ...treeRows(node.Children ?? [], path, depth + 1)
    ];
  });
}

function isReferenceDocument(value: unknown): value is ReferenceDocument {
  if (
    !value ||
    typeof value !== 'object' ||
    !('Project' in value) ||
    !value.Project ||
    typeof value.Project !== 'object' ||
    !('ProjectId' in value.Project) ||
    typeof value.Project.ProjectId !== 'number' ||
    !('Components' in value) ||
    !Array.isArray(value.Components)
  )
    return false;
  const validNode = (node: unknown): boolean =>
    Boolean(
      node &&
      typeof node === 'object' &&
      'ComponentName' in node &&
      typeof node.ComponentName === 'string' &&
      (!('Children' in node) ||
        node.Children == null ||
        (Array.isArray(node.Children) && node.Children.every(validNode)))
    );
  return value.Components.every(validNode);
}

@customElement('page-project-ref-data')
export class PageProjectRefData extends PageElement {
  @state() private document: ReferenceDocument | undefined;
  @state() private baseline: ReferenceDocument | undefined;
  @state() private preview: ReferenceDocument | undefined;
  @state() private selectedPath = '';
  @state() private tab = 0;
  @state() private historyComponentId = 0;
  @state() private loading = false;
  @state() private saving = false;
  @state() private error = '';
  @state() private powerShellVersions: string[] = [];
  @state() private jsonDirty = false;
  @state() private backendEnvironments: EnvironmentApiModel[] = [];
  @state() private backendEnvironmentError = '';
  private backendEnvironmentsLoading = false;
  private backendEnvironmentsLoaded = false;
  private projectId = 0;
  private editor: ace.Ace.Editor | undefined;
  private syncingEditor = false;
  private loadToken = 0;

  static styles = css`
    :host {
      display: flex;
      flex-direction: column;
      height: 100%;
      min-height: 0;
      color: var(--dorc-text-primary);
    }
    header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--lumo-space-s);
      padding: var(--lumo-space-xs) var(--lumo-space-m);
      flex-wrap: wrap;
      border-bottom: 1px solid var(--dorc-border-color);
    }
    header > div:first-child {
      display: flex;
      flex-wrap: wrap;
      align-items: baseline;
      gap: var(--lumo-space-s);
    }
    header > div:first-child > span {
      font-size: var(--lumo-font-size-xs);
      color: var(--dorc-text-secondary-strong);
    }
    h1 {
      margin: 0;
      font-size: var(--lumo-font-size-l);
    }
    h2 {
      font-size: var(--lumo-font-size-l);
    }
    p {
      max-width: 75ch;
      color: var(--dorc-text-secondary-strong);
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--lumo-space-s);
    }
    a {
      color: var(--dorc-link-color);
    }
    .workspace {
      flex: 1;
      min-height: 0;
      display: grid;
      grid-template-columns: minmax(180px, 240px) minmax(0, 1fr);
      gap: var(--lumo-space-m);
      padding: var(--lumo-space-s) var(--lumo-space-m);
      overflow: hidden;
    }
    aside {
      min-height: 0;
      overflow-y: auto;
      border-inline-end: 1px solid var(--dorc-border-color);
      padding-inline-end: var(--lumo-space-s);
    }
    .tree {
      list-style: none;
      margin: var(--lumo-space-xs) 0;
      padding: 0;
    }
    .tree button {
      font: inherit;
      color: var(--dorc-text-primary);
      background: transparent;
      border: 0;
      border-radius: var(--lumo-border-radius-s);
      padding: var(--lumo-space-xs) var(--lumo-space-s);
      width: 100%;
      text-align: start;
      overflow-wrap: anywhere;
      cursor: pointer;
    }
    .tree button[aria-pressed='true'] {
      background: var(--dorc-chip-bg);
      color: var(--dorc-chip-text);
    }
    .tree button:focus-visible {
      outline: 2px solid var(--dorc-icon-interactive);
      outline-offset: -2px;
    }
    .kind {
      display: block;
      font-size: var(--lumo-font-size-xs);
      color: var(--dorc-text-secondary-strong);
    }
    .pane {
      min-width: 0;
      min-height: 0;
      overflow-y: auto;
    }
    .notice,
    .error {
      margin: var(--lumo-space-xs) var(--lumo-space-m);
      font-size: var(--lumo-font-size-s);
    }
    .error {
      color: var(--lumo-error-text-color);
    }
    .advanced {
      flex: 1;
      min-height: 0;
      display: flex;
      flex-direction: column;
      padding: var(--lumo-space-xs) var(--lumo-space-m);
    }
    .advanced > p {
      margin: var(--lumo-space-xs) 0;
      font-size: var(--lumo-font-size-xs);
    }
    #editor {
      flex: 1;
      min-height: 200px;
    }
    [hidden] {
      display: none !important;
    }
    .history {
      flex: 1;
      min-height: 0;
      overflow: auto;
      padding: var(--lumo-space-s) var(--lumo-space-m);
    }
    label {
      display: inline-flex;
      flex-direction: column;
      gap: 4px;
      margin-block: var(--lumo-space-xs);
      font-size: var(--lumo-font-size-xs);
      font-weight: 500;
      color: var(--dorc-text-secondary-strong);
    }
    label select {
      font: inherit;
      font-size: var(--lumo-font-size-s);
      color: var(--dorc-text-primary);
      background: var(--dorc-bg-secondary);
      border: 1px solid var(--dorc-border-color);
      border-radius: var(--lumo-border-radius-m);
      padding: var(--lumo-space-xs) var(--lumo-space-s);
      min-width: 220px;
      max-width: 100%;
    }
    details {
      margin-top: var(--lumo-space-s);
      border-top: 1px solid var(--dorc-border-color);
      padding-top: var(--lumo-space-xs);
    }
    details summary {
      cursor: pointer;
      font-size: var(--lumo-font-size-s);
      font-weight: 600;
      padding-block: var(--lumo-space-xs);
    }
    details > p {
      margin: var(--lumo-space-xs) 0;
      font-size: var(--lumo-font-size-xs);
      color: var(--dorc-text-secondary-strong);
    }
    .project-fields {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--lumo-space-s) var(--lumo-space-m);
    }
    .backend-row {
      border: 1px solid var(--dorc-border-color);
      padding: var(--lumo-space-s);
      margin-block: var(--lumo-space-s);
      min-width: 0;
    }
    .backend-row legend {
      padding-inline: var(--lumo-space-xs);
    }
    vaadin-text-field,
    vaadin-combo-box {
      width: 100%;
      min-width: 0;
    }
    .preview {
      flex: 1;
      min-height: 0;
      display: flex;
      flex-direction: column;
      gap: var(--lumo-space-xs);
      padding: var(--lumo-space-s) var(--lumo-space-m);
      border-top: 1px solid var(--dorc-border-color);
      overflow: hidden;
    }
    .preview h2,
    .preview h3,
    .preview p {
      margin: 0;
    }
    .preview h2 {
      font-size: var(--lumo-font-size-m);
    }
    .preview h3 {
      font-size: var(--lumo-font-size-s);
    }
    .preview p {
      font-size: var(--lumo-font-size-xs);
      color: var(--dorc-text-secondary-strong);
    }
    .diff {
      flex: 1;
      min-height: 0;
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--lumo-space-m);
    }
    .diff > div {
      min-height: 0;
      display: flex;
      flex-direction: column;
      gap: var(--lumo-space-xs);
    }
    pre {
      flex: 1;
      min-height: 0;
      margin: 0;
      background: var(--dorc-bg-secondary);
      border: 1px solid var(--dorc-border-color);
      border-radius: var(--lumo-border-radius-s);
      font-family: var(--dorc-mono-font, monospace);
      font-size: var(--lumo-font-size-xs);
      white-space: pre-wrap;
      overflow-wrap: anywhere;
      padding: var(--lumo-space-s);
      overflow: auto;
    }
    @media (max-width: 768px) {
      .workspace {
        grid-template-columns: 1fr;
        overflow-y: auto;
        padding: var(--lumo-space-s);
        gap: var(--lumo-space-s);
      }
      .project-fields,
      .diff {
        grid-template-columns: 1fr;
      }
      aside {
        max-height: 28vh;
        border-inline-end: none;
        border-bottom: 1px solid var(--dorc-border-color);
        padding-inline-end: 0;
        padding-bottom: var(--lumo-space-xs);
      }
      .pane {
        overflow-y: visible;
      }
    }
  `;

  protected firstUpdated() {
    const match = window.location.pathname.match(
      /\/project-ref-data\/(\d+)\/?$/
    );
    this.projectId = match ? Number(match[1]) : 0;
    this.tab =
      new URLSearchParams(window.location.search).get('tab') === 'history'
        ? 2
        : 0;
    this.historyComponentId = Number(
      new URLSearchParams(window.location.search).get('componentId')
    );
    const editorElement = this.shadowRoot?.getElementById('editor');
    if (editorElement) {
      this.editor = ace.edit(editorElement);
      this.editor.renderer.attachToShadowRoot();
      this.editor.setTheme('ace/theme/terminal');
      this.editor.session.setMode('ace/mode/json');
      this.editor.session.setUseWorker(false);
      this.editor.setOptions({ showPrintMargin: false });
      this.editor.on('change', () => {
        if (this.syncingEditor) return;
        this.jsonDirty = true;
        this.preview = undefined;
      });
    }
    window.addEventListener('beforeunload', this.beforeUnload);
    this.load();
    new PowerShellVersionsApi(dorcApiConfiguration)
      .powerShellVersionsGet()
      .subscribe({
        next: versions => {
          this.powerShellVersions = versions
            .map(version => version.Value)
            .filter((version): version is string => Boolean(version));
        },
        error: error => {
          this.error =
            retrieveErrorMessage(error) ||
            'Could not load supported PowerShell versions.';
        }
      });
  }

  disconnectedCallback() {
    super.disconnectedCallback();
    this.loadToken++;
    window.removeEventListener('beforeunload', this.beforeUnload);
    this.editor?.destroy();
  }

  private beforeUnload = (event: BeforeUnloadEvent) => {
    if (this.dirty) {
      event.preventDefault();
      event.returnValue = '';
    }
  };

  private get dirty() {
    return (
      this.jsonDirty ||
      JSON.stringify(this.document) !== JSON.stringify(this.baseline)
    );
  }

  private get editable() {
    return this.document?.CanEdit === true && !this.saving && !this.loading;
  }
  private get rows() {
    return treeRows(this.document?.Components ?? []);
  }
  private get selected() {
    return this.rows.find(row => row.path === this.selectedPath)?.node;
  }

  private syncEditor() {
    this.syncingEditor = true;
    this.editor?.setValue(JSON.stringify(this.document, null, 2) ?? '', -1);
    this.editor?.setReadOnly(!this.editable);
    this.syncingEditor = false;
    this.jsonDirty = false;
  }

  private load() {
    if (!this.projectId) {
      this.error = 'A valid project ID is required.';
      return;
    }
    const token = ++this.loadToken;
    this.loading = true;
    this.error = '';
    this.backendEnvironmentsLoaded = false;
    this.backendEnvironmentsLoading = false;
    this.backendEnvironmentError = '';
    new RefDataApi(dorcApiConfiguration)
      .refDataIdGet({ id: String(this.projectId) })
      .subscribe({
        next: data => {
          if (token !== this.loadToken) return;
          this.document = structuredClone(data);
          this.baseline = structuredClone(data);
          this.preview = undefined;
          this.loading = false;
          const componentId = Number(
            new URLSearchParams(window.location.search).get('componentId')
          );
          this.selectedPath = componentId
            ? (this.rows.find(row => row.node.ComponentId === componentId)
                ?.path ?? '')
            : (this.rows[0]?.path ?? '');
          if (componentId && !this.selectedPath) {
            this.error =
              'This component is no longer attached to the current project. Its retained history is still available in the History tab.';
          }
          this.syncEditor();
          if (
            this.renderRoot.querySelector<HTMLDetailsElement>(
              'details.backends'
            )?.open
          ) {
            this.fetchBackendEnvironments();
          }
        },
        error: error => {
          if (token !== this.loadToken) return;
          this.loading = false;
          this.error =
            retrieveErrorMessage(error) ||
            'Could not load project reference data.';
        }
      });
  }

  private async reload() {
    if (
      this.dirty &&
      !(await confirmPrompt(
        'Discard unsaved definition changes and load the current project revision?',
        {
          header: 'Reload reference data',
          confirmText: 'Discard and reload'
        }
      ))
    )
      return;
    this.load();
  }

  private readJson() {
    if (!this.jsonDirty) return true;
    try {
      const value: unknown = JSON.parse(this.editor?.getValue() || '');
      if (
        !isReferenceDocument(value) ||
        value.Project?.ProjectId !== this.projectId
      ) {
        this.error =
          'JSON must contain this project and a valid Components tree. Project identity cannot be changed.';
        return false;
      }
      this.document = {
        ...value,
        Revision: this.baseline?.Revision,
        CanEdit: this.baseline?.CanEdit,
        CanDeploy: this.baseline?.CanDeploy
      };
      this.jsonDirty = false;
      this.error = '';
      return true;
    } catch (error) {
      this.error =
        error instanceof Error
          ? `Invalid reference-data JSON: ${error.message}`
          : 'Invalid reference-data JSON.';
      return false;
    }
  }

  private switchTab(index: number) {
    if (this.saving || !this.readJson()) {
      this.requestUpdate();
      return;
    }
    if (index === this.tab) return;
    this.tab = index;
    if (index === 1) {
      this.syncEditor();
      void this.updateComplete.then(() => this.editor?.resize());
    }
  }

  private setDocument(document: ReferenceDocument) {
    this.document = document;
    this.preview = undefined;
    this.error = '';
  }

  private changeSelected(event: CustomEvent<ComponentApiModel>) {
    if (!this.editable || !this.document) return;
    const document = structuredClone(this.document);
    const node = treeRows(document.Components ?? []).find(
      row => row.path === this.selectedPath
    )?.node;
    if (!node) return;
    Object.assign(node, event.detail);
    this.setDocument(document);
  }

  private addComponent() {
    if (!this.editable || !this.document) return;
    const document = structuredClone(this.document);
    document.Components ??= [];
    this.selectedPath = String(document.Components.length);
    document.Components.push({
      ComponentId: 0,
      ComponentName: '',
      ComponentType: ComponentType.PowerShell,
      TerraformSourceType: TerraformSourceType.SharedFolder,
      ScriptPath: '',
      IsEnabled: true,
      StopOnFailure: true,
      NonProdOnly: false,
      PSVersion: '5.1',
      ParentId: 0,
      Children: []
    });
    this.setDocument(document);
  }

  private async detachSelected() {
    if (!this.editable || !this.selected || !this.document) return;
    const document = structuredClone(this.document);
    const selectedPath = this.selectedPath;
    if (
      !(await confirmPrompt(
        `Detach ${this.selected.ComponentName || 'this component'} and its children from this project?\n\nThis changes DOrc metadata only. It does not destroy infrastructure or erase execution history.`,
        {
          header: 'Detach component',
          confirmText: 'Detach from project'
        }
      ))
    )
      return;
    const indexes = selectedPath.split('.').map(Number);
    let siblings = document.Components ?? [];
    for (const index of indexes.slice(0, -1))
      siblings = siblings[index].Children ?? [];
    siblings.splice(indexes[indexes.length - 1], 1);
    this.selectedPath = '';
    this.setDocument(document);
  }

  private changeParent(parentPath: string) {
    if (!this.editable || !this.document || !this.selected) return;
    if (
      parentPath === this.selectedPath ||
      parentPath.startsWith(`${this.selectedPath}.`)
    )
      return;
    const document = structuredClone(this.document);
    const rows = treeRows(document.Components ?? []);
    const selected = rows.find(row => row.path === this.selectedPath)?.node;
    const parent = rows.find(row => row.path === parentPath)?.node;
    if (!selected) return;
    const indexes = this.selectedPath.split('.').map(Number);
    let siblings = document.Components ?? [];
    for (const index of indexes.slice(0, -1))
      siblings = siblings[index].Children ?? [];
    siblings.splice(indexes[indexes.length - 1], 1);
    if (parent) {
      parent.Children ??= [];
      parent.Children.push(selected);
    } else {
      document.Components ??= [];
      document.Components.push(selected);
    }
    selected.ParentId = parent?.ComponentId ?? 0;
    this.selectedPath =
      treeRows(document.Components ?? []).find(row => row.node === selected)
        ?.path ?? '';
    this.setDocument(document);
  }

  private projectField(
    label: string,
    field:
      | 'ProjectDescription'
      | 'TerraformGitRepoUrl'
      | 'ArtefactsUrl'
      | 'ArtefactsSubPaths'
      | 'ArtefactsBuildRegex'
  ) {
    return html`<vaadin-text-field
      .label=${label}
      .value=${this.document?.Project?.[field] ?? ''}
      .readonly=${!this.editable}
      @change=${(event: Event) => {
        if (!this.editable || !this.document?.Project) return;
        this.setDocument({
          ...this.document,
          Project: {
            ...this.document.Project,
            [field]: (event.currentTarget as HTMLInputElement).value
          }
        });
      }}
    ></vaadin-text-field>`;
  }

  private loadBackendEnvironments(event: Event) {
    if (!(event.currentTarget as HTMLDetailsElement).open) return;
    this.fetchBackendEnvironments();
  }

  private fetchBackendEnvironments() {
    if (
      this.backendEnvironmentsLoaded ||
      this.backendEnvironmentsLoading ||
      !this.document?.Project?.ProjectName
    )
      return;
    this.backendEnvironmentsLoading = true;
    this.backendEnvironmentError = '';
    const token = this.loadToken;
    new RefDataProjectEnvironmentMappingsApi(dorcApiConfiguration)
      .refDataProjectEnvironmentMappingsGet({
        project: this.document.Project.ProjectName,
        includeRead: true
      })
      .subscribe({
        next: result => {
          this.backendEnvironmentsLoading = false;
          if (token !== this.loadToken) return;
          this.backendEnvironments = result.Items ?? [];
          this.backendEnvironmentsLoaded = true;
        },
        error: error => {
          this.backendEnvironmentsLoading = false;
          if (token !== this.loadToken) return;
          this.backendEnvironmentError =
            retrieveErrorMessage(error) ||
            'Could not load mapped environments. Close and reopen this section to retry.';
        }
      });
  }

  private setBackends(backends: TerraformStateBackendApiModel[]) {
    if (!this.editable || !this.document?.Project) return;
    this.setDocument({
      ...this.document,
      Project: { ...this.document.Project, TerraformStateBackends: backends }
    });
  }

  private backendField(
    index: number,
    label: string,
    field:
      'StorageAccount' | 'ContainerName' | 'SubscriptionId' | 'ResourceGroup'
  ) {
    const backends = this.document?.Project?.TerraformStateBackends ?? [];
    return html`<vaadin-text-field
      .label=${label}
      .value=${live(backends[index]?.[field] ?? '')}
      .disabled=${!this.editable}
      ?required=${field !== 'ResourceGroup'}
      @change=${(event: Event) =>
        this.setBackends(
          backends.map((backend, row) =>
            row === index
              ? {
                  ...backend,
                  [field]: (event.currentTarget as HTMLInputElement).value
                }
              : backend
          )
        )}
    ></vaadin-text-field>`;
  }

  private renderBackends() {
    const backends = this.document?.Project?.TerraformStateBackends ?? [];
    const scopes = [
      { label: 'Project default', value: 'default' },
      ...this.backendEnvironments.map(environment => ({
        label:
          environment.EnvironmentName ??
          `Environment ${environment.EnvironmentId}`,
        value: String(environment.EnvironmentId)
      }))
    ];
    for (const backend of backends) {
      if (
        backend.EnvironmentId != null &&
        !scopes.some(scope => scope.value === String(backend.EnvironmentId))
      )
        scopes.push({
          label: `Environment ${backend.EnvironmentId} (not in current mappings)`,
          value: String(backend.EnvironmentId)
        });
    }
    return html`<details
      class="backends"
      @toggle=${this.loadBackendEnvironments}
    >
      <summary>Terraform state backends</summary>
      <p>
        Configure this project's Catalog state storage. An environment override
        replaces the complete project default; fields are not merged. Workload
        subscription and credentials stay on the target environment. There is no
        DOrc-wide backend fallback.
      </p>
      <p>
        Requests retain the backend selected at submission. Moving a backend
        already used by a request or plan requires an operator-reviewed state
        migration. Storage accounts and containers must already exist.
      </p>
      ${this.backendEnvironmentError ? html`<p role="alert">${this.backendEnvironmentError}</p>` : nothing}
      ${backends.length === 0 ? html`<p>No backend configured. Add a project default or an environment override before deploying a Catalog module.</p>` : nothing}
      ${backends.map(
        (backend, index) =>
          html`<fieldset class="backend-row">
            <legend>
              ${scopes.find(scope => scope.value === (backend.EnvironmentId == null ? 'default' : String(backend.EnvironmentId)))?.label}
            </legend>
            <div class="project-fields">
              <vaadin-combo-box
                label="Backend scope"
                .items=${scopes}
                item-label-path="label"
                item-value-path="value"
                .value=${backend.EnvironmentId == null ? 'default' : String(backend.EnvironmentId)}
                .disabled=${!this.editable}
                @change=${(event: Event) => {
                  const value = (event.currentTarget as HTMLInputElement).value;
                  if (scopes.some(scope => scope.value === value))
                    this.setBackends(
                      backends.map((item, row) =>
                        row === index
                          ? {
                              ...item,
                              EnvironmentId:
                                value === 'default' ? null : Number(value)
                            }
                          : item
                      )
                    );
                }}
              ></vaadin-combo-box>
              ${this.backendField(index, 'State storage account', 'StorageAccount')}
              ${this.backendField(index, 'State container', 'ContainerName')}
              ${this.backendField(index, 'State subscription ID', 'SubscriptionId')}
              ${this.backendField(index, 'State resource group (optional)', 'ResourceGroup')}
            </div>
            <vaadin-button
              .disabled=${!this.editable}
              @click=${() => this.setBackends(backends.filter((_, row) => row !== index))}
              >Remove backend</vaadin-button
            >
          </fieldset>`
      )}
      <vaadin-button
        .disabled=${
          !this.editable ||
          (backends.some(backend => backend.EnvironmentId == null) &&
            !this.backendEnvironments.some(
              environment =>
                !backends.some(
                  backend => backend.EnvironmentId === environment.EnvironmentId
                )
            ))
        }
        @click=${() => {
          const environment = backends.some(
            backend => backend.EnvironmentId == null
          )
            ? this.backendEnvironments.find(
                environment =>
                  !backends.some(
                    backend =>
                      backend.EnvironmentId === environment.EnvironmentId
                  )
              )
            : undefined;
          this.setBackends([
            ...backends,
            { EnvironmentId: environment?.EnvironmentId ?? null }
          ]);
        }}
        >Add backend</vaadin-button
      >
    </details>`;
  }

  private previewSave() {
    if (!this.editable || !this.readJson() || !this.document) return;
    this.preview = structuredClone(this.document);
  }

  private save() {
    if (!this.preview || !this.editable) return;
    const selectedId = this.selected?.ComponentId;
    const selectedName = this.selected?.ComponentName;
    this.saving = true;
    this.editor?.setReadOnly(true);
    this.error = '';
    new RefDataApi(dorcApiConfiguration)
      .refDataPut({ refDataApiModel: this.preview })
      .subscribe({
        next: data => {
          this.document = structuredClone(data);
          this.baseline = structuredClone(data);
          this.selectedPath =
            this.rows.find(row =>
              selectedId
                ? row.node.ComponentId === selectedId
                : row.node.ComponentName === selectedName
            )?.path ?? '';
          this.preview = undefined;
          this.saving = false;
          this.syncEditor();
          Notification.show(
            'Saved reference data and recorded the definition revision.',
            { theme: 'success', position: 'bottom-end', duration: 5000 }
          );
        },
        error: error => {
          this.saving = false;
          this.editor?.setReadOnly(!this.editable);
          this.error =
            retrieveErrorMessage(error) ||
            'Could not save reference data. Your unsaved changes are preserved; reload to resolve a revision conflict.';
        }
      });
  }

  render() {
    const selected = this.selected;
    const project = this.document?.Project;
    const parentPath = this.selectedPath.includes('.')
      ? this.selectedPath.slice(0, this.selectedPath.lastIndexOf('.'))
      : '';
    const catalogUrl = `/module-catalog?project=${encodeURIComponent(project?.ProjectName ?? '')}`;
    return html`
      <project-workspace-nav
        .projectId=${project?.ProjectId ?? this.projectId}
        .projectName=${project?.ProjectName ?? ''}
        activeTab="definitions"
      ></project-workspace-nav>
      <header>
        <div>
          <h1>${project?.ProjectName ?? 'Project'} reference data</h1>
          <span
            >Current revision: ${this.document?.Revision ?? 'Not loaded'}</span
          >
        </div>
        <div class="actions">
          <vaadin-button
            @click=${() => this.reload()}
            .disabled=${this.loading || this.saving}
            >Reload</vaadin-button
          >
          <vaadin-button
            theme="primary"
            @click=${() => this.previewSave()}
            .disabled=${!this.editable || !this.dirty}
            >Review changes</vaadin-button
          >
        </div>
      </header>
      ${this.error ? html`<p class="error" role="alert">${this.error}</p>` : nothing}
      ${this.loading ? html`<p class="notice" role="status">Loading project definitions...</p>` : nothing}
      ${this.document && !this.document.CanEdit ? html`<p class="notice">Read-only. Editing requires permission to modify this project.</p>` : nothing}
      ${this.dirty ? html`<p class="notice" role="status">Unsaved changes. Save before deploying; submitted requests keep their original definition revision.</p>` : nothing}
      <vaadin-tabs
        .selected=${live(this.tab)}
        @selected-changed=${(event: CustomEvent<{ value: number }>) => this.switchTab(event.detail.value)}
      >
        <vaadin-tab>Definitions</vaadin-tab
        ><vaadin-tab>Advanced JSON</vaadin-tab><vaadin-tab>History</vaadin-tab>
      </vaadin-tabs>
      <div
        class="workspace"
        ?hidden=${Boolean(this.preview) || this.tab !== 0 || !this.document}
      >
        <aside>
          <div class="actions">
            <vaadin-button
              @click=${() => this.addComponent()}
              .disabled=${!this.editable}
              >Add component</vaadin-button
            >
            ${this.editable && !this.dirty ? html`<a href=${catalogUrl}>Add from catalog</a>` : nothing}
          </div>
          <ul class="tree" aria-label="Project component hierarchy">
            ${this.rows.map(
              row =>
                html`<li style="padding-inline-start:${row.depth * 16}px">
                  <button
                    type="button"
                    aria-pressed=${row.path === this.selectedPath ? 'true' : 'false'}
                    @click=${() => {
                      this.selectedPath = row.path;
                    }}
                  >
                    ${row.node.ComponentName || 'New component'}
                    <span class="kind"
                      >${row.node.ComponentType ?? ComponentType.PowerShell}${row.node.ComponentType === ComponentType.Terraform ? ` / ${row.node.TerraformSourceType ?? TerraformSourceType.SharedFolder}` : ''}</span
                    >
                  </button>
                </li>`
            )}
          </ul>
          ${!this.rows.length ? html`<p>No components yet. Add a script or choose a stock module.</p>` : nothing}
        </aside>
        <section class="pane">
          <deployment-definition-editor
            .component=${selected}
            .project=${project}
            .readonly=${!this.editable}
            .powerShellVersions=${this.powerShellVersions}
            @definition-changed=${(event: CustomEvent<ComponentApiModel>) => this.changeSelected(event)}
          ></deployment-definition-editor>
          ${
            selected
              ? html` <label
                    >Parent component
                    <select
                      aria-label="Parent component"
                      .value=${parentPath}
                      ?disabled=${!this.editable}
                      @change=${(event: Event) => this.changeParent((event.currentTarget as HTMLSelectElement).value)}
                    >
                      <option value="">Project root</option>
                      ${this.rows
                        .filter(
                          row =>
                            row.path !== this.selectedPath &&
                            !row.path.startsWith(`${this.selectedPath}.`)
                        )
                        .map(
                          row =>
                            html`<option value=${row.path}>
                              ${row.node.ComponentName || 'New component'}
                            </option>`
                        )}
                    </select>
                  </label>
                  <div class="actions">
                    <vaadin-button
                      @click=${() => this.detachSelected()}
                      .disabled=${!this.editable}
                      >Detach from project</vaadin-button
                    >
                    ${
                      selected.ComponentId && !this.dirty
                        ? html`
                            ${
                              this.document?.CanDeploy
                                ? html`<a
                                    href="/deploy?projectId=${this.projectId}&amp;componentId=${selected.ComponentId}"
                                    >Deploy this component</a
                                  >`
                                : nothing
                            }
                            <a
                              href="/project-ref-data/${this.projectId}?componentId=${selected.ComponentId}&amp;tab=history"
                              >Component history</a
                            >
                            ${this.document?.CanDeploy && selected.TerraformSourceType === TerraformSourceType.Catalog ? html`<a href="${catalogUrl}&amp;template=${encodeURIComponent(selected.TerraformTemplateName ?? '')}&amp;version=${encodeURIComponent(selected.TerraformTemplateVersion ?? '')}&amp;componentId=${selected.ComponentId}">Plan or upgrade module</a>` : nothing}
                          `
                        : nothing
                    }
                  </div>`
              : nothing
          }
          <details>
            <summary>Project source settings</summary>
            <p>
              These settings are shared by this project's definitions. Changing
              them creates new revisions; queued requests retain their submitted
              configuration.
            </p>
            <div class="project-fields">
              ${this.projectField('Description', 'ProjectDescription')}
              ${this.projectField('Terraform Git repository', 'TerraformGitRepoUrl')}
              ${this.projectField('Artifact URL', 'ArtefactsUrl')}
              ${this.projectField('Artifact subpaths', 'ArtefactsSubPaths')}
              ${this.projectField('Build filter', 'ArtefactsBuildRegex')}
              <vaadin-combo-box
                label="Project source control"
                .items=${Object.values(SourceControlType)}
                .value=${project?.SourceControlType ?? SourceControlType.AzureDevOps}
                .disabled=${!this.editable}
                @change=${(event: Event) => {
                  const source = Object.values(SourceControlType).find(
                    value =>
                      value === (event.currentTarget as HTMLInputElement).value
                  );
                  if (source && this.document?.Project && this.editable)
                    this.setDocument({
                      ...this.document,
                      Project: {
                        ...this.document.Project,
                        SourceControlType: source
                      }
                    });
                }}
              ></vaadin-combo-box>
            </div>
          </details>
          ${this.renderBackends()}
        </section>
      </div>
      <div class="advanced" ?hidden=${Boolean(this.preview) || this.tab !== 1}>
        <p>
          Advanced editing uses the same validation, revision and audit path.
          Project identity and the expected revision cannot be overridden here.
        </p>
        <div id="editor"></div>
      </div>
      ${
        !this.preview && this.tab === 2 && this.projectId
          ? html`<div class="history">
              <label
                >History scope
                <select
                  aria-label="History scope"
                  .value=${String(this.historyComponentId)}
                  @change=${(event: Event) => {
                    this.historyComponentId = Number(
                      (event.currentTarget as HTMLSelectElement).value
                    );
                  }}
                >
                  <option value="0">Entire project</option>
                  ${
                    this.historyComponentId &&
                    !this.rows.some(
                      row => row.node.ComponentId === this.historyComponentId
                    )
                      ? html`<option value=${String(this.historyComponentId)}>
                          Retained component #${this.historyComponentId} (not
                          attached)
                        </option>`
                      : nothing
                  }
                  ${this.rows.filter(row => row.node.ComponentId).map(row => html`<option value=${String(row.node.ComponentId)}>${row.node.ComponentName}</option>`)}
                </select>
              </label>
              <deployment-definition-history
                .projectId=${this.projectId}
                .componentId=${this.historyComponentId || undefined}
                .revision=${this.document?.Revision ?? ''}
              ></deployment-definition-history>
            </div>`
          : nothing
      }
      ${
        this.preview
          ? html`<section class="preview">
              <h2>Review reference-data changes</h2>
              <p>
                Saving updates current definitions and their audit history. It
                does not deploy infrastructure or change requests already
                submitted.
              </p>
              <div class="diff">
                <div>
                  <h3>Before</h3>
                  <pre>${JSON.stringify(this.baseline, null, 2)}</pre>
                </div>
                <div>
                  <h3>After</h3>
                  <pre>${JSON.stringify(this.preview, null, 2)}</pre>
                </div>
              </div>
              <div class="actions">
                <vaadin-button
                  theme="primary"
                  @click=${() => this.save()}
                  .disabled=${this.saving}
                  >${this.saving ? 'Saving...' : 'Confirm save'}</vaadin-button
                >
                <vaadin-button
                  @click=${() => {
                    this.preview = undefined;
                  }}
                  .disabled=${this.saving}
                  >Keep editing</vaadin-button
                >
              </div>
            </section>`
          : nothing
      }
    `;
  }
}
