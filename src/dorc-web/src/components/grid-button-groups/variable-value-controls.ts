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
import { styleMap } from 'lit/directives/style-map.js';
import { PropertyValuesApi } from '../../apis/dorc-api';
import type { PropertyValueDto } from '../../apis/dorc-api';
import { Response } from '../../apis/dorc-api';
import '../../icons/editor-icons.js';
import '../../icons/iron-icons.js';

@customElement('variable-value-controls')
export class VariableValueControls extends LitElement {
  @property({ type: Object })
  value!: PropertyValueDto;

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
    const editStyles = {
      color: this.value.UserEditable ? 'cornflowerblue' : 'grey'
    };
    const deleteStyles = {
      color: this.value.UserEditable ? '#FF3131' : 'grey'
    };
    return html`
      ${this.value?.Property?.Secure
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
        ?disabled="${!this.value.UserEditable}"
        ?hidden="${this.editHidden}"
      >
        <vaadin-icon
          icon="editor:mode-edit"
          style=${styleMap(editStyles)}
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
        @click="${this.removePropertyValue}"
        ?disabled="${!this.value.UserEditable}"
      >
        <vaadin-icon
          icon="icons:clear"
          style=${styleMap(deleteStyles)}
        ></vaadin-icon>
      </vaadin-button>
      ${this.additionalInformation !== ''
        ? html`<div style="display: inline-block">
            ${this.additionalInformation}
          </div>`
        : html``}
    `;
  }

  removePropertyValue() {
    const answer = confirm(
      `Confirm removing value: ${this.value?.Value}?\nfor variable: ${
        this.value?.Property?.Name
      }\nwith scope: ${this.value?.PropertyValueFilter}`
    );
    if (answer && this.value?.Id) {
      if (this.value.PropertyValueFilter === '') {
        this.value.PropertyValueFilter = undefined;
        this.value.DefaultValue = true;
      }
      const api = new PropertyValuesApi();
      api
        .propertyValuesDelete({
          propertyValueDto: [this.value]
        })
        .subscribe({
          next: (value: Response[]) => {
            this.fireVariableValueDeletedEvent(value);
          },
          error: (err: any) => this.fireVariableValueDeletedEvent(err),
          complete: () => console.log('done deleting variable value')
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

  _editClick() {
    const textField = this.shadowRoot?.querySelector(
      `#propValue${this.value?.Id}`
    ) as unknown as TextField;
    if (this.value.Property?.Secure) {
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
    const api = new PropertyValuesApi();
    api
      .propertyValuesPut({
        propertyValueDto: [this.value]
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
