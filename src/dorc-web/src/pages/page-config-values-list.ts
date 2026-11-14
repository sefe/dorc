import { live } from 'lit/directives/live.js';
import { columnBodyRenderer, columnHeaderRenderer } from '@vaadin/grid/lit';
import { css, nothing, PropertyValues } from 'lit';
import '../components/dorc-spinner';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid-sorter';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/dialog';
import '@vaadin/text-field';
import type { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { dialogFooterRenderer, dialogRenderer } from '@vaadin/dialog/lit';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { PageElement } from '../helpers/page-element';
import { ResponsiveMixin } from '../helpers/responsive-mixin';
import { ConfigValueApiModel, RefDataConfigApi } from '../apis/dorc-api';
import { Checkbox } from '@vaadin/checkbox';
import '../components/grid-button-groups/config-value-controls';
import type { ConfigValueEditDraft } from '../components/grid-button-groups/config-value-controls';
import '../components/add-config-value';
import { RefDataRolesApi } from '../apis/dorc-api';
import '@vaadin/checkbox';
import { ref } from 'lit/directives/ref.js';
import { keyed } from 'lit/directives/keyed.js';
import { UnsavedChangesGuard } from '../components/unsaved-changes-guard';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';

const VISIBLE_TO_SCRIPTS_HEADER_TOOLTIP =
  'Stored script-visibility classification. Non-secure values are visible to scripts unless reserved; secure values require opt-in. Reserved DOrc operating credentials are always withheld from script scope regardless of this setting.';

@customElement('page-config-values-list')
export class PageConfigValuesList extends ResponsiveMixin(PageElement) {
  private readonly unsavedChanges = new UnsavedChangesGuard();

  @state() addConfigValueDialogOpened = false;

  @state() private addConfigValueSequence = 0;

  @state() private updatingConfigIds: ReadonlySet<number> = new Set<number>();

  @state() private editingDrafts: ReadonlyMap<number, ConfigValueEditDraft> =
    new Map<number, ConfigValueEditDraft>();

  private searchQuery = '';

  @property({ type: Array }) configValues: Array<ConfigValueApiModel> = [];

  @property({ type: Array }) filteredConfigValues: Array<ConfigValueApiModel> =
    [];

  @property({ type: Array }) appConfig = [];

  @property({ type: Boolean }) details = false;

  @property({ type: Boolean }) isAdmin = false;

  private loading = true;

  constructor() {
    super();
    this.getConfigValuesList();
  }

  private isRowUpdating(configValue: ConfigValueApiModel): boolean {
    return configValue.Id != null && this.updatingConfigIds.has(configValue.Id);
  }

  private isRowEditing(configValue: ConfigValueApiModel): boolean {
    return configValue.Id != null && this.editingDrafts.has(configValue.Id);
  }

  private setEditingDraft(
    id: number,
    draft: ConfigValueEditDraft | undefined
  ): void {
    const next = new Map(this.editingDrafts);
    if (draft) {
      next.set(id, draft);
    } else {
      next.delete(id);
    }
    this.editingDrafts = next;
  }

  private setRowUpdating(id: number, updating: boolean): void {
    const next = new Set(this.updatingConfigIds);
    if (updating) {
      next.add(id);
    } else {
      next.delete(id);
    }
    this.updatingConfigIds = next;
  }

  private getConfigValuesList(onSettled?: () => void) {
    const settleCallback =
      typeof onSettled === 'function' ? onSettled : undefined;
    const api = new RefDataConfigApi(dorcApiConfiguration);
    api.refDataConfigGet().subscribe({
      next: (data: ConfigValueApiModel[]) => {
        this.setConfigValues(data);
        settleCallback?.();
      },
      error: (err: any) => {
        console.error(err);
        settleCallback?.();
      },
      complete: () => console.log('done loading config values')
    });
  }

  // `isAdmin` gates the checkbox columns and is in their dependency arrays,
  // so setting it is enough — the manual grid.requestContentUpdate() this used
  // to need is what the directive does.
  private loadRoles(): void {
    const api = new RefDataRolesApi(dorcApiConfiguration);
    api.refDataRolesGet().subscribe({
      next: (roles: string[]) => {
        this.isAdmin = roles.find(p => p === 'Admin') !== undefined;
      },
      error: err => {
        console.error('Failed to load roles', err);
        this.isAdmin = false;
      }
    });
  }

  private updateConfigItem(updated: ConfigValueApiModel): void {
    const api = new RefDataConfigApi(dorcApiConfiguration);
    const id = updated.Id;

    if (id == null) {
      console.error(
        `Missing Id on ConfigValueApiModel; keys: ${Object.keys(updated)}`
      );
      return;
    }

    if (this.updatingConfigIds.has(id)) {
      return;
    }

    this.setRowUpdating(id, true);

    api.refDataConfigPut({ id, configValueApiModel: updated }).subscribe({
      next: () => {
        this.getConfigValuesList(() => this.setRowUpdating(id, false));
      },
      error: (err: any) => {
        console.error('Update failed', err);
        this.getConfigValuesList(() => this.setRowUpdating(id, false));
      }
    });
  }

  static get styles() {
    return css`
      :host {
        display: flex;
        flex-direction: column;
        height: 100%;
        overflow: hidden;
      }
      vaadin-grid#grid {
        flex: 1;
        min-height: 0;
        --divider-color: var(--dorc-border-color);
        width: 100%;
      }

      vaadin-text-field {
        padding: 0px;
        margin: 0px;
      }

      vaadin-grid-cell-content {
        padding-top: 0px;
        padding-bottom: 0px;
        margin: 0px;
      }

      vaadin-dialog::part(overlay) {
        top: 16px;
        overflow: auto;
        max-width: calc(100vw - 32px);
      }

      .config-value-cell {
        display: flex;
        flex-direction: column;
        gap: var(--lumo-space-2xs);
      }

      .narrow-classifications {
        display: flex;
        flex-wrap: wrap;
        gap: var(--lumo-space-s);
        padding-bottom: var(--lumo-space-2xs);
      }

      @media (max-width: 768px) {
        vaadin-grid-cell-content {
          white-space: normal;
          word-wrap: break-word;
          overflow-wrap: break-word;
        }
      }
    `;
  }

  render() {
    return html`
      <div style="display: inline">
        <vaadin-text-field
          style="padding-left: 5px; width: 50%;"
          placeholder="Search"
          @value-changed="${this.updateSearch}"
          clear-button-visible
          helper-text="Use | for multiple search terms"
        >
          <vaadin-icon slot="prefix" icon="vaadin:search"></vaadin-icon>
        </vaadin-text-field>
        <vaadin-button
          title="Add Config Value"
          style="width: 250px"
          @click="${this.addConfigValue}"
        >
          <vaadin-icon
            icon="vaadin:options"
            style="color: var(--dorc-link-color)"
          ></vaadin-icon>
          Add Config Value...
        </vaadin-button>
      </div>
      <vaadin-dialog
        ${ref(this.unsavedChanges.attach)}
        id="add-config-value-dialog"
        header-title="Add Config Value"
        draggable
        width="560px"
        .opened="${this.addConfigValueDialogOpened}"
        @opened-changed="${(e: DialogOpenedChangedEvent) => {
          this.addConfigValueDialogOpened = e.detail.value;
        }}"
        @unsaved-changes-discarded="${this.resetAddConfigValue}"
        ${dialogRenderer(this.renderAddConfigValue, [
          this.addConfigValueSequence
        ])}
        ${dialogFooterRenderer(this.renderAddConfigValueFooter, [])}
      ></vaadin-dialog>
      ${
        this.loading
          ? html` <dorc-spinner></dorc-spinner> `
          : html`
              <vaadin-grid
                id="grid"
                .items=${this.filteredConfigValues}
                column-reordering-allowed
                multi-sort
                theme="compact row-stripes no-row-borders no-border"
              >
                <vaadin-grid-sort-column
                  path="Key"
                  header="Config Name"
                  resizable
                  width="300px"
                  flex-grow="0"
                ></vaadin-grid-sort-column>
                <vaadin-grid-sort-column
                  path="Secure"
                  header="Is Secure"
                  resizable
                  width="100px"
                  flex-grow="0"
                  ${columnBodyRenderer(this.isSecuredRenderer, [
                    this.isAdmin,
                    this.updatingConfigIds,
                    this.editingDrafts
                  ])}
                  ?hidden="${this._narrowScreen}"
                ></vaadin-grid-sort-column>
                <vaadin-grid-sort-column
                  path="IsForProd"
                  header="Is For Prod"
                  resizable
                  width="100px"
                  flex-grow="0"
                  ${columnBodyRenderer(this.isForProdRenderer, [
                    this.isAdmin,
                    this.updatingConfigIds
                  ])}
                  ?hidden="${this._narrowScreen}"
                ></vaadin-grid-sort-column>
                <vaadin-grid-column
                  path="VisibleToScripts"
                  resizable
                  width="150px"
                  flex-grow="0"
                  ${columnHeaderRenderer(
                    this.visibleToScriptsHeaderRenderer,
                    []
                  )}
                  ${columnBodyRenderer(this.isVisibleToScriptsRenderer, [
                    this.isAdmin,
                    this.updatingConfigIds
                  ])}
                  ?hidden="${this._narrowScreen}"
                ></vaadin-grid-column>
                <vaadin-grid-column
                  header="Config Value"
                  ${columnBodyRenderer(this.variableValueControlsRenderer, [
                    this.isAdmin,
                    this._narrowScreen,
                    this.updatingConfigIds,
                    this.editingDrafts
                  ])}
                  resizable
                  flex-grow="1"
                ></vaadin-grid-column>
              </vaadin-grid>
            `
      }
    `;
  }

  firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);
    this.loadRoles();

    this.addEventListener(
      'config-value-created',
      this.configValueCreated as EventListener
    );

    this.addEventListener('config-value-deleted', () =>
      this.getConfigValuesList()
    );

    this.addEventListener('config-value-draft-changed', ((e: CustomEvent) => {
      const { id, draft } = e.detail as {
        id: number;
        draft: ConfigValueEditDraft | undefined;
      };
      if (id != null) {
        this.setEditingDraft(id, draft);
      }
    }) as EventListener);

    this.addEventListener('config-value-save-started', ((e: CustomEvent) => {
      const { id } = e.detail as { id: number };
      if (id != null) {
        this.setRowUpdating(id, true);
      }
    }) as EventListener);

    this.addEventListener('config-value-saved', ((e: CustomEvent) => {
      const { id } = e.detail as { id: number };
      this.getConfigValuesList(() => {
        if (id != null) {
          this.setRowUpdating(id, false);
        }
      });
    }) as EventListener);

    this.addEventListener('config-value-save-failed', ((e: CustomEvent) => {
      const { id } = e.detail as { id: number };
      this.getConfigValuesList(() => {
        if (id != null) {
          this.setRowUpdating(id, false);
        }
      });
    }) as EventListener);
  }

  private visibleToScriptsHeaderRenderer = () =>
    html`<vaadin-grid-sorter
      path="VisibleToScripts"
      title="${VISIBLE_TO_SCRIPTS_HEADER_TOOLTIP}"
      >Visible To Scripts</vaadin-grid-sorter
    >`;

  private renderAddConfigValue = () =>
    html`${keyed(
      this.addConfigValueSequence,
      html`<add-config-value></add-config-value>`
    )}`;

  private resetAddConfigValue = () => {
    this.addConfigValueSequence += 1;
  };

  private renderAddConfigValueFooter = () => html`
    <vaadin-button @click="${() => (this.addConfigValueDialogOpened = false)}"
      >Close</vaadin-button
    >
  `;

  configValueCreated() {
    this.getConfigValuesList();
    this.addConfigValueDialogOpened = false;
  }

  variableValueControlsRenderer(configValue: ConfigValueApiModel) {
    const rowUpdating = this.isRowUpdating(configValue);
    const draft =
      configValue.Id != null
        ? this.editingDrafts.get(configValue.Id)
        : undefined;
    return html`<div class="config-value-cell">
      <config-value-controls
        .value="${configValue}"
        .draftControlled="${true}"
        .draft="${draft}"
        ?disabled="${rowUpdating}"
      ></config-value-controls>
      ${
        this._narrowScreen
          ? html`<div
              class="narrow-classifications"
              role="group"
              aria-label="Classifications"
            >
              ${this.renderSecuredCheckbox(configValue, true)}
              ${this.renderForProdCheckbox(configValue, true)}
              ${this.renderVisibleToScriptsCheckbox(configValue, true)}
            </div>`
          : nothing
      }
    </div>`;
  }

  isSecuredRenderer(configValue: ConfigValueApiModel) {
    return this.renderSecuredCheckbox(configValue, false);
  }

  isForProdRenderer(configValue: ConfigValueApiModel) {
    return this.renderForProdCheckbox(configValue, false);
  }

  /**
   * Whether deployment scripts may read this value.
   *
   * The classification is only meaningful for secure values — a non-secure value is
   * ordinary configuration and is visible to scripts by definition, which is why the
   * server forces it true on creation. The checkbox follows that: readable for every
   * row, changeable only on the secure ones.
   *
   * Ticking this box is not the same as publishing the value. Values on the reserved-key
   * list are withheld from script scope whatever this says, and the server is where that
   * is decided — the list is not duplicated here, because a second copy of a security
   * decision is a second place for it to drift.
   */
  isVisibleToScriptsRenderer(configValue: ConfigValueApiModel) {
    return this.renderVisibleToScriptsCheckbox(configValue, false);
  }

  private renderSecuredCheckbox(
    configValue: ConfigValueApiModel,
    showLabel: boolean
  ) {
    const rowUpdating = this.isRowUpdating(configValue);
    const rowEditing = this.isRowEditing(configValue);
    return html`<vaadin-checkbox
      aria-label="Is Secure"
      .label="${showLabel ? 'Is Secure' : ''}"
      ?disabled="${!this.isAdmin || rowUpdating || rowEditing}"
      .checked="${live(Boolean(configValue.Secure))}"
      @change="${(e: Event) => {
        if (this.isRowUpdating(configValue) || this.isRowEditing(configValue)) {
          return;
        }
        const secure = (e.currentTarget as Checkbox).checked;
        this.updateConfigItem({
          ...configValue,
          Value: configValue.Secure ? undefined : configValue.Value,
          Secure: secure,
          VisibleToScripts: secure
            ? configValue.Secure
              ? Boolean(configValue.VisibleToScripts)
              : false
            : true
        });
      }}"
    ></vaadin-checkbox>`;
  }

  private renderForProdCheckbox(
    configValue: ConfigValueApiModel,
    showLabel: boolean
  ) {
    const rowUpdating = this.isRowUpdating(configValue);
    return html`<vaadin-checkbox
      aria-label="Is For Prod"
      .label="${showLabel ? 'Is For Prod' : ''}"
      ?disabled="${!this.isAdmin || rowUpdating}"
      .checked="${live(Boolean(configValue.IsForProd))}"
      @change="${(e: Event) => {
        if (this.isRowUpdating(configValue)) return;
        this.updateConfigItem({
          ...configValue,
          Value: undefined,
          VisibleToScripts: undefined,
          IsForProd: (e.currentTarget as Checkbox).checked
        });
      }}"
    ></vaadin-checkbox>`;
  }

  private renderVisibleToScriptsCheckbox(
    configValue: ConfigValueApiModel,
    showLabel: boolean
  ) {
    const rowUpdating = this.isRowUpdating(configValue);
    const tooltip = configValue.Secure
      ? 'Stored script-visibility classification for this secure value. Reserved DOrc operating credentials are always withheld from script scope even when checked.'
      : 'Non-secure values are visible to deployment scripts unless the key is a reserved DOrc operating credential.';
    return html`<vaadin-checkbox
      aria-label="Visible To Scripts"
      title="${tooltip}"
      .label="${showLabel ? 'Visible To Scripts' : ''}"
      ?disabled="${!this.isAdmin || !configValue.Secure || rowUpdating}"
      .checked="${live(Boolean(configValue.VisibleToScripts))}"
      @change="${(e: Event) => {
        if (this.isRowUpdating(configValue)) return;
        this.updateConfigItem({
          ...configValue,
          Value: undefined,
          VisibleToScripts: (e.currentTarget as Checkbox).checked
        });
      }}"
    ></vaadin-checkbox>`;
  }

  private applySearchFilter() {
    if (!this.searchQuery) {
      this.filteredConfigValues = this.configValues;
      return;
    }

    const filters = this.searchQuery
      .trim()
      .split('|')
      .map((filter): ((value: string) => boolean) => {
        try {
          const regex = new RegExp(filter, 'i');
          return value => regex.test(value);
        } catch (err) {
          if (err instanceof SyntaxError) {
            const literal = filter.toLowerCase();
            return value => value.toLowerCase().includes(literal);
          }
          throw err;
        }
      });

    this.filteredConfigValues = this.configValues.filter(({ Key, Value }) =>
      filters.some(matches => matches(Key || '') || matches(Value || ''))
    );
  }

  updateSearch(e: CustomEvent) {
    this.searchQuery = (e.detail.value as string) || '';
    this.applySearchFilter();
  }

  setConfigValues(configValueApiModels: ConfigValueApiModel[]) {
    this.configValues = configValueApiModels;
    this.applySearchFilter();
    if (this.editingDrafts.size > 0) {
      const rowsById = new Map<number, ConfigValueApiModel>();
      for (const item of configValueApiModels) {
        if (item.Id != null) {
          rowsById.set(item.Id, item);
        }
      }
      let changed = false;
      const nextDrafts = new Map<number, ConfigValueEditDraft>();
      for (const [id, draft] of this.editingDrafts) {
        const refreshed = rowsById.get(id);
        if (refreshed && Boolean(refreshed.Secure) === draft.secure) {
          nextDrafts.set(id, draft);
        } else {
          changed = true;
        }
      }
      if (changed) {
        this.editingDrafts = nextDrafts;
      }
    }
    this.loading = false;
  }

  addConfigValue() {
    this.addConfigValueDialogOpened = true;
  }
}
