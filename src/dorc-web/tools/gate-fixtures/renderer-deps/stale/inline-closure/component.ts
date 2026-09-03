import { columnBodyRenderer } from '@vaadin/grid/lit';
import { html, LitElement } from 'lit';
import { property } from 'lit/decorators.js';

export class Fixture extends LitElement {
  @property({ type: Boolean }) readonly = false;

  render() {
    // A fresh closure every render, with `[]`: the directive dirty-checks the
    // array, never the renderer identity, so this is pinned at first paint.
    return html`<vaadin-grid-column
      ${columnBodyRenderer(() => html`<span ?hidden="${this.readonly}"></span>`, [])}
    ></vaadin-grid-column>`;
  }
}
