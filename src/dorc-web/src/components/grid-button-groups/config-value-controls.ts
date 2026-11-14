import { confirmPrompt } from '../confirm-prompt';
import '@vaadin/button';
import { Button } from '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/password-field';
import { TextField } from '@vaadin/text-field';
import '@vaadin/vaadin-lumo-styles/icons.js';
import { css, LitElement } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { live } from 'lit/directives/live.js';
import { html } from 'lit/html.js';
import { ConfigValueApiModel, RefDataConfigApi } from '../../apis/dorc-api';
import '../../icons/editor-icons.js';
import '../../icons/iron-icons.js';
import '@vaadin/text-field';
import '@vaadin/tooltip';
import { dorcApiConfiguration } from '../../services/dorc-api-configuration';

export interface ConfigValueEditDraft {
  value: string;
  dirty: boolean;
  secure: boolean;
}

const SECURE_MASK = 'Ex@mplePassw0rd';

@customElement('config-value-controls')
export class ConfigValueControls extends LitElement {
  @property({ type: Object })
  value!: ConfigValueApiModel;

  @property({ type: Boolean }) disabled = false;

  @property({ type: Boolean }) draftControlled = false;

  @property({ attribute: false }) draft: ConfigValueEditDraft | undefined =
    undefined;

  @property({ type: String }) additionalInformation = '';

  @state() private localDraft:
    (ConfigValueEditDraft & { id: number | undefined }) | undefined = undefined;

  @state() private saving = false;

  private get activeDraft(): ConfigValueEditDraft | undefined {
    const secure = Boolean(this.value?.Secure);
    if (this.draftControlled) {
      return this.draft && this.draft.secure === secure
        ? this.draft
        : undefined;
    }
    if (
      this.localDraft &&
      this.localDraft.id === this.value?.Id &&
      this.localDraft.secure === secure
    ) {
      return this.localDraft;
    }
    return undefined;
  }

  get editHidden(): boolean {
    return this.activeDraft !== undefined;
  }

  get saveHidden(): boolean {
    return this.activeDraft === undefined;
  }

  get cancelHidden(): boolean {
    return this.activeDraft === undefined;
  }

  private get isBusy(): boolean {
    return this.disabled || this.saving;
  }

  static get styles() {
    return css`
      :host {
        display: inline-flex;
        align-items: center;
        flex-wrap: nowrap;
        gap: var(--lumo-space-xs);
        max-width: 100%;
      }
      .value-input {
        width: 720px;
        max-width: calc(100vw - 160px);
      }
      vaadin-button {
        padding: 0px;
        margin: 0px;
      }
      vaadin-button:disabled,
      vaadin-button[disabled] {
        background-color: var(--dorc-border-color);
      }
    `;
  }

  render() {
    const draft = this.activeDraft;
    const editing = draft !== undefined;
    const busy = this.isBusy;
    const secureDisplayValue = editing ? draft.value : SECURE_MASK;
    const plainDisplayValue = editing ? draft.value : (this.value?.Value ?? '');

    return html`
      ${
        this.value?.Secure
          ? html`<vaadin-password-field
              class="value-input"
              id="${`propValue${this.value?.Id}`}"
              reveal-button-hidden
              ?readonly="${!editing || busy}"
              focus-target
              .value="${live(secureDisplayValue)}"
              @input="${this._onValueInput}"
            ></vaadin-password-field>`
          : html`<vaadin-text-field
              class="value-input"
              id="${`propValue${this.value?.Id}`}"
              ?readonly="${!editing || busy}"
              focus-target
              .value="${live(plainDisplayValue)}"
              @input="${this._onValueInput}"
            ></vaadin-text-field>`
      }

      <vaadin-button
        id="edit"
        aria-label="Edit"
        theme="icon"
        ?disabled="${busy}"
        @click="${this._editClick}"
        ?hidden="${editing}"
      >
        <vaadin-tooltip slot="tooltip" text="Edit"></vaadin-tooltip>
        <vaadin-icon
          icon="editor:mode-edit"
          style="color: var(--dorc-link-color)"
        ></vaadin-icon>
      </vaadin-button>
      <vaadin-button
        aria-label="Save"
        theme="primary"
        focus-target
        ?disabled="${busy}"
        ?hidden="${!editing}"
        @click="${this._saveClick}"
        >Save</vaadin-button
      >
      <vaadin-button
        aria-label="Cancel"
        ?disabled="${busy}"
        ?hidden="${!editing}"
        @click="${this._cancelClick}"
        >Cancel</vaadin-button
      >
      <vaadin-button
        aria-label="Delete Value"
        theme="icon"
        ?disabled="${busy}"
        @click="${this.removeConfigValue}"
      >
        <vaadin-tooltip slot="tooltip" text="Delete Value"></vaadin-tooltip>
        <vaadin-icon
          icon="icons:clear"
          style="color: var(--dorc-error-color)"
        ></vaadin-icon>
      </vaadin-button>
      ${
        this.additionalInformation !== ''
          ? html`<div style="display: inline-block">
              ${this.additionalInformation}
            </div>`
          : html``
      }
    `;
  }

  async removeConfigValue() {
    if (this.isBusy) return;
    // Snapshot before awaiting: this control sits in a recycled grid cell, so
    // `this.value` can belong to a different row by the time the user answers.
    const configValue = this.value;
    const answer = await confirmPrompt(
      `Confirm removing value: ${configValue?.Key}?\nfor variable: ${
        configValue?.Value
      }`
    );
    if (answer && configValue?.Id) {
      const api = new RefDataConfigApi(dorcApiConfiguration);
      api
        .refDataConfigDelete({
          id: configValue.Id
        })
        .subscribe({
          next: (value: boolean) => {
            this.fireVariableValueDeletedEvent(value);
          },
          error: (err: any) => this.fireVariableValueDeletedEvent(err),
          complete: () => console.log('done deleting variable value')
        });
    }
  }

  private fireVariableValueDeletedEvent(data: any) {
    const event = new CustomEvent('config-value-deleted', {
      detail: {
        data
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }

  private setDraftState(nextDraft: ConfigValueEditDraft | undefined) {
    const id = this.value?.Id;
    this.localDraft = nextDraft ? { id, ...nextDraft } : undefined;
    if (id != null) {
      this.dispatchEvent(
        new CustomEvent('config-value-draft-changed', {
          detail: { id, draft: nextDraft },
          bubbles: true,
          composed: true
        })
      );
    }
  }

  private _onValueInput(e: Event) {
    if (this.activeDraft === undefined || this.isBusy) return;
    const textField = e.currentTarget as TextField;
    this.setDraftState({
      value: textField.value ?? '',
      dirty: true,
      secure: Boolean(this.value?.Secure)
    });
  }

  _editClick() {
    if (this.isBusy || !this.value) return;
    const secure = Boolean(this.value.Secure);
    const initialDraft: ConfigValueEditDraft = {
      value: secure ? '' : (this.value.Value ?? ''),
      dirty: false,
      secure
    };
    this.setDraftState(initialDraft);

    const textField = this.shadowRoot?.querySelector(
      `#propValue${this.value?.Id}`
    ) as unknown as TextField | null;
    if (textField) {
      textField.value = initialDraft.value;
      textField.readonly = false;
      textField.focus();
    }
  }

  private exitEditMode() {
    const secure = Boolean(this.value?.Secure);
    const restoredValue = secure ? SECURE_MASK : (this.value?.Value ?? '');
    this.setDraftState(undefined);

    const textField = this.shadowRoot?.querySelector(
      `#propValue${this.value?.Id}`
    ) as unknown as TextField | null;
    if (textField) {
      textField.value = restoredValue;
      textField.readonly = true;
    }

    const edit = this.shadowRoot?.getElementById('edit') as Button | null;
    edit?.focus();
  }

  _cancelClick() {
    if (this.saving) return;
    this.exitEditMode();
  }

  _saveClick() {
    if (this.isBusy || !this.value || this.value.Id == null) return;

    const currentItem = this.value;
    const id = currentItem.Id!;
    const draft = this.activeDraft;
    if (!draft) return;

    if (currentItem.Secure) {
      if (!draft.dirty || draft.value.length === 0) {
        this.exitEditMode();
        return;
      }
    } else if (!draft.dirty || draft.value === (currentItem.Value ?? '')) {
      this.exitEditMode();
      return;
    }

    this.saving = true;
    this.dispatchEvent(
      new CustomEvent('config-value-save-started', {
        detail: { id },
        bubbles: true,
        composed: true
      })
    );

    const api = new RefDataConfigApi(dorcApiConfiguration);
    api
      .refDataConfigPut({
        id,
        configValueApiModel: {
          ...currentItem,
          Value: draft.value,
          VisibleToScripts: undefined
        }
      })
      .subscribe({
        next: () => {
          this.saving = false;
          if (this.value?.Id === id) {
            this.exitEditMode();
          } else {
            this.localDraft = undefined;
            this.dispatchEvent(
              new CustomEvent('config-value-draft-changed', {
                detail: { id, draft: undefined },
                bubbles: true,
                composed: true
              })
            );
          }
          this.dispatchEvent(
            new CustomEvent('config-value-saved', {
              detail: { id },
              bubbles: true,
              composed: true
            })
          );
        },
        error: (err: any) => {
          console.error('Failed to save config value', err);
          this.saving = false;
          this.dispatchEvent(
            new CustomEvent('config-value-save-failed', {
              detail: { id },
              bubbles: true,
              composed: true
            })
          );
        }
      });
  }
}
