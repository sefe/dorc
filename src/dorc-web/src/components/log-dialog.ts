import '@vaadin/button';
import '@vaadin/dialog';
import '@vaadin/text-area';
import * as ace from 'ace-builds';
import { LitElement, PropertyValues, render } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import './dorc-icon.js';
import { guard } from 'lit/directives/guard.js';
import { html } from 'lit/html.js';
import { DialogOpenedChangedEvent } from '@vaadin/dialog';

@customElement('log-dialog')
export class LogDialog extends LitElement {
  @property()
  isOpened = false;

  @property()
  selectedLog: string | undefined;

  private editor: ace.Ace.Editor | undefined;

  render() {
    return html`
      <vaadin-dialog
        .opened="${this.isOpened}"
        draggable="true"
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
        .renderer = "${guard([], () => (root: HTMLElement) => {

        render(
          html`<vaadin-button
              @click="${() =>
                this.dispatchEvent(
                  new CustomEvent('close-log-dialog', {
                    bubbles: true,
                    composed: true
                  })
                )}"
            >
              <dorc-icon icon="close-small" color="primary"></dorc-icon>
            </vaadin-button>`,
          root
        );

        let editorDiv = root.querySelector('div');
        if (!editorDiv){
          editorDiv = document.createElement('div');
          editorDiv.setAttribute('id', 'logViewer');
          editorDiv.setAttribute('style', 'width:80vw; height:80vh;');
  
          root.appendChild(editorDiv);
        }

        this.editor = ace.edit(editorDiv);
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
        this.editor?.setValue(this.selectedLog ?? '');
        this.highlightWarningsLogs();
        this.editor?.clearSelection();
        })}"
      ></vaadin-dialog>
    `;
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener('close-log-dialog', this.close as EventListener);
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
