import { columnBodyRenderer } from '@vaadin/grid/lit';
import { html, LitElement } from 'lit';
import { property } from 'lit/decorators.js';

export class Fixture extends LitElement {
  @property({ type: Boolean }) readonly = false;

  render() {
    return html`<vaadin-grid-column
      ${columnBodyRenderer(this.rowRenderer, [])}
    ></vaadin-grid-column>`;
  }

  private rowRenderer = () => html`<span ?hidden="${this.readonly}"></span>`;
}
