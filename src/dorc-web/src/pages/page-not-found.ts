import { css } from 'lit';
import { customElement } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { urlForName } from '../router';
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
          src="/hegsie_white_background_cartoon_dork_code_markdown_simple_icon__9a6a1001-ed80-4ed7-a62a-ffee1e55b4d6.png"
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
