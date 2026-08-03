import '@vaadin/button';
import '@vaadin/dialog';
import '@vaadin/icon';
import '@vaadin/text-area';
import * as ace from 'ace-builds';
import { css, LitElement, PropertyValues } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { ref } from 'lit/directives/ref.js';
import { html } from 'lit/html.js';
import { DialogOpenedChangedEvent } from '@vaadin/dialog';
import { dialogRenderer } from '@vaadin/dialog/lit';

@customElement('log-dialog')
export class LogDialog extends LitElement {
  @property({ type: Boolean })
  isOpened = false;

  @property({ type: String })
  selectedLog: string | undefined;

  @property({ type: Boolean })
  isLoading = false;

  private editor: ace.Ace.Editor | undefined;
  private readonly viewerHeight = 'calc(85dvh - 90px)';

  static get styles() {
    return css`
      .spinner {
        width: 40px;
        height: 40px;
        display: inline-block;
        border-width: 3px;
        border-color: var(--dorc-border-color);
        border-top-color: var(--dorc-link-color);
        animation: spin 1s infinite linear;
        border-radius: 100%;
        border-style: solid;
        margin: 20px;
      }

      @keyframes spin {
        100% {
          transform: rotate(360deg);
        }
      }

      .loading-container {
        display: flex;
        justify-content: center;
        align-items: center;
        width: 100%;
        height: calc(85dvh - 90px);
        flex-direction: column;
      }

      .loading-text {
        margin-top: 20px;
        color: #666;
        font-size: var(--lumo-font-size-s);
      }
    `;
  }

  render() {
    return html`
      <vaadin-dialog
        theme="log-viewer"
        header-title="Deployment Log"
        .opened="${this.isOpened}"
        draggable
        @opened-changed="${(event: DialogOpenedChangedEvent) => {
          this.isOpened = event.detail.value;
          if (!this.isOpened) {
            this.dispatchEvent(
              new CustomEvent('log-dialog-closed', {
                bubbles: true,
                composed: true
              })
            );
          }
        }}"
        resizable
        ${dialogRenderer(this.renderLog, [this.isLoading, this.selectedLog])}
      ></vaadin-dialog>
    `;
  }

  private renderLog = () => html`
    <vaadin-button
      @click="${() =>
        this.dispatchEvent(
          new CustomEvent('close-log-dialog', {
            bubbles: true,
            composed: true
          })
        )}"
    >
      <vaadin-icon
        style="color: var(--dorc-link-color);"
        icon="vaadin:close-small"
      ></vaadin-icon>
    </vaadin-button>
    ${this.isLoading
      ? html`
          <div class="loading-container">
            <div class="spinner"></div>
            <div class="loading-text">Loading log...</div>
          </div>
        `
      : html`
          <div
            id="logViewer"
            style="width: 100%; height: ${this.viewerHeight};"
            ${ref(this.attachEditor)}
          ></div>
        `}
  `;

  /**
   * Creates the ace editor on the div Lit just made, and tears it down when
   * the div goes away.
   *
   * `ref` fires only when the element itself is created or removed, which is
   * exactly the editor's lifetime — a new log for the same open dialog reuses
   * the div, and `updated()` pushes the text in.
   */
  private attachEditor = (element?: Element) => {
    if (!element) {
      this.editor?.destroy();
      this.editor = undefined;
      return;
    }

    this.editor = ace.edit(element as HTMLElement);
    this.editor.renderer.attachToShadowRoot();
    this.editor.setTheme('ace/theme/monokai');
    this.editor.session.setMode('ace/mode/less');
    this.editor.getSession().setUseWorker(false);
    this.editor.setReadOnly(true);
    this.editor.setHighlightActiveLine(true);
    this.editor.setOptions({
      autoScrollEditorIntoView: true,
      enableBasicAutocompletion: false,
      enableLiveAutocompletion: false,
      placeholder: '',
      enableSnippets: false
    });

    this.showLog();
  };

  private showLog() {
    if (!this.editor) return;
    this.editor.setValue(this.selectedLog ?? '');
    this.highlightWarningsLogs();
    this.editor.gotoLine(1, 0, false);
    this.editor.clearSelection();
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener('close-log-dialog', this.close as EventListener);
  }

  protected updated(changed: PropertyValues) {
    super.updated(changed);
    // A new log for an already-open dialog reuses the same div, so `ref` does
    // not fire; the text has to be pushed in from here.
    if (changed.has('selectedLog')) this.showLog();
  }

  private highlightWarningsLogs() {
    const lines = this.editor?.getValue().split("\n");
    const session = this.editor?.getSession();
    const annotations: ace.Ace.Annotation[] = [];

    lines?.forEach((line, index) => {
        if (line.toLowerCase().includes("error")) {
            annotations.push({
                row: index,
                column: 0,
                text: "Error log detected",
                type: "error",
            });
        } else if (line.toLowerCase().includes("warn")) {
            annotations.push({
                row: index,
                column: 0,
                text: "Warning log detected",
                type: "warning", 
            });
        }
    });

    session?.setAnnotations(annotations);
}

  private close() {
    this.isOpened = false;
    this.dispatchEvent(
      new CustomEvent('log-dialog-closed', {
        bubbles: true,
        composed: true
      })
    );
  }
}
