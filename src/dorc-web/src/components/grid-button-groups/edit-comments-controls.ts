import { Button } from '@vaadin/button';
import { GridItemModel } from '@vaadin/grid';
import '@vaadin/grid/vaadin-grid';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/icons/vaadin-icons';
import '@vaadin/vaadin-lumo-styles/icons.js';
import { css, LitElement } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { RefDataEnvironmentsHistoryApi } from '../../apis/dorc-api/apis';
import { EnvironmentHistoryApiModel } from '../../apis/dorc-api/models';

@customElement('edit-comments-controls')
export class EditCommentsControls extends LitElement {
  @property({ type: Object }) model:
    | GridItemModel<EnvironmentHistoryApiModel>
    | undefined;

  @property({ type: Boolean }) editHidden = false;

  @property({ type: Boolean }) saveHidden = true;

  @property({ type: Boolean }) cancelHidden = true;

  static get styles() {
    return css`
      vaadin-button {
        padding: 0px;
        margin: 2px;
      }
    `;
  }

  render() {
    return html`
      <div style="text-align: right">
        <vaadin-button
          id="edit"
          aria-label="Edit"
          theme="icon"
          ?hidden="${this.editHidden}"
          focus-target
          @click="${this._editClick}"
        >
          <vaadin-icon icon="lumo:edit"></vaadin-icon>
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
      </div>
    `;
  }

  updateButtonsVisibility(editing: boolean) {
    this.editHidden = editing;
    this.saveHidden = !editing;
    this.cancelHidden = !editing;
  }

  _editClick() {
    const event = new CustomEvent('env-history-comments-edit', {
      detail: {
        Model: this.model
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);

    this.updateButtonsVisibility(true);
  }

  _saveClick() {
    const envHistory = this.model?.item as EnvironmentHistoryApiModel;
    const api = new RefDataEnvironmentsHistoryApi();
    api
      .refDataEnvironmentsHistoryPut({ environmentHistoryApiModel: envHistory })
      .subscribe(
        () => {
          this._cancelClick();

          const event = new CustomEvent('env-history-comments-save', {
            detail: {
              Model: this.model
            },
            bubbles: true,
            composed: true
          });
          this.dispatchEvent(event);
        },

        (err: any) => console.error(err),
        () => console.log('done saving env history comment')
      );
  }

  _cancelClick() {
    const event = new CustomEvent('env-history-comments-cancel', {
      detail: {
        Model: this.model
      },
      bubbles: true,
      composed: true
    });
    this.dispatchEvent(event);

    this.updateButtonsVisibility(false);
    const edit = this.shadowRoot?.getElementById('edit') as Button;
    edit.focus();
  }
}
