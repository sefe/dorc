import { css, LitElement, PropertyValues } from 'lit';
import { html } from 'lit/html.js';
import { customElement, property } from 'lit/decorators.js';
import { classMap } from 'lit/directives/class-map.js';

@customElement('hegs-dialog')
export class HegsDialog extends LitElement {
  @property({ type: String }) title = '';

  @property({ type: Boolean }) open = false;

  @property({ type: String }) text = '';

  @property({ type: String }) clickAction = '';

  static get styles() {
    return css`
      :host {
        font-family: Arial, Helvetica, sans-serif;
      }

      .wrapper {
        opacity: 0;
        position: absolute;
        z-index: 10;
        transition: opacity 0.25s ease-in;
      }

      .wrapper:not(.open) {
        visibility: hidden;
      }

      .wrapper.open {
        align-items: start;
        display: flex;
        justify-content: center;
        width: calc(100% - 300px);
        height: 100%;
        opacity: 1;
        visibility: visible;
      }

      .overlay {
        opacity: 0.5;
        background: #000;
        width: 100%;
        height: 100%;
        z-index: 1;
        top: 0;
        left: 0;
        position: fixed;
        overflow: hidden;
      }

      .dialog {
        background: #ffffff;
        border-radius: 13px;
        padding: 1rem;
        position: absolute;
        z-index: 10;
        box-shadow:
          0 0 0 1px var(--lumo-shade-5pct),
          var(--lumo-box-shadow-xl);
      }

      .dialog h2 {
        margin: 0 0 10px;
        padding: 15px;
      }
    `;
  }

  render() {
    return html`
      <div class="${classMap({ wrapper: true, open: this.open })}">
        <div
          class="overlay"
          @click="${this.close}"
          @keydown="${this.keyPressed}"
        ></div>
        <div class="dialog">
          <h2 id="title">${this.title}</h2>
          <dorc-icon icon="close-small" color="lightblue"></dorc-icon>
          <slot></slot>
        </div>
      </div>
    `;
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener('keydown', this.keyPressed as EventListener);
  }

  close() {
    this.open = false;
    this.dispatchEvent(new CustomEvent('hegs-dialog-closed'));
  }

  keyPressed(e: KeyboardEvent) {
    if (e.code === 'Escape') {
      this.close();
    }
  }
}
