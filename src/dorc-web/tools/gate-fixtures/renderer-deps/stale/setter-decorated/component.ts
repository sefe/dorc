import { columnBodyRenderer } from '@vaadin/grid/lit';
import { html, LitElement } from 'lit';
import { property } from 'lit/decorators.js';

export class Fixture extends LitElement {
  private _rows: string[] = [];

  get rows() {
    return this._rows;
  }

  // Lit only needs the decorator on one half of the pair.
  @property({ type: Array })
  set rows(value: string[]) {
    this._rows = value;
  }

  render() {
    return html`<vaadin-grid-column
      ${columnBodyRenderer(this.rowRenderer, [])}
    ></vaadin-grid-column>`;
  }

  private rowRenderer = () => html`<span>${this.rows.length}</span>`;
}
