import { css, PropertyValues } from 'lit';
import * as ace from 'ace-builds';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../icons/iron-icons';
import { Notification } from '@vaadin/notification';
import { ErrorNotification } from '../components/notifications/error-notification';
import { PageElement } from '../helpers/page-element';
import { RefDataApi, RefDataApiModel } from '../apis/dorc-api';

let editorValue: string | undefined = '';
@customElement('page-project-ref-data')
export class PageProjectRefData extends PageElement {
  private editor: ace.Ace.Editor | undefined;

  static get styles() {
    return css`
      .btn {
        position: fixed;
        right: 40px;
        bottom: 60px;
        z-index: 999;

        height: 56px;
        width: 56px;
        border-radius: 50%;
        color: white;
        cursor: pointer;
        border: 0;
        background-color: cornflowerblue;
      }
      .btn:hover {
        background-color: RoyalBlue;
      }
      button:disabled,
      button[disabled] {
        background-color: #cccccc;
        color: #666666;
      }
      .loader {
        position: fixed;
        border: 10px solid #f3f3f3; /* Light grey */
        border-top: 10px solid #3498db; /* Blue */
        border-radius: 50%;
        width: 56px;
        height: 56px;
        animation: spin 2s linear infinite;
        z-index: 998;
        right: 30px;
        bottom: 50px;
      }
      @keyframes spin {
        0% {
          transform: rotate(0deg);
        }
        100% {
          transform: rotate(360deg);
        }
      }
    `;
  }

  private projectId: number | undefined;

  @property({ type: Boolean })
  private refDataLoading = false;

  render() {
    return html`
      <div id="editor" style="width: 100%; height: calc(100vh - 50px);">
        Loading...
      </div>
      <div class="loader" ?hidden="${!this.refDataLoading}"></div>
      <button
        class="btn"
        title="Save Reference Data"
        .disabled="${this.refDataLoading}"
        @click="${this.saveRefData}"
      >
        <vaadin-icon
          icon="icons:save"
          style="color: white; text-align: center;"
        ></vaadin-icon>
      </button>
    `;
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    const editorDiv = this.shadowRoot?.getElementById(
      'editor'
    ) as HTMLDivElement;

    this.editor = ace.edit(editorDiv);
    this.editor.renderer.attachToShadowRoot();

    this.editor.setTheme('ace/theme/terminal');
    this.editor.session.setMode('ace/mode/json');
    this.editor.getSession().setUseWorker(false);
    this.editor.setReadOnly(false);
    this.editor.setHighlightActiveLine(true);

    this.editor.setOptions({
      autoScrollEditorIntoView: true,
      enableBasicAutocompletion: false,
      enableLiveAutocompletion: false,
      placeholder: '',
      enableSnippets: false
    });

    this.editor.on('change', () => {
      editorValue = this.editor?.getValue();
    });

    this.projectId = parseInt(
      location.pathname.substring(location.pathname.lastIndexOf('/') + 1),
      10
    );

    this.refDataLoading = true;
    this.getProjectJson(this.projectId);
  }

  getProjectJson(projId: number) {
    const api = new RefDataApi();
    api.refDataIdGet({ id: projId.toString() }).subscribe(value => {
      this.editor?.setValue(JSON.stringify(value, null, 2), 0);
      this.refDataLoading = false;
      this.editor?.clearSelection();
    });
  }

  saveRefData() {
    this.refDataLoading = true;

    try {
      const rd: RefDataApiModel = JSON.parse(editorValue || '');
      const api = new RefDataApi();
      api.refDataPut({ refDataApiModel: rd }).subscribe({
        next: () => {
          if (this.projectId !== undefined) {
            this.getProjectJson(this.projectId);
            Notification.show('Saved Reference Data', {
              theme: 'success',
              position: 'bottom-start',
              duration: 10000
            });
          }
        },
        error: (err: any) => {
          this.errorAlert(err.xhr.response ?? err.xhr.statusText);
          console.log(err);
          this.refDataLoading = false;
        },
        complete: () => {
          console.log('completed saving project reference data');
        }
      });
    } catch (e: any) {
      const notification = new ErrorNotification();
      notification.setAttribute('errorMessage', e);
      this.shadowRoot?.appendChild(notification);
      notification.open();
      this.refDataLoading = false;
    }
  }

  errorAlert(err: string) {
    const notification = new ErrorNotification();

    notification.setAttribute('errorMessage', err);
    this.shadowRoot?.appendChild(notification);
    notification.open();
  }
}
