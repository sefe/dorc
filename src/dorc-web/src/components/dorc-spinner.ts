import { css, LitElement } from 'lit';
import { customElement } from 'lit/decorators.js';
import { html } from 'lit/html.js';

/**
 * Centered loading spinner overlay used while a page or panel loads its data.
 * Encapsulates the overlay + themed spinner markup that was previously
 * copy-pasted across many pages and components.
 */
@customElement('dorc-spinner')
export class DorcSpinner extends LitElement {
  static get styles() {
    return css`
      .overlay {
        width: 100%;
        height: 100%;
        position: fixed;
        z-index: 2;
      }

      .overlay__inner {
        width: 100%;
        height: 100%;
        position: absolute;
      }

      .overlay__content {
        left: 50%;
        position: absolute;
        top: 50%;
        transform: translate(-50%, -50%);
      }

      .spinner {
        width: 75px;
        height: 75px;
        display: inline-block;
        border-width: 2px;
        border-color: var(--dorc-border-color);
        border-top-color: var(--dorc-link-color);
        animation: spin 1s infinite linear;
        border-radius: 100%;
        border-style: solid;
      }

      @keyframes spin {
        100% {
          transform: rotate(360deg);
        }
      }
    `;
  }

  render() {
    return html`
      <div class="overlay">
        <div class="overlay__inner">
          <div class="overlay__content">
            <span class="spinner"></span>
          </div>
        </div>
      </div>
    `;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'dorc-spinner': DorcSpinner;
  }
}
