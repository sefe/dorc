import '@vaadin/button';
import { Button } from '@vaadin/button';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/icon';
import '@vaadin/password-field';
import { TextField } from '@vaadin/text-field';
import '@vaadin/vaadin-lumo-styles/icons.js';
import { css, LitElement } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { ConfigValueApiModel, RefDataConfigApi } from '../../apis/dorc-api';
import '../../icons/editor-icons.js';
import '../../icons/iron-icons.js';

@customElement('config-value-controls')
export class ConfigValueControls extends LitElement {
  @property({ type: Object })
  value!: ConfigValueApiModel;

  @property({ type: Boolean }) editHidden = false;

  @property({ type: Boolean }) saveHidden = true;

  @property({ type: Boolean }) cancelHidden = true;

  @property({ type: String }) additionalInformation = '';

  static get styles() {
    return css`
      vaadin-button {
        padding: 0px;
        margin: 0px;
      }
      vaadin-button:disabled,
      vaadin-button[disabled] {
        background-color: #dde2e8;
      }
    `;
  }

  render() {
    return html`
      ${this.value?.Secure
        ? html`<vaadin-password-field
            id="${`propValue${this.value?.Id}`}"
            value="Ex@mplePassw0rd"
            reveal-button-hidden
            readonly
            focus-target
            @value-changed="${(e: CustomEvent) => {
              const textField = e.detail as TextField;
              if (this.value) this.value.Value = textField.value;
            }}"
            style="width: 720px"
          ></vaadin-password-field>`
        : html` <vaadin-text-field
            id="${`propValue${this.value?.Id}`}"
            readonly
            focus-target
            .value="${this.value?.Value ?? ''}"
            @value-changed="${(e: CustomEvent) => {
              const textField = e.detail as TextField;
              if (this.value) this.value.Value = textField.value;
            }}"
            style="width: 720px"
          ></vaadin-text-field>`}

      <vaadin-button
        id="edit"
        title="Edit"
        theme="icon"
        @click="${this._editClick}"
        ?hidden="${this.editHidden}"
      >
        <vaadin-icon
          icon="editor:mode-edit"
          style="color: cornflowerblue"
        ></vaadin-icon>
      </vaadin-button>
      <vaadin-button
        aria-label="Save"
        theme="primary"
        focus-target
        ?hidden="${this.saveHidden}"
        @click="${this._saveClick}"
        >Save</vaadin-button
      >
      <vaadin-button
        aria-label="Cancel"
        ?hidden="${this.cancelHidden}"
        @click="${this._cancelClick}"
        >Cancel</vaadin-button
      >
      <vaadin-button
        title="Delete Value"
        theme="icon"
        @click="${this.removeConfigValue}"
      >
        <vaadin-icon icon="icons:clear" style="color: #FF3131"></vaadin-icon>
      </vaadin-button>
      ${this.additionalInformation !== ''
        ? html`<div style="display: inline-block">
            ${this.additionalInformation}
          </div>`
        : html``}
    `;
  }

  removeConfigValue() {
    const answer = confirm(
      `Confirm removing value: ${this.value?.Key}?\nfor variable: ${
        this.value?.Value
      }`
    );
    if (answer && this.value?.Id) {
      const api = new RefDataConfigApi();
      api
        .refDataConfigDelete({
          id: this.value.Id
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

  _editClick() {
    const textField = this.shadowRoot?.querySelector(
      `#propValue${this.value?.Id}`
    ) as unknown as TextField;
    if (this.value.Secure) {
      textField.value = '';
    }
    textField.readonly = false;
    textField.focus();
    this.updateButtonsVisibility(true);
  }

  updateButtonsVisibility(editing: boolean) {
    this.editHidden = editing;
    this.saveHidden = !editing;
    this.cancelHidden = !editing;
  }

  _cancelClick() {
    this.updateButtonsVisibility(false);
    const edit = this.shadowRoot?.getElementById('edit') as Button;
    edit.focus();

    const textField = this.shadowRoot?.querySelector(
      `#propValue${this.value?.Id}`
    ) as unknown as TextField;
    if (textField) textField.readonly = true;
  }

  _saveClick() {
    const api = new RefDataConfigApi();
    api
      .refDataConfigPut({
        id: this.value.Id,
        configValueApiModel: this.value
      })
      .subscribe(() => {
        this._cancelClick();

        const textField = this.shadowRoot?.querySelector(
          `#propValue${this.value?.Id}`
        ) as unknown as TextField;
        if (textField) textField.readonly = true;
      });
  }
}
