import { columnBodyRenderer } from '@vaadin/grid/lit';
import { html, LitElement } from 'lit';

// `rowRenderer` comes from a base class in another file, so its reads are not
// visible here. The gate must say so rather than pass it.
export class Fixture extends LitElement {
  render() {
    return html`<vaadin-grid-column
      ${columnBodyRenderer(this.rowRenderer, [])}
    ></vaadin-grid-column>`;
  }
}
