import { css } from 'lit';
import { customElement } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { urlForName } from '../router/router';
import { PageElementNotFound } from '../helpers/page-element-not-found';

@customElement('page-not-found')
export class PageNotFound extends PageElementNotFound {
  static get styles() {
    return css`
      :host {
        padding: 1rem;
        text-align: center;
      }
    `;
  }

  render() {
    return html`
      <section>
        <img
          src="/modern-dorc-not-found.webp"
          alt="Page not found"
          style="height: 300px; padding: 3px"
        />
        <p>Oops! It looks like the internet gremlins got into the gears.</p>
        <p>We'll have them evicted shortly!</p>
        <p>
          <a href="${urlForName('projects')}">Back to Projects</a>
        </p>
      </section>
    `;
  }
}
