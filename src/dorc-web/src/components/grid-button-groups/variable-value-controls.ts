import { confirmPrompt } from '../confirm-prompt';
import '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/password-field';
import { TextField } from '@vaadin/text-field';
import '@vaadin/vaadin-lumo-styles/icons.js';
import { css, LitElement } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { live } from 'lit/directives/live.js';
import { styleMap } from 'lit/directives/style-map.js';
import { PropertyValuesApi } from '../../apis/dorc-api';
import type {
  PropertyUsageReferenceApiModel,
  PropertyValueDto
} from '../../apis/dorc-api';
import { Response } from '../../apis/dorc-api';
import '../../icons/editor-icons.js';
import '../../icons/iron-icons.js';
import { Notification } from '@vaadin/notification';
import '@vaadin/text-field';
import '@vaadin/tooltip';
import { dorcApiConfiguration } from '../../services/dorc-api-configuration';
import { defaultPropertyResolutionClientCache } from '../../variable-resolution/property-resolution-client-cache';
import { inspectUnescapedDollars } from '../../variable-resolution/resolution-presentation';

export type PropertyUsageReference = PropertyUsageReferenceApiModel;

@customElement('variable-value-controls')
export class VariableValueControls extends LitElement {
  @property({ type: Object })
  value!: PropertyValueDto;

  @property({ type: Boolean }) editing = false;

  @property({ type: String }) additionalInformation = '';

  private _originalValueBeforeEdit: string | null | undefined;

  private _originalValueId: number | undefined;

  private _whereUsedReferences: PropertyUsageReference[] | null = null;

  private _whereUsedLoading = false;

  private _whereUsedForPropertyName: string | undefined;

  private _whereUsedForValueId: number | undefined;

  private _whereUsedGeneration = 0;

  static get styles() {
    return css`
      :host {
        display: flex;
        align-items: center;
        flex-wrap: nowrap;
        gap: 6px;
        width: 100%;
        padding-block: 2px;
      }
      vaadin-text-field,
      vaadin-password-field {
        flex: 1;
        min-width: 0;
        --lumo-text-field-size: var(--lumo-size-s, 30px);
      }
      vaadin-text-field::part(value) {
        font-family: var(--lumo-font-family-monospace, monospace);
        font-size: var(--lumo-font-size-xs);
      }
      vaadin-text-field[readonly]::part(input-field),
      vaadin-password-field[readonly]::part(input-field) {
        background-color: transparent;
        border: 1px solid transparent;
      }
      vaadin-text-field:not([readonly])::part(input-field),
      vaadin-password-field:not([readonly])::part(input-field) {
        border: 1px solid var(--dorc-icon-interactive, var(--dorc-link-color));
      }
      vaadin-button {
        padding: 0px;
        margin: 0px;
        flex: 0 0 auto;
      }
      vaadin-button:disabled,
      vaadin-button[disabled] {
        opacity: 0.38;
        background-color: transparent;
      }
      .where-used-badge {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        max-width: 260px;
        padding: 2px 8px;
        border-radius: 999px;
        font-size: var(--lumo-font-size-xxs, 11px);
        line-height: 1.3;
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
        background: var(--lumo-contrast-10pct, rgba(0, 0, 0, 0.06));
        color: var(--dorc-text-primary, inherit);
        border: 1px solid var(--dorc-border-color, rgba(0, 0, 0, 0.15));
        flex: 0 0 auto;
      }
      .where-used-badge.has-dependents {
        background: var(--lumo-primary-color-10pct, rgba(25, 118, 210, 0.12));
        color: var(--dorc-link-color, #1565c0);
        border-color: var(--dorc-link-color, #1565c0);
      }
    `;
  }

  protected override willUpdate(changedProperties: Map<string, unknown>) {
    if (changedProperties.has('value')) {
      const currentName = this.value?.Property?.Name ?? undefined;
      const currentId = this.value?.Id;
      if (
        this._whereUsedForPropertyName !== currentName ||
        this._whereUsedForValueId !== currentId
      ) {
        this._whereUsedReferences = null;
        this._whereUsedLoading = false;
        this._whereUsedForPropertyName = currentName;
        this._whereUsedForValueId = currentId;
      }
    }
  }

  render() {
    const editStyles = {
      color: this.value.UserEditable
        ? 'var(--dorc-link-color)'
        : 'var(--dorc-text-secondary)'
    };
    const deleteStyles = {
      color: this.value.UserEditable
        ? 'var(--dorc-error-color)'
        : 'var(--dorc-text-secondary)'
    };
    return html`
      ${
        this.value?.Property?.Secure
          ? html`<vaadin-password-field
              id="${`propValue${this.value?.Id}`}"
              value="Ex@mplePassw0rd"
              reveal-button-hidden
              ?readonly="${!this.editing}"
              focus-target
              @keydown="${(e: KeyboardEvent) => {
                if (!this.editing) return;
                if (e.key === 'Enter') {
                  e.preventDefault();
                  this._saveClick();
                } else if (e.key === 'Escape') {
                  e.preventDefault();
                  this._cancelClick();
                }
              }}"
              @value-changed="${(e: CustomEvent) => {
                if (!this.editing) return;
                const textField = e.detail as TextField;
                if (this.value) this.value.Value = textField.value;
              }}"
              style="padding: 0px"
            ></vaadin-password-field>`
          : html` <vaadin-text-field
              id="${`propValue${this.value?.Id}`}"
              ?readonly="${!this.editing}"
              focus-target
              .value="${live(this.value?.Value ?? '')}"
              @keydown="${(e: KeyboardEvent) => {
                if (!this.editing) return;
                if (e.key === 'Enter') {
                  e.preventDefault();
                  this._saveClick();
                } else if (e.key === 'Escape') {
                  e.preventDefault();
                  this._cancelClick();
                }
              }}"
              @value-changed="${(e: CustomEvent) => {
                if (!this.editing) return;
                const textField = e.detail as TextField;
                if (this.value && this.value.Value !== textField.value) {
                  this.value.Value = textField.value;
                  this.requestUpdate();
                }
              }}"
              style="padding: 0px"
            ></vaadin-text-field>`
      }
      ${
        this.editing && !this.value?.Property?.Secure
          ? this.renderInlineDollarEscapeButton()
          : html``
      }

      <vaadin-button
        id="edit"
        aria-label="Edit"
        theme="icon small"
        @click="${this._editClick}"
        ?disabled="${!this.value.UserEditable}"
        ?hidden="${this.editing}"
      >
        <vaadin-tooltip slot="tooltip" text="Edit"></vaadin-tooltip>
        <vaadin-icon
          icon="editor:mode-edit"
          style=${styleMap(editStyles)}
        ></vaadin-icon>
      </vaadin-button>
      <vaadin-button
        aria-label="Save"
        theme="primary"
        focus-target
        ?hidden="${!this.editing}"
        @click="${this._saveClick}"
        >Save</vaadin-button
      >
      <vaadin-button
        aria-label="Cancel"
        ?hidden="${!this.editing}"
        @click="${this._cancelClick}"
        >Cancel</vaadin-button
      >
      <vaadin-button
        aria-label="Delete Value"
        theme="icon small"
        @click="${this.removePropertyValue}"
        ?disabled="${!this.value.UserEditable}"
      >
        <vaadin-tooltip slot="tooltip" text="Delete Value"></vaadin-tooltip>
        <vaadin-icon
          icon="icons:clear"
          style=${styleMap(deleteStyles)}
        ></vaadin-icon>
      </vaadin-button>
      ${this.renderWhereUsedControl()}
      ${
        this.additionalInformation !== ''
          ? html`<div style="display: inline-block">
              ${this.additionalInformation}
            </div>`
          : html``
      }
    `;
  }

  private renderWhereUsedControl() {
    const propertyName = this.value?.Property?.Name?.trim();
    if (!propertyName) {
      return html``;
    }

    const references = this._whereUsedReferences;
    const hasDependents = Boolean(references && references.length > 0);
    const badgeSummary =
      references === null
        ? ''
        : references.length === 0
          ? `No other variables reference $${propertyName}$`
          : `Used by ${references.length}: ${references
              .map(
                ref =>
                  `$${ref.PropertyName}$ (${ref.Scope || ref.PropertyValueFilter || 'Default'})`
              )
              .join(', ')}`;
    const badgeTooltip =
      references && references.length > 0
        ? references
            .map(
              ref =>
                `$${ref.PropertyName}$ [${ref.Scope || ref.PropertyValueFilter || 'Default'}]: ${ref.RawValueSnippet ?? ''}`
            )
            .join('\n')
        : badgeSummary;

    return html`
      <vaadin-button
        id="where-used"
        aria-label="Where Used"
        theme="icon small"
        ?disabled="${this._whereUsedLoading}"
        @click="${this._inspectWhereUsed}"
      >
        <vaadin-tooltip
          slot="tooltip"
          text="Where Used (Impact Analysis for $${propertyName}$)"
        ></vaadin-tooltip>
        <vaadin-icon
          icon="vaadin:connect"
          style="color: var(--dorc-link-color)"
        ></vaadin-icon>
      </vaadin-button>
      ${
        references !== null
          ? html`<span
              class="where-used-badge ${hasDependents ? 'has-dependents' : ''}"
              role="status"
              title="${badgeTooltip}"
              >${badgeSummary}</span
            >`
          : html``
      }
    `;
  }

  private _inspectWhereUsed() {
    const propertyName = this.value?.Property?.Name?.trim();
    if (!propertyName || this._whereUsedLoading) {
      return;
    }

    if (
      this._whereUsedReferences !== null &&
      this._whereUsedForPropertyName === propertyName
    ) {
      this._whereUsedReferences = null;
      this.requestUpdate();
      return;
    }

    const valueId = this.value?.Id;
    const generation = ++this._whereUsedGeneration;
    this._whereUsedLoading = true;
    this._whereUsedForPropertyName = propertyName;
    this._whereUsedForValueId = valueId;
    this.requestUpdate();

    const api = new PropertyValuesApi(dorcApiConfiguration);
    api.propertyValuesWhereUsedGet({ propertyName }).subscribe({
      next: (payload: PropertyUsageReference[]) => {
        if (
          generation !== this._whereUsedGeneration ||
          this.value?.Id !== valueId ||
          this.value?.Property?.Name?.trim() !== propertyName
        ) {
          return;
        }
        const references: PropertyUsageReference[] = Array.isArray(payload)
          ? payload
          : [];
        this._whereUsedReferences = references;
        this._whereUsedLoading = false;
        this.requestUpdate();

        const message =
          references.length === 0
            ? `No other variables reference $${propertyName}$`
            : `$${propertyName}$ is referenced by ${references.length} variable value(s): ${references
                .map(
                  ref =>
                    `$${ref.PropertyName ?? ''}$ (${ref.Scope || ref.PropertyValueFilter || 'Default'})`
                )
                .join(', ')}`;
        Notification.show(message, {
          theme: references.length > 0 ? 'primary' : 'contrast',
          position: 'bottom-start',
          duration: 5000
        });
      },
      error: (err: any) => {
        if (
          generation !== this._whereUsedGeneration ||
          this.value?.Id !== valueId ||
          this.value?.Property?.Name?.trim() !== propertyName
        ) {
          return;
        }
        this._whereUsedLoading = false;
        this.requestUpdate();
        Notification.show(
          err?.message ?? `Unable to inspect references for $${propertyName}$.`,
          {
            theme: 'error',
            position: 'bottom-start',
            duration: 5000
          }
        );
      }
    });
  }

  async removePropertyValue() {
    // Snapshot before awaiting: this control sits in a recycled grid cell, so
    // `this.value` can belong to a different row by the time the user answers.
    const propertyValue = this.value;
    const answer = await confirmPrompt(
      `Confirm removing value: ${propertyValue?.Value}?\nfor variable: ${
        propertyValue?.Property?.Name
      }\nwith scope: ${propertyValue?.PropertyValueFilter}`
    );
    if (answer && propertyValue?.Id) {
      if (propertyValue.PropertyValueFilter === '') {
        propertyValue.PropertyValueFilter = undefined;
        propertyValue.DefaultValue = true;
      }
      const api = new PropertyValuesApi(dorcApiConfiguration);
      api
        .propertyValuesDelete({
          propertyValueDto: [propertyValue]
        })
        .subscribe({
          next: (value: Response[]) => {
            this.fireVariableValueDeletedEvent(value);
          },
          error: (err: any) => this.fireVariableValueDeletedEvent(err),
          complete: () => {
            console.log('done deleting variable value');
            this.showSuccessMessage('Variable value deleted successfully!');
          }
        });
    }
  }

  private fireVariableValueDeletedEvent(data: any) {
    const event = new CustomEvent('variable-value-deleted', {
      detail: {
        data
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);
  }

  private renderInlineDollarEscapeButton() {
    const inspection = inspectUnescapedDollars(this.value?.Value ?? '');
    if (inspection.hasBraceSyntax) {
      return html`<vaadin-button
        theme="small tertiary"
        aria-label="Convert to $Variable$"
        title="Convert \${VariableName} to $VariableName$"
        @click="${() => {
          if (!this.value) return;
          this.value.Value = inspection.braceConvertedSuggestion;
          const textField = this.shadowRoot?.querySelector(
            `#propValue${this.value?.Id}`
          ) as unknown as TextField | null;
          if (textField) {
            textField.value = inspection.braceConvertedSuggestion;
          }
          this.requestUpdate();
        }}"
        >Use $Variable$</vaadin-button
      >`;
    }
    if (!inspection.hasUnclosedDollar) return html``;
    return html`<vaadin-button
      theme="small tertiary"
      aria-label="Escape literal $ as $$"
      title="${
        inspection.hasOtherPropertyTokens
          ? 'Unescaped $ prevents $Variable$ expansion — click to escape as $$'
          : 'Escape literal $ (e.g. E$) as $$'
      }"
      @click="${() => {
        if (!this.value) return;
        this.value.Value = inspection.escapedSuggestion;
        const textField = this.shadowRoot?.querySelector(
          `#propValue${this.value?.Id}`
        ) as unknown as TextField | null;
        if (textField) {
          textField.value = inspection.escapedSuggestion;
        }
        this.requestUpdate();
      }}"
      >Escape $ → $$</vaadin-button
    >`;
  }

  _editClick() {
    this._originalValueBeforeEdit = this.value?.Value;
    this._originalValueId = this.value?.Id;
    const textField = this.shadowRoot?.querySelector(
      `#propValue${this.value?.Id}`
    ) as unknown as TextField;
    if (this.value.Property?.Secure) {
      textField.value = '';
    }
    textField.focus();
    this.dispatchEvent(
      new CustomEvent('editing-started', {
        detail: { id: this.value?.Id },
        bubbles: true,
        composed: true
      })
    );
  }

  updateButtonsVisibility(editing: boolean) {
    this.editing = editing;
  }

  private resetSecureFieldIfNeeded() {
    if (!this.value?.Property?.Secure) return;
    this.value.Value = '';
    const textField = this.shadowRoot?.querySelector(
      `#propValue${this.value?.Id}`
    ) as unknown as TextField | null;
    if (textField) {
      textField.value = 'Ex@mplePassw0rd';
    }
  }

  _cancelClick() {
    if (this.value?.Property?.Secure) {
      this.resetSecureFieldIfNeeded();
    } else if (
      this.value &&
      this._originalValueBeforeEdit !== undefined &&
      this._originalValueId === this.value.Id
    ) {
      this.value.Value = this._originalValueBeforeEdit;
      const textField = this.shadowRoot?.querySelector(
        `#propValue${this.value?.Id}`
      ) as unknown as TextField | null;
      if (textField) {
        textField.value = this._originalValueBeforeEdit ?? '';
      }
    }
    this.dispatchEvent(
      new CustomEvent('editing-cancelled', {
        detail: { id: this.value?.Id },
        bubbles: true,
        composed: true
      })
    );
  }

  _saveClick() {
    const api = new PropertyValuesApi(dorcApiConfiguration);
    api
      .propertyValuesPut({
        propertyValueDto: [this.value]
      })
      .subscribe({
        next: (responses: Response[]) => {
          if (responses[0]?.Status !== 'success') {
            Notification.show(
              responses[0]?.Status ?? 'Unable to update variable value.',
              {
                theme: 'error',
                position: 'bottom-start',
                duration: 5000
              }
            );
            return;
          }

          defaultPropertyResolutionClientCache.clear();
          this.resetSecureFieldIfNeeded();
          this.dispatchEvent(
            new CustomEvent('variable-value-updated', {
              detail: { data: responses },
              bubbles: true,
              composed: true
            })
          );
          this.dispatchEvent(
            new CustomEvent('editing-cancelled', {
              detail: { id: this.value?.Id },
              bubbles: true,
              composed: true
            })
          );
          this.showSuccessMessage('Variable value saved successfully!');
        },
        error: (err: any) => {
          Notification.show(
            err?.message ?? 'Unable to update variable value.',
            {
              theme: 'error',
              position: 'bottom-start',
              duration: 5000
            }
          );
        }
      });
  }

  private showSuccessMessage(text: string) {
    Notification.show(text, {
      theme: 'success',
      position: 'bottom-start',
      duration: 5000
    });
  }
}
