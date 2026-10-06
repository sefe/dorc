import { css, LitElement } from 'lit';
import { customElement } from 'lit/decorators.js';
import { html } from 'lit/html.js';

/**
 * Centered loading spinner overlay used while a page or panel loads its data.
 * Encapsulates the overlay + themed spinner markup that was previously
 * copy-pasted across many pages and components.
 *
 * The stacking order can be overridden per call site with the
 * `--dorc-spinner-z-index` custom property (defaults to 2) for pages whose
 * content sits in a raised stacking context.
 */
@customElement('dorc-spinner')
export class DorcSpinner extends LitElement {
  static get styles() {
    return css`
      :host([hidden]) {
        display: none !important;
      }

      .overlay {
        width: 100%;
        height: 100%;
        position: fixed;
        z-index: var(--dorc-spinner-z-index, 2);
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

      .visually-hidden {
        position: absolute;
        width: 1px;
        height: 1px;
        overflow: hidden;
        clip: rect(0 0 0 0);
        white-space: nowrap;
      }

      @keyframes spin {
        100% {
          transform: rotate(360deg);
        }
      }

      /* WCAG 2.3.3 Animation from Interactions (AAA, voluntary). Honouring the
         OS-level preference costs nothing and covers every call site at once.

         On WCAG 2.2.2 Pause, Stop, Hide (Level A): this does NOT discharge it —
         a media query is a preference, not a mechanism, and most users never set
         it. 2.2.2 is discharged instead by its own exceptions: the motion is
         *essential* (an indeterminate spinner's only job is to convey that work
         is in progress — freezing it would destroy the information it carries),
         and it is not "presented in parallel with other content" because the
         overlay covers the content it is loading. Recorded here so the argument
         is on the record rather than assumed. */
      @media (prefers-reduced-motion: reduce) {
        .spinner {
          animation: none;
        }
      }
    `;
  }

  render() {
    return html`
      <div class="overlay" role="status" aria-label="Loading">
        <div class="overlay__inner">
          <div class="overlay__content">
            <span class="spinner"></span>
            <span class="visually-hidden">Loading…</span>
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
