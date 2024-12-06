import { css } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import '../components/add-edit-access-control';
import { PageElement } from '../helpers/page-element';

@customElement('page-test-component')
export class PageTestComponent extends PageElement {
  secureName = 'FO ARC';

  static get styles() {
    return css`
      :host {
        padding: 1rem;
      }
      .loader {
        border: 16px solid #f3f3f3; /* Light grey */
        border-top: 16px solid #3498db; /* Blue */
        border-radius: 50%;
        width: 120px;
        height: 120px;
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
    `;
  }

  @property({ type: Number })
  searchId = 0;

  private loading = true;

  constructor() {
    super();
    this.loading = false;
  }

  render() {
    return html`
      ${this.loading
        ? html` <div class="loader"></div> `
        : html`
            <add-edit-access-control
              .secureName="${this.secureName}"
            ></add-edit-access-control>
          `}
    `;
  }
}
