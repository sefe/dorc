import '@vaadin/button';
import '@vaadin/dialog';
import '@vaadin/icon';
import '@vaadin/text-area';
import * as ace from 'ace-builds';
import { LitElement, PropertyValues, render, css } from 'lit';
import { customElement, property } from 'lit/decorators.js';
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

  static get styles() {
      return css`
        .ace_line {
            color: rgba(255, 0, 0, 0.2);
        }

        .ace_line {
            color: rgba(255, 0, 255, 0.2);
        }

        .gutter-info .ace_line {
            color: rgba(0, 255, 0, 0.2);
        }
            
        .ace_marker-layer .error {
            color: rgba(255,0,0,0.2); /* Красный фон */
            background-color: rgba(0,0,255,0.2);
            position: absolute;
        }
      `;
  }


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
              <vaadin-icon
                style="color: cornflowerblue;"
                icon="vaadin:close-small"
              ></vaadin-icon>
            </vaadin-button>`,
          root
        );

        var editorDiv = root.querySelector('div');
        if (!editorDiv){
          // Создаём новые элементы
          editorDiv = document.createElement('div');
          editorDiv.setAttribute('id', 'logViewer');
          editorDiv.setAttribute('style', 'width:80vw; height:80vh;');
  
          // Вставляем их в root
          root.appendChild(editorDiv);
        }

        this.editor = ace.edit(editorDiv);
        this.editor.renderer.attachToShadowRoot();
    
        this.editor.setTheme('ace/theme/monokai');
        this.editor.session.setMode('ace/mode/crystal');
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
        if (line.includes("Run")) {
            annotations.push({
                row: index,
                column: 0,
                text: "Error log detected",
                type: "error",
            });
            //session?.highlightLines(index, index+1, "gutter-error");
            //session?.addGutterDecoration(index, "gutter-error");
        } else if (line.includes("Install")) {
            annotations.push({
                row: index,
                column: 0,
                text: "Warning log detected",
                type: "warning", 
            });
            //session?.highlightLines(index, index+1, "gutter-warning");
            //session?.addGutterDecoration(index, "gutter-warning");
        } else {
          //session?.highlightLines(index, index+1, "gutter-info");
          //session?.addGutterDecoration(index, "gutter-info");
        }
    });
    // const range = new ace.Range(5, 0, 7, 0); // Подсвечиваем 2-ю, 3-ю и 4-ю строки
    // session?.addMarker(range, 'error', 'fullLine', true);
    // session?.highlightLines(8, 10, 'error', true);

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
