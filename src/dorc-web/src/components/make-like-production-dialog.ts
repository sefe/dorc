import { css, LitElement, PropertyValues } from 'lit';
import '@vaadin/checkbox';
import '@vaadin/button';
import '@vaadin/combo-box';
import { customElement, property, state } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import {
  MakeLikeProdApi,
  PropertyApiModel,
  RequestProperty
} from '../apis/dorc-api';
import { dialogFooterRenderer, dialogRenderer } from '@vaadin/dialog/lit';
import { DialogOpenedChangedEvent } from '@vaadin/dialog';
import './make-like-production';

@customElement('make-like-production-dialog')
export class MakeLikeProductionDialog extends LitElement {
  get mappedProjects(): string[] | undefined {
    return this._mappedProjects;
  }

  @property({ type: Array })
  set mappedProjects(value: string[] | undefined) {
    if (!value) {
      return;
    }

    this._mappedProjects = value;
  }

  private _mappedProjects: string[] | undefined;

  @property({ type: String }) private targetEnv: string | undefined;

  @state() private canSubmit = false;

  @property({ type: Array }) propertyOverrides: RequestProperty[] = [];

  @property({ type: Array }) properties: PropertyApiModel[] | undefined;

  private selectedDataBackup: string | undefined;

  private selectedBundleName: string | undefined;

  @state()
  private bundleRequestDialogOpened = false;

  @state()
  private loading = false;

  static get styles() {
    return css`
      .block {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 100%;
      }

      .loader {
        border: 2px solid #f3f3f3; /* Light grey */
        border-top: 2px solid #3498db; /* Blue */
        border-radius: 50%;
        width: 12px;
        height: 12px;
        animation: spin 2s linear infinite;
      }

      @keyframes spin {
        0% {
          transform: rotate(0deg);
        }
        100% {
          transform: rotate(360deg);
        }
      }

      .inline {
        display: inline-block;
        vertical-align: top;
      }

      .buttons {
        font-size: 10px;
        color: cornflowerblue;
        text-align: justify;
      }
    `;
  }

  private renderDialog = () => html`
    <make-like-production
      id="make-like-production"
      .mappedProjects="${this.mappedProjects}"
      .dialog="${this}"
    ></make-like-production>
  `;

  private renderFooter = () => html`
    ${this.loading ? html` <div class="loader"></div> ` : html``}
    <vaadin-button @click="${this.close}">Cancel</vaadin-button>
    <vaadin-button
      theme="primary"
      @click="${this.makeLikeProd}"
      .disabled="${!this.canSubmit}"
      >Queue
    </vaadin-button>
  `;

  private close() {
    this.bundleRequestDialogOpened = false;
  }

  render() {
    return html`
      <vaadin-dialog
        id="dialog"
        header-title="Queue Bundle Request Set"
        .opened="${this.bundleRequestDialogOpened}"
        resizable
        draggable
        @opened-changed="${(event: DialogOpenedChangedEvent) => {
          this.bundleRequestDialogOpened = event.detail.value;
        }}"
        ${dialogRenderer(this.renderDialog, [])}
        ${dialogFooterRenderer(this.renderFooter, [
          this.loading,
          this.canSubmit
        ])}
      ></vaadin-dialog>
    `;
  }

  public closeDialog() {
    this.bundleRequestDialogOpened = false;
  }

  _canSubmit() {
    if (this.selectedDataBackup !== '' && this.selectedBundleName !== '') {
      this.canSubmit = true;
    }
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);
  }

  mlpIsLoading() {
    this.loading = true;
  }

  mlpIsntLoading() {
    this.loading = false;
  }

  makeLikeProd() {
    console.log('making like prod');
    this.loading = true;
    this.canSubmit = false;
    const api = new MakeLikeProdApi();
    api
      .makeLikeProdPut({
        makeLikeProdRequest: {
          BundleName: this.selectedBundleName,
          TargetEnv: this.targetEnv,
          DataBackup: this.selectedDataBackup,
          BundleProperties: this.propertyOverrides
        }
      })
      .subscribe({
        next: () => {
          console.log('done triggering MLP');
          const event = new CustomEvent('close-mlp-dialog', {
            detail: {},
            bubbles: true,
            composed: true
          });
          this.dispatchEvent(event);
        },
        error: (err: any) => {
          console.error(err);
          const result = err.response as string;
          const event = new CustomEvent('error-alert', {
            detail: {
              description: 'Unable to Trigger Make Like Prod: ',
              result
            },
            bubbles: true,
            composed: true
          });
          this.dispatchEvent(event);
          this.loading = false;
          this.canSubmit = true;
        },
        complete: () => {
          this.loading = false;
          this.canSubmit = true;
        }
      });
  }

  Open() {
    this.bundleRequestDialogOpened = true;
  }

  public backupChanged(e: string | undefined) {
    this.selectedDataBackup = e;
    this._canSubmit();
  }

  public bundleChanged(e: string | undefined) {
    this.selectedBundleName = e;
    this._canSubmit();
  }

  public propertyAdded(propertyOverride: RequestProperty) {
    this.propertyOverrides.push(propertyOverride);
  }

  public propertyRemoved(propertyOverride: RequestProperty) {
    this.propertyOverrides = this.propertyOverrides.filter(
      val => val.PropertyName != propertyOverride.PropertyName
    );
    this.propertyOverrides = JSON.parse(JSON.stringify(this.propertyOverrides));
  }
}
