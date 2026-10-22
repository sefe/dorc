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
  DeploymentDefinitionsApi,
  PowerShellVersionsApi,
  RefDataApi,
  RefDataProjectEnvironmentMappingsApi,
  SourceControlType,
  TerraformSourceType,
  type ComponentApiModel,
  type DeploymentDefinitionApiModel,
  type EnvironmentApiModel,
  type TerraformStateBackendApiModel,
  type RefDataApiModel
} from '../apis/dorc-api';
import { PageElement } from '../helpers/page-element';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';
import { confirmPrompt } from '../components/confirm-prompt';
import { LOCATION_CHANGED_EVENT } from '../router/router';
import '../components/deploy/component-tree/hegs-tree';
import { TreeNode } from '../components/deploy/component-tree/TreeNode';
import '../components/deployment-definition-editor';
import '../components/deployment-definition-history';
import '../components/project-workspace-nav';
import {
  GENERIC_SCRIPT_CATEGORIES,
  GENERIC_SCRIPT_TEMPLATES,
  serializeScriptPathValue,
  type GenericScriptTemplate
} from '../helpers/generic-scripts';

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
  @state() private treeWidth = 280;
  @state() private collapsedPaths: string[] = [];
  @state() private scriptCatalogOpen = false;
  @state() private scriptCatalogSearch = '';
  @state() private scriptCatalogCategory = 'All';
  @state() private selectedGenericTemplateId =
    GENERIC_SCRIPT_TEMPLATES[0]?.id ?? '';
  @state() private genericDialogStepName =
    GENERIC_SCRIPT_TEMPLATES[0]?.defaultStepName ?? 'Deploy MSI';
  @state() private genericDialogPsVersion = '5.1';
  @state() private genericDialogArgs: Record<string, string> = {};
  @state() private scriptCatalogLoading = false;
  @state() private scriptCatalogItems: DeploymentDefinitionApiModel[] = [];
  private backendEnvironmentsLoading = false;
  private backendEnvironmentsLoaded = false;
  private treeResizeActive = false;
  private treeResizeStartX = 0;
  private treeResizeStartWidth = 280;
  private projectId = 0;
  private editor: ace.Ace.Editor | undefined;
  private syncingEditor = false;
  private loadToken = 0;
  private scriptCatalogToken = 0;

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
      grid-template-columns: var(--tree-pane-width, 280px) 8px minmax(0, 1fr);
      gap: var(--lumo-space-s);
      padding: var(--lumo-space-s) var(--lumo-space-m);
      overflow: hidden;
    }
    aside {
      min-height: 0;
      overflow-y: auto;
      padding-inline-end: var(--lumo-space-xs);
    }
    .tree-splitter {
      position: relative;
      cursor: col-resize;
      user-select: none;
      touch-action: none;
    }
    .tree-splitter::before {
      content: '';
      position: absolute;
      inset-block: 0;
      left: 3px;
      width: 2px;
      background-color: var(--dorc-border-color);
      transition: background-color 0.15s ease;
    }
    .tree-splitter:hover::before,
    .tree-splitter:focus-visible::before {
      background-color: var(--dorc-icon-interactive);
    }
    .tree-splitter:focus-visible {
      outline: 2px solid var(--dorc-link-color);
      outline-offset: -2px;
    }
    .tree {
      margin: var(--lumo-space-xs) 0;
    }
    .tree-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--lumo-space-xs);
      margin-top: var(--lumo-space-xs);
      font-size: var(--lumo-font-size-xs);
      color: var(--dorc-text-secondary-strong);
    }
    .tree-controls {
      display: inline-flex;
      gap: 4px;
    }
    .tree-control-btn {
      font: inherit;
      font-size: var(--lumo-font-size-xxs, 11px);
      color: var(--dorc-link-color);
      background: transparent;
      border: 1px solid var(--dorc-border-color);
      border-radius: var(--lumo-border-radius-s);
      padding: 1px 6px;
      cursor: pointer;
    }
    .tree-control-btn:hover {
      background: var(--dorc-bg-secondary);
    }
    .script-catalog-backdrop {
      position: fixed;
      inset: 0;
      z-index: 200;
      background: rgba(15, 23, 42, 0.48);
      display: flex;
      align-items: center;
      justify-content: center;
      padding: var(--lumo-space-m);
    }
    .script-catalog-dialog {
      width: min(980px, 94vw);
      max-height: 86vh;
      background: var(--dorc-bg-primary, #ffffff);
      color: var(--dorc-text-primary);
      border: 1px solid var(--dorc-border-color);
      border-radius: var(--lumo-border-radius-l);
      box-shadow: 0 20px 50px rgba(0, 0, 0, 0.28);
      display: flex;
      flex-direction: column;
      overflow: hidden;
    }
    .script-catalog-dialog-header {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: var(--lumo-space-m);
      padding: var(--lumo-space-m) var(--lumo-space-l);
      border-bottom: 1px solid var(--dorc-border-color);
    }
    .script-catalog-dialog-header h2 {
      margin: 0;
      font-size: var(--lumo-font-size-l);
    }
    .script-catalog-dialog-header p {
      margin: 2px 0 0;
      font-size: var(--lumo-font-size-xs);
    }
    .script-catalog-toolbar {
      display: flex;
      flex-direction: column;
      gap: var(--lumo-space-xs);
      padding: var(--lumo-space-s) var(--lumo-space-l);
      background: var(--dorc-bg-secondary);
      border-bottom: 1px solid var(--dorc-border-color);
    }
    .script-catalog-categories {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
    }
    .category-pill {
      font: inherit;
      font-size: var(--lumo-font-size-xs);
      padding: 3px 10px;
      border-radius: 999px;
      border: 1px solid var(--dorc-border-color);
      background: var(--dorc-bg-primary, #ffffff);
      color: var(--dorc-text-primary);
      cursor: pointer;
    }
    .category-pill[aria-pressed='true'] {
      background: var(--dorc-chip-bg);
      color: var(--dorc-chip-text);
      border-color: var(--dorc-icon-interactive);
      font-weight: 600;
    }
    .script-catalog-body {
      flex: 1;
      min-height: 0;
      display: grid;
      grid-template-columns: minmax(280px, 360px) minmax(0, 1fr);
      overflow: hidden;
    }
    .script-catalog-list {
      list-style: none;
      margin: 0;
      padding: var(--lumo-space-s);
      overflow-y: auto;
      border-inline-end: 1px solid var(--dorc-border-color);
      display: flex;
      flex-direction: column;
      gap: 6px;
    }
    .script-catalog-card {
      width: 100%;
      text-align: start;
      font: inherit;
      padding: var(--lumo-space-s);
      background: var(--dorc-bg-primary, #ffffff);
      color: var(--dorc-text-primary);
      border: 1px solid var(--dorc-border-color);
      border-radius: var(--lumo-border-radius-m);
      cursor: pointer;
      display: flex;
      flex-direction: column;
      gap: 3px;
    }
    .script-catalog-card:hover {
      background: var(--dorc-bg-secondary);
    }
    .script-catalog-card[aria-pressed='true'] {
      background: var(--dorc-chip-bg);
      color: var(--dorc-chip-text);
      border-color: var(--dorc-icon-interactive);
    }
    .script-catalog-card strong {
      font-size: var(--lumo-font-size-s);
    }
    .script-catalog-card code {
      font-size: var(--lumo-font-size-xs);
      color: var(--dorc-text-secondary-strong);
    }
    .script-catalog-detail {
      padding: var(--lumo-space-m) var(--lumo-space-l);
      overflow-y: auto;
      display: flex;
      flex-direction: column;
      gap: var(--lumo-space-s);
    }
    .script-catalog-detail h3 {
      margin: 0;
      font-size: var(--lumo-font-size-m);
    }
    .script-catalog-detail-grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--lumo-space-xs) var(--lumo-space-m);
    }
    .script-catalog-item {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--lumo-space-s);
      padding: var(--lumo-space-xs) var(--lumo-space-s);
      background: var(--dorc-bg-secondary);
      border: 1px solid var(--dorc-border-color);
      border-radius: var(--lumo-border-radius-s);
      font-size: var(--lumo-font-size-xs);
    }
    .script-catalog-meta {
      min-width: 0;
      flex: 1;
    }
    .script-catalog-meta strong {
      display: block;
      overflow-wrap: anywhere;
    }
    .script-catalog-meta span {
      display: block;
      color: var(--dorc-text-secondary-strong);
      font-family: var(--dorc-mono-font, monospace);
      overflow-wrap: anywhere;
    }
    .script-catalog-footer {
      display: flex;
      align-items: center;
      justify-content: space-between;
      flex-wrap: wrap;
      gap: var(--lumo-space-s);
      padding: var(--lumo-space-s) var(--lumo-space-l);
      border-top: 1px solid var(--dorc-border-color);
      background: var(--dorc-bg-secondary);
    }
    .pane,
    .project-source-pane,
    .terraform-state-pane {
      min-width: 0;
      min-height: 0;
      overflow-y: auto;
    }
    .project-source-pane,
    .terraform-state-pane {
      flex: 1;
      padding: var(--lumo-space-s) var(--lumo-space-m);
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
      .tree-splitter {
        display: none;
      }
      .project-fields,
      .diff,
      .script-catalog-body,
      .script-catalog-detail-grid {
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

  private parseTabFromSearch(search: string): number {
    const tab = new URLSearchParams(search).get('tab');
    if (tab === 'history') return 2;
    if (tab === 'project-source') return 3;
    if (tab === 'terraform-state') return 4;
    return 0;
  }

  private syncLocationState = () => {
    const nextTab = this.parseTabFromSearch(window.location.search);
    const nextHistoryComponentId = Number(
      new URLSearchParams(window.location.search).get('componentId')
    );
    if (nextHistoryComponentId) {
      this.historyComponentId = nextHistoryComponentId;
    }
    if (nextTab !== this.tab && !this.saving && this.readJson()) {
      this.tab = nextTab;
      if (nextTab === 4) {
        this.fetchBackendEnvironments();
      }
    }
  };

  protected firstUpdated() {
    const match = window.location.pathname.match(
      /\/project-ref-data\/(\d+)\/?$/
    );
    this.projectId = match ? Number(match[1]) : 0;
    this.tab = this.parseTabFromSearch(window.location.search);
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
    window.addEventListener(LOCATION_CHANGED_EVENT, this.syncLocationState);
    window.addEventListener('popstate', this.syncLocationState);
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
    this.stopTreeResize();
    this.loadToken++;
    window.removeEventListener('beforeunload', this.beforeUnload);
    window.removeEventListener(LOCATION_CHANGED_EVENT, this.syncLocationState);
    window.removeEventListener('popstate', this.syncLocationState);
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
          const searchParams = new URLSearchParams(window.location.search);
          const addScriptPath = searchParams.get('addScriptPath');
          if (addScriptPath && this.editable) {
            this.addScriptFromCatalog(
              {
                ComponentName:
                  searchParams.get('addScriptName') || 'Script step',
                ScriptPath: addScriptPath,
                PSVersion: searchParams.get('addPsVersion') || '5.1',
                NonProdOnly: searchParams.get('addNonProdOnly') === 'true',
                StopOnFailure: searchParams.get('addStopOnFailure') !== 'false',
                IsEnabled: true
              },
              Boolean(this.selectedPath)
            );
            searchParams.delete('addScriptPath');
            searchParams.delete('addScriptName');
            searchParams.delete('addPsVersion');
            searchParams.delete('addNonProdOnly');
            searchParams.delete('addStopOnFailure');
            const nextQuery = searchParams.toString();
            window.history.replaceState(
              {},
              '',
              `${window.location.pathname}${nextQuery ? `?${nextQuery}` : ''}`
            );
          }
          this.syncEditor();
          if (
            this.tab === 4 ||
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
    } else if (index === 4) {
      this.fetchBackendEnvironments();
    }
    const url = new URL(window.location.href);
    if (index === 2) {
      url.searchParams.set('tab', 'history');
    } else if (index === 3) {
      url.searchParams.set('tab', 'project-source');
    } else if (index === 4) {
      url.searchParams.set('tab', 'terraform-state');
    } else {
      url.searchParams.delete('tab');
    }
    window.history.replaceState({}, '', `${url.pathname}${url.search}`);
  }

  private startTreeResize = (event: MouseEvent) => {
    event.preventDefault();
    this.treeResizeActive = true;
    this.treeResizeStartX = event.clientX;
    this.treeResizeStartWidth = this.treeWidth;
    window.addEventListener('mousemove', this.onTreeResizeMove);
    window.addEventListener('mouseup', this.stopTreeResize);
  };

  private onTreeResizeMove = (event: MouseEvent) => {
    if (!this.treeResizeActive) return;
    const delta = event.clientX - this.treeResizeStartX;
    this.treeWidth = Math.min(
      640,
      Math.max(180, this.treeResizeStartWidth + delta)
    );
  };

  private stopTreeResize = () => {
    if (!this.treeResizeActive) return;
    this.treeResizeActive = false;
    window.removeEventListener('mousemove', this.onTreeResizeMove);
    window.removeEventListener('mouseup', this.stopTreeResize);
  };

  private onTreeSplitterKeyDown = (event: KeyboardEvent) => {
    if (event.key === 'ArrowLeft') {
      event.preventDefault();
      this.treeWidth = Math.max(180, this.treeWidth - 20);
    } else if (event.key === 'ArrowRight') {
      event.preventDefault();
      this.treeWidth = Math.min(640, this.treeWidth + 20);
    }
  };

  private onHegsTreeNodeToggled(
    event: CustomEvent<{ path: string; open: boolean }>
  ) {
    const { path, open } = event.detail;
    if (!path) return;
    if (open) {
      this.collapsedPaths = this.collapsedPaths.filter(item => item !== path);
    } else if (!this.collapsedPaths.includes(path)) {
      this.collapsedPaths = [...this.collapsedPaths, path];
    }
  }

  private expandAllTree() {
    this.collapsedPaths = [];
  }

  private collapseAllTree() {
    this.collapsedPaths = this.rows
      .filter(row => (row.node.Children?.length ?? 0) > 0)
      .map(row => row.path);
  }

  private buildTreeNodes(
    nodes: ComponentApiModel[],
    parentPath = ''
  ): TreeNode[] {
    return nodes.map((node, index) => {
      const path = parentPath ? `${parentPath}.${index}` : String(index);
      const children = this.buildTreeNodes(node.Children ?? [], path);
      const kindText =
        node.ComponentType === ComponentType.Terraform
          ? `Terraform / ${node.TerraformSourceType ?? TerraformSourceType.SharedFolder}`
          : `${node.ComponentType ?? ComponentType.PowerShell}${node.PSVersion ? ` ${node.PSVersion}` : ''}`;
      return {
        id: node.ComponentId ?? 0,
        name: node.ComponentName || 'New component',
        icon: '',
        open: !this.collapsedPaths.includes(path),
        children,
        numOfChildren: children.length,
        hasParent: Boolean(parentPath),
        parentId: node.ParentId ?? 0,
        checked: false,
        indeterminate: false,
        path,
        subtitle: kindText,
        disabled: node.IsEnabled === false
      };
    });
  }

  private ensureAncestorsExpanded(path: string) {
    const parts = path.split('.');
    if (parts.length <= 1) return;
    const ancestors = new Set<string>();
    for (let i = 1; i < parts.length; i++) {
      ancestors.add(parts.slice(0, i).join('.'));
    }
    this.collapsedPaths = this.collapsedPaths.filter(p => !ancestors.has(p));
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

  private cloneSelectedAsSibling() {
    if (!this.editable || !this.document || !this.selected) return;
    const document = structuredClone(this.document);
    const indexes = this.selectedPath.split('.').map(Number);
    let siblings = document.Components ?? [];
    for (const index of indexes.slice(0, -1)) {
      siblings = siblings[index].Children ?? [];
    }
    const currentIndex = indexes[indexes.length - 1];
    const source = siblings[currentIndex];
    if (!source) return;
    const insertIndex = currentIndex + 1;
    const parentPrefix = indexes.slice(0, -1).join('.');
    const clone: ComponentApiModel = {
      ComponentId: 0,
      ComponentName: source.ComponentName
        ? `${source.ComponentName} (copy)`
        : '',
      ComponentType: source.ComponentType ?? ComponentType.PowerShell,
      TerraformSourceType:
        source.TerraformSourceType ?? TerraformSourceType.SharedFolder,
      ScriptPath: source.ScriptPath ?? '',
      TerraformSubPath: source.TerraformSubPath,
      TerraformGitBranch: source.TerraformGitBranch,
      TerraformTemplateName: source.TerraformTemplateName,
      TerraformTemplateVersion: source.TerraformTemplateVersion,
      IsEnabled: source.IsEnabled ?? true,
      StopOnFailure: source.StopOnFailure ?? true,
      NonProdOnly: source.NonProdOnly ?? false,
      PSVersion: source.PSVersion ?? '5.1',
      ParentId: source.ParentId ?? 0,
      Children: []
    };
    siblings.splice(insertIndex, 0, clone);
    this.selectedPath = parentPrefix
      ? `${parentPrefix}.${insertIndex}`
      : String(insertIndex);
    this.ensureAncestorsExpanded(this.selectedPath);
    this.setDocument(document);
  }

  private addChildToSelected() {
    if (!this.editable || !this.document || !this.selected) return;
    const document = structuredClone(this.document);
    const target = treeRows(document.Components ?? []).find(
      row => row.path === this.selectedPath
    )?.node;
    if (!target) return;
    target.Children ??= [];
    const childIndex = target.Children.length;
    target.Children.push({
      ComponentId: 0,
      ComponentName: '',
      ComponentType: target.ComponentType ?? ComponentType.PowerShell,
      TerraformSourceType:
        target.TerraformSourceType ?? TerraformSourceType.SharedFolder,
      ScriptPath: target.ScriptPath ?? '',
      TerraformSubPath: target.TerraformSubPath,
      TerraformGitBranch: target.TerraformGitBranch,
      IsEnabled: target.IsEnabled ?? true,
      StopOnFailure: target.StopOnFailure ?? true,
      NonProdOnly: target.NonProdOnly ?? false,
      PSVersion: target.PSVersion ?? '5.1',
      ParentId: target.ComponentId ?? 0,
      Children: []
    });
    this.selectedPath = `${this.selectedPath}.${childIndex}`;
    this.ensureAncestorsExpanded(this.selectedPath);
    this.setDocument(document);
  }

  private moveSelected(delta: -1 | 1) {
    if (!this.editable || !this.document || !this.selectedPath) return;
    const document = structuredClone(this.document);
    const indexes = this.selectedPath.split('.').map(Number);
    let siblings = document.Components ?? [];
    for (const index of indexes.slice(0, -1)) {
      siblings = siblings[index].Children ?? [];
    }
    const currentIndex = indexes[indexes.length - 1];
    const targetIndex = currentIndex + delta;
    if (targetIndex < 0 || targetIndex >= siblings.length) return;
    const [moved] = siblings.splice(currentIndex, 1);
    siblings.splice(targetIndex, 0, moved);
    const parentPrefix = indexes.slice(0, -1).join('.');
    this.selectedPath = parentPrefix
      ? `${parentPrefix}.${targetIndex}`
      : String(targetIndex);
    this.setDocument(document);
  }

  private toggleScriptCatalog() {
    this.scriptCatalogOpen = !this.scriptCatalogOpen;
    if (this.scriptCatalogOpen) {
      if (!this.selectedGenericTemplateId && GENERIC_SCRIPT_TEMPLATES[0]) {
        this.selectDialogGenericTemplate(GENERIC_SCRIPT_TEMPLATES[0]);
      }
      if (this.scriptCatalogItems.length === 0) {
        this.loadScriptCatalog();
      }
    }
  }

  private selectDialogGenericTemplate(template: GenericScriptTemplate) {
    this.selectedGenericTemplateId = template.id;
    this.genericDialogStepName = template.defaultStepName;
    const defaults: Record<string, string> = {};
    for (const param of template.parameters) {
      defaults[param.name] = param.options?.[0] ?? '';
    }
    this.genericDialogArgs = defaults;
  }

  private addConfiguredGenericScript(asSiblingOfSelected: boolean) {
    const template =
      GENERIC_SCRIPT_TEMPLATES.find(
        item => item.id === this.selectedGenericTemplateId
      ) ?? GENERIC_SCRIPT_TEMPLATES[0];
    if (!template) return;
    // Unfilled template parameters are omitted so they fall back to the
    // environment property of the same name rather than shadowing it with "".
    const args = template.parameters
      .map(param => ({
        key: param.name,
        value: this.genericDialogArgs[param.name] ?? ''
      }))
      .filter(arg => arg.value.trim().length > 0);
    const scriptPath = serializeScriptPathValue(template.scriptPath, args);
    this.addScriptFromCatalog(
      {
        ComponentName: this.genericDialogStepName || template.defaultStepName,
        ScriptPath: scriptPath,
        PSVersion: this.genericDialogPsVersion || '5.1',
        IsEnabled: true,
        StopOnFailure: true,
        NonProdOnly: false
      },
      asSiblingOfSelected
    );
    this.scriptCatalogOpen = false;
  }

  private loadScriptCatalog() {
    // Fired per keystroke; only the latest response may update the list.
    const token = ++this.scriptCatalogToken;
    this.scriptCatalogLoading = true;
    new DeploymentDefinitionsApi(dorcApiConfiguration)
      .deploymentDefinitionsGet({
        page: 1,
        limit: 50,
        componentType: ComponentType.PowerShell,
        search: this.scriptCatalogSearch || undefined
      })
      .subscribe({
        next: response => {
          if (token !== this.scriptCatalogToken) return;
          this.scriptCatalogLoading = false;
          const seen = new Set<string>();
          this.scriptCatalogItems = (response.Items ?? []).filter(item => {
            const comp = item.Component;
            const key = `${comp?.ScriptPath ?? ''}|${comp?.ComponentName ?? ''}`;
            if (!comp?.ScriptPath || seen.has(key)) return false;
            seen.add(key);
            return true;
          });
        },
        error: error => {
          if (token !== this.scriptCatalogToken) return;
          this.scriptCatalogLoading = false;
          this.error =
            retrieveErrorMessage(error) ||
            'Could not load script components from catalog.';
        }
      });
  }

  private addScriptFromCatalog(
    comp: ComponentApiModel,
    asSiblingOfSelected: boolean
  ) {
    if (!this.editable || !this.document) return;
    const document = structuredClone(this.document);
    document.Components ??= [];
    const baseName = comp.ComponentName || 'Script step';
    const existingNames = new Set(
      treeRows(document.Components).map(r => r.node.ComponentName ?? '')
    );
    const componentName = existingNames.has(baseName)
      ? `${baseName} (copy)`
      : baseName;

    const newComponent: ComponentApiModel = {
      ComponentId: 0,
      ComponentName: componentName,
      ComponentType: ComponentType.PowerShell,
      TerraformSourceType: TerraformSourceType.SharedFolder,
      ScriptPath: comp.ScriptPath ?? '',
      IsEnabled: comp.IsEnabled ?? true,
      StopOnFailure: comp.StopOnFailure ?? true,
      NonProdOnly: comp.NonProdOnly ?? false,
      PSVersion: comp.PSVersion ?? '5.1',
      ParentId: 0,
      Children: []
    };

    if (asSiblingOfSelected && this.selectedPath) {
      const indexes = this.selectedPath.split('.').map(Number);
      let siblings = document.Components;
      for (const index of indexes.slice(0, -1)) {
        siblings = siblings[index].Children ?? [];
      }
      const currentIndex = indexes[indexes.length - 1];
      const current = siblings[currentIndex];
      newComponent.ParentId = current?.ParentId ?? 0;
      const insertIndex = currentIndex + 1;
      siblings.splice(insertIndex, 0, newComponent);
      const parentPrefix = indexes.slice(0, -1).join('.');
      this.selectedPath = parentPrefix
        ? `${parentPrefix}.${insertIndex}`
        : String(insertIndex);
    } else {
      this.selectedPath = String(document.Components.length);
      document.Components.push(newComponent);
    }
    this.ensureAncestorsExpanded(this.selectedPath);
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
      | 'BusinessVertical'
      | 'ApplicationCode',
    helperText = ''
  ) {
    return html`<vaadin-text-field
      .label=${label}
      .value=${this.document?.Project?.[field] ?? ''}
      .readonly=${!this.editable}
      .helperText=${helperText}
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

  private renderProjectSource() {
    const project = this.document?.Project;
    return html`<details class="project-source" open>
      <summary>Project source settings</summary>
      <p>
        These settings are shared by this project's definitions. Changing them
        creates new revisions; queued requests retain their submitted
        configuration.
      </p>
      <div class="project-fields">
        ${this.projectField('Description', 'ProjectDescription')}
        ${this.projectField('Terraform Git repository', 'TerraformGitRepoUrl')}
        ${this.projectField('Artifact URL', 'ArtefactsUrl')}
        ${this.projectField('Artifact subpaths', 'ArtefactsSubPaths')}
        ${this.projectField('Build filter', 'ArtefactsBuildRegex')}
        ${this.projectField(
          'Business vertical',
          'BusinessVertical',
          'Two-letter Kafka naming-standard code (e.g. tr); seeds catalog input suggestions.'
        )}
        ${this.projectField(
          'Application code',
          'ApplicationCode',
          'Three-character IT application code (e.g. gma); seeds the origin segment.'
        )}
        <vaadin-combo-box
          label="Project source control"
          .items=${Object.values(SourceControlType)}
          .value=${project?.SourceControlType ?? SourceControlType.AzureDevOps}
          .disabled=${!this.editable}
          @change=${(event: Event) => {
            const source = Object.values(SourceControlType).find(
              value => value === (event.currentTarget as HTMLInputElement).value
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
    </details>`;
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
      open
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

  private renderScriptCatalogDialog(selected: ComponentApiModel | undefined) {
    if (!this.scriptCatalogOpen || !this.editable) return nothing;
    const query = this.scriptCatalogSearch.trim().toLowerCase();
    const filteredTemplates = GENERIC_SCRIPT_TEMPLATES.filter(item => {
      const categoryMatch =
        this.scriptCatalogCategory === 'All' ||
        this.scriptCatalogCategory === 'Project scripts' ||
        item.category === this.scriptCatalogCategory;
      if (!categoryMatch) return false;
      if (!query) return true;
      return (
        item.name.toLowerCase().includes(query) ||
        item.scriptPath.toLowerCase().includes(query) ||
        item.description.toLowerCase().includes(query) ||
        item.category.toLowerCase().includes(query)
      );
    });
    const activeTemplate =
      filteredTemplates.find(
        item => item.id === this.selectedGenericTemplateId
      ) ??
      GENERIC_SCRIPT_TEMPLATES.find(
        item => item.id === this.selectedGenericTemplateId
      ) ??
      filteredTemplates[0];
    const previewScriptPath = activeTemplate
      ? serializeScriptPathValue(
          activeTemplate.scriptPath,
          activeTemplate.parameters.map(param => ({
            key: param.name,
            value: this.genericDialogArgs[param.name] ?? ''
          }))
        )
      : '';

    return html`
      <div
        class="script-catalog-backdrop"
        @click=${(event: Event) => {
          if (event.target === event.currentTarget) {
            this.scriptCatalogOpen = false;
          }
        }}
        @keydown=${(event: KeyboardEvent) => {
          if (event.key === 'Escape') {
            this.scriptCatalogOpen = false;
          }
        }}
      >
        <div
          class="script-catalog-dialog"
          role="dialog"
          aria-modal="true"
          aria-label="Reusable PowerShell script catalog"
        >
          <div class="script-catalog-dialog-header">
            <div>
              <h2>
                Add predefined generic script (<code>00 Generic*.ps1</code>)
              </h2>
              <p>
                Select a predefined PowerShell script from the Azure DevOps
                <code>project-scripts</code> repository and fill in its
                <code>GenericArguments</code>, or reuse an existing project
                script step.
              </p>
            </div>
            <vaadin-button
              theme="tertiary"
              @click=${() => {
                this.scriptCatalogOpen = false;
              }}
              >Close</vaadin-button
            >
          </div>
          <div class="script-catalog-toolbar">
            <vaadin-text-field
              label="Filter generic scripts or project steps"
              placeholder="Search DeployMSI, DACPAC, ControlServices, ActiveBatch, 00 Generic..."
              .value=${this.scriptCatalogSearch}
              @input=${(event: Event) => {
                this.scriptCatalogSearch = (
                  event.currentTarget as HTMLInputElement
                ).value;
                this.loadScriptCatalog();
              }}
            ></vaadin-text-field>
            <div
              class="script-catalog-categories"
              role="group"
              aria-label="Generic script categories"
            >
              ${[...GENERIC_SCRIPT_CATEGORIES, 'Project scripts'].map(
                category => html`
                  <button
                    type="button"
                    class="category-pill"
                    aria-pressed=${
                      this.scriptCatalogCategory === category ? 'true' : 'false'
                    }
                    @click=${() => {
                      this.scriptCatalogCategory = category;
                    }}
                  >
                    ${category}
                  </button>
                `
              )}
            </div>
          </div>
          <div class="script-catalog-body">
            <ul
              class="script-catalog-list"
              aria-label="Predefined generic scripts"
            >
              ${filteredTemplates.map(
                item => html`
                  <li>
                    <button
                      type="button"
                      class="script-catalog-card"
                      aria-pressed=${
                        activeTemplate?.id === item.id ? 'true' : 'false'
                      }
                      @click=${() => this.selectDialogGenericTemplate(item)}
                    >
                      <strong>${item.name}</strong>
                      <code>${item.scriptPath}</code>
                      <span>${item.category}</span>
                    </button>
                  </li>
                `
              )}
            </ul>
            <div class="script-catalog-detail">
              ${
                activeTemplate
                  ? html`
                      <div>
                        <h3>${activeTemplate.name}</h3>
                        <p>${activeTemplate.description}</p>
                      </div>
                      <div class="script-catalog-detail-grid">
                        <vaadin-text-field
                          label="Step name"
                          .value=${this.genericDialogStepName}
                          @input=${(event: Event) => {
                            this.genericDialogStepName = (
                              event.currentTarget as HTMLInputElement
                            ).value;
                          }}
                        ></vaadin-text-field>
                        <vaadin-combo-box
                          label="PowerShell version"
                          .items=${
                            this.powerShellVersions.length
                              ? this.powerShellVersions
                              : ['5.1', '7.4']
                          }
                          .value=${this.genericDialogPsVersion}
                          @change=${(event: Event) => {
                            this.genericDialogPsVersion =
                              (event.currentTarget as HTMLInputElement).value ||
                              '5.1';
                          }}
                        ></vaadin-combo-box>
                        ${activeTemplate.parameters.map(param =>
                          param.options
                            ? html`<vaadin-combo-box
                                .label=${param.label}
                                .items=${param.options}
                                .value=${this.genericDialogArgs[param.name] ?? ''}
                                .helperText=${param.helperText ?? ''}
                                ?required=${Boolean(param.required)}
                                @change=${(event: Event) => {
                                  this.genericDialogArgs = {
                                    ...this.genericDialogArgs,
                                    [param.name]: (
                                      event.currentTarget as HTMLInputElement
                                    ).value
                                  };
                                }}
                              ></vaadin-combo-box>`
                            : html`<vaadin-text-field
                                .label=${param.label}
                                .placeholder=${param.placeholder ?? ''}
                                .value=${this.genericDialogArgs[param.name] ?? ''}
                                .helperText=${param.helperText ?? ''}
                                ?required=${Boolean(param.required)}
                                @input=${(event: Event) => {
                                  this.genericDialogArgs = {
                                    ...this.genericDialogArgs,
                                    [param.name]: (
                                      event.currentTarget as HTMLInputElement
                                    ).value
                                  };
                                }}
                              ></vaadin-text-field>`
                        )}
                      </div>
                      <div>
                        <p>
                          Generated <code>ScriptPath</code>:
                          <code>${previewScriptPath}</code>
                        </p>
                        ${
                          activeTemplate.requiredEnvironmentProperties?.length
                            ? html`<p>
                                Standard DOrc environment properties used:
                                <code
                                  >${activeTemplate.requiredEnvironmentProperties.join(
                                    ', '
                                  )}</code
                                >
                              </p>`
                            : nothing
                        }
                      </div>
                      <div class="actions">
                        <vaadin-button
                          theme="primary"
                          @click=${() =>
                            this.addConfiguredGenericScript(Boolean(selected))}
                        >
                          ${
                            selected
                              ? `Add "${activeTemplate.name}" as sibling`
                              : `Add "${activeTemplate.name}" step`
                          }
                        </vaadin-button>
                        ${
                          selected
                            ? html`<vaadin-button
                                @click=${() =>
                                  this.addConfiguredGenericScript(false)}
                                >Add at project root</vaadin-button
                              >`
                            : nothing
                        }
                      </div>
                    `
                  : nothing
              }
              ${
                this.scriptCatalogLoading
                  ? html`<span>Loading reusable project script steps...</span>`
                  : this.scriptCatalogItems.length > 0
                    ? html`
                        <details open>
                          <summary>
                            Reusable project script steps
                            (${this.scriptCatalogItems.length})
                          </summary>
                          <div
                            style="display: flex; flex-direction: column; gap: 6px; margin-top: 8px;"
                          >
                            ${this.scriptCatalogItems.map(
                              item => html`
                                <div class="script-catalog-item">
                                  <div class="script-catalog-meta">
                                    <strong
                                      >${
                                        item.Component?.ComponentName ||
                                        'Script step'
                                      }</strong
                                    >
                                    <span
                                      >${item.Component?.ScriptPath ?? ''}
                                      (${item.Component?.PSVersion ?? '5.1'})</span
                                    >
                                  </div>
                                  <vaadin-button
                                    theme="small"
                                    @click=${() => {
                                      if (item.Component) {
                                        this.addScriptFromCatalog(
                                          item.Component,
                                          Boolean(selected)
                                        );
                                        this.scriptCatalogOpen = false;
                                      }
                                    }}
                                    >${
                                      selected ? 'Insert sibling' : 'Add step'
                                    }</vaadin-button
                                  >
                                </div>
                              `
                            )}
                          </div>
                        </details>
                      `
                    : nothing
              }
            </div>
          </div>
        </div>
      </div>
    `;
  }

  render() {
    const selected = this.selected;
    const project = this.document?.Project;
    const parentPath = this.selectedPath.includes('.')
      ? this.selectedPath.slice(0, this.selectedPath.lastIndexOf('.'))
      : '';
    const catalogUrl = `/module-catalog?project=${encodeURIComponent(project?.ProjectName ?? '')}&projectId=${this.projectId}`;
    const workspaceActiveTab =
      this.tab === 3
        ? 'project-source'
        : this.tab === 4
          ? 'terraform-state'
          : 'definitions';
    const hasNestedNodes = this.rows.some(
      row => (row.node.Children?.length ?? 0) > 0
    );
    const selectedIndexes = this.selectedPath
      ? this.selectedPath.split('.').map(Number)
      : [];
    let selectedSiblings: ComponentApiModel[] = this.document?.Components ?? [];
    for (const index of selectedIndexes.slice(0, -1)) {
      selectedSiblings = selectedSiblings[index]?.Children ?? [];
    }
    const selectedSiblingIndex = selectedIndexes.length
      ? selectedIndexes[selectedIndexes.length - 1]
      : -1;
    const canMoveUp = this.editable && selectedSiblingIndex > 0;
    const canMoveDown =
      this.editable &&
      selectedSiblingIndex >= 0 &&
      selectedSiblingIndex < selectedSiblings.length - 1;

    return html`
      <project-workspace-nav
        .projectId=${project?.ProjectId ?? this.projectId}
        .projectName=${project?.ProjectName ?? ''}
        .activeTab=${workspaceActiveTab}
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
        ?hidden=${this.tab === 3 || this.tab === 4}
        .selected=${live(this.tab <= 2 ? this.tab : 0)}
        @selected-changed=${(event: CustomEvent<{ value: number }>) => {
          if (this.tab <= 2) {
            this.switchTab(event.detail.value);
          }
        }}
      >
        <vaadin-tab>Definitions</vaadin-tab
        ><vaadin-tab>Advanced JSON</vaadin-tab><vaadin-tab>History</vaadin-tab>
      </vaadin-tabs>
      <div
        class="workspace"
        style="--tree-pane-width: ${this.treeWidth}px"
        ?hidden=${Boolean(this.preview) || this.tab !== 0 || !this.document}
      >
        <aside>
          <div class="actions">
            <vaadin-button
              @click=${() => this.addComponent()}
              .disabled=${!this.editable}
              >Add component</vaadin-button
            >
            ${
              this.editable
                ? html`<vaadin-button
                    theme="tertiary"
                    @click=${() => this.toggleScriptCatalog()}
                    >Script catalog</vaadin-button
                  >`
                : nothing
            }
            ${this.editable && !this.dirty ? html`<a href=${catalogUrl}>Add from catalog</a>` : nothing}
          </div>
          ${
            hasNestedNodes
              ? html`<div class="tree-header">
                  <span>Step hierarchy (${this.rows.length})</span>
                  <div class="tree-controls">
                    <button
                      type="button"
                      class="tree-control-btn"
                      @click=${() => this.expandAllTree()}
                    >
                      Expand all
                    </button>
                    <button
                      type="button"
                      class="tree-control-btn"
                      @click=${() => this.collapseAllTree()}
                    >
                      Collapse all
                    </button>
                  </div>
                </div>`
              : nothing
          }
          <hegs-tree
            class="tree"
            aria-label="Project component hierarchy"
            selectable
            .data=${this.buildTreeNodes(this.document?.Components ?? [])}
            .selectedPath=${this.selectedPath}
            @hegs-tree-node-selected=${(
              event: CustomEvent<{ path: string }>
            ) => {
              this.selectedPath = event.detail.path;
            }}
            @hegs-tree-node-toggled=${(
              event: CustomEvent<{ path: string; open: boolean }>
            ) => this.onHegsTreeNodeToggled(event)}
          ></hegs-tree>
          ${!this.rows.length ? html`<p>No components yet. Add a script or choose a stock module.</p>` : nothing}
        </aside>
        ${this.renderScriptCatalogDialog(selected)}
        <div
          class="tree-splitter"
          role="separator"
          aria-orientation="vertical"
          aria-label="Resize component tree"
          tabindex="0"
          @mousedown=${this.startTreeResize}
          @keydown=${this.onTreeSplitterKeyDown}
        ></div>
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
                              ${'— '.repeat(row.depth)}${row.node.ComponentName || 'New component'}
                            </option>`
                        )}
                    </select>
                  </label>
                  <div class="actions">
                    <vaadin-button
                      @click=${() => this.cloneSelectedAsSibling()}
                      .disabled=${!this.editable}
                      title="Create a new sibling step pre-populated with parent, PowerShell version and script path"
                      >Clone sibling</vaadin-button
                    >
                    <vaadin-button
                      @click=${() => this.addChildToSelected()}
                      .disabled=${!this.editable}
                      title="Create a child step under this component pre-populated with its runtime and script defaults"
                      >Add child</vaadin-button
                    >
                    <vaadin-button
                      theme="tertiary"
                      @click=${() => this.moveSelected(-1)}
                      .disabled=${!canMoveUp}
                      title="Move step up within parent"
                      >Move up</vaadin-button
                    >
                    <vaadin-button
                      theme="tertiary"
                      @click=${() => this.moveSelected(1)}
                      .disabled=${!canMoveDown}
                      title="Move step down within parent"
                      >Move down</vaadin-button
                    >
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
        </section>
      </div>
      <section
        class="project-source-pane"
        ?hidden=${Boolean(this.preview) || this.tab !== 3 || !this.document}
      >
        ${this.renderProjectSource()}
      </section>
      <section
        class="terraform-state-pane"
        ?hidden=${Boolean(this.preview) || this.tab !== 4 || !this.document}
      >
        ${this.renderBackends()}
      </section>
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
