import '@vaadin/button';
import '@vaadin/dialog';
import '@vaadin/icon';
import '@vaadin/text-area';
import * as ace from 'ace-builds';
import { css, LitElement, PropertyValues, render } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { guard } from 'lit/directives/guard.js';
import { html } from 'lit/html.js';
import { DialogOpenedChangedEvent } from '@vaadin/dialog';

@customElement('log-dialog')
export class LogDialog extends LitElement {
  @property({ type: Boolean })
  isOpened = false;

  @property({ type: String })
  selectedLog: string | undefined;

  @property({ type: Boolean })
  isLoading = false;

  private editor: ace.Ace.Editor | undefined;

  static get styles() {
    return css`
      .spinner {
        width: 40px;
        height: 40px;
        display: inline-block;
        border-width: 3px;
        border-color: rgba(255, 255, 255, 0.05);
        border-top-color: cornflowerblue;
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
        width: 80vw;
        height: 80vh;
        flex-direction: column;
      }

      .loading-text {
        margin-top: 20px;
        color: #666;
        font-size: 14px;
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
        .renderer = "${guard([this.isLoading, this.selectedLog], () => (root: HTMLElement) => {

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

        if (this.isLoading) {
          let loadingDiv = root.querySelector('.loading-container') as HTMLElement;
          if (!loadingDiv) {
            loadingDiv = document.createElement('div');
            loadingDiv.style.cssText = 'display: flex; justify-content: center; align-items: center; width: 80vw; height: 80vh; flex-direction: column;';
            
            const spinnerDiv = document.createElement('div');
            spinnerDiv.style.cssText = `
              width: 40px;
              height: 40px;
              display: inline-block;
              border-width: 3px;
              border-color: rgba(255, 255, 255, 0.05);
              border-top-color: cornflowerblue;
              animation: spin 1s infinite linear;
              border-radius: 100%;
              border-style: solid;
              margin: 20px;
            `;
            
            const textDiv = document.createElement('div');
            textDiv.style.cssText = 'margin-top: 20px; color: #666; font-size: 14px;';
            textDiv.textContent = 'Loading log...';
            
            // Add keyframe animation
            const style = document.createElement('style');
            style.textContent = `
              @keyframes spin {
                100% { transform: rotate(360deg); }
              }
            `;
            
            loadingDiv.appendChild(style);
            loadingDiv.appendChild(spinnerDiv);
            loadingDiv.appendChild(textDiv);
            loadingDiv.className = 'loading-container';
            
            // Hide any existing editor
            const existingEditor = root.querySelector('#logViewer') as HTMLElement;
            if (existingEditor) {
              existingEditor.style.display = 'none';
            }
            
            root.appendChild(loadingDiv);
          }
          return;
        }

        // Hide loading div if it exists
        const loadingDiv = root.querySelector('.loading-container') as HTMLElement;
        if (loadingDiv) {
          loadingDiv.remove();
        }

        let editorDiv = root.querySelector('#logViewer') as HTMLElement;
        if (!editorDiv){
          editorDiv = document.createElement('div');
          editorDiv.setAttribute('id', 'logViewer');
          editorDiv.setAttribute('style', 'width:80vw; height:80vh;');
  
          root.appendChild(editorDiv);
          
          // Initialize the editor only when creating the div
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
        } else {
          editorDiv.style.display = 'block';
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
        this.editor?.gotoLine(1, 0, false);
        this.editor?.clearSelection();
        // Update the editor content
        if (this.editor) {
          this.editor.setValue(this.selectedLog ?? '');
          this.highlightWarningsLogs();
          this.editor.clearSelection();
        }
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
