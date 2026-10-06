import { columnBodyRenderer } from '@vaadin/grid/lit';
import { html, LitElement } from 'lit';
import { state } from 'lit/decorators.js';

export class Fixture extends LitElement {
  @state() private highlight = '';

  render() {
    return html`<vaadin-grid-column
      ${columnBodyRenderer(this.valueRenderer('FromValue'), [])}
    ></vaadin-grid-column>`;
  }

  // The renderer is what this returns; that closure's reads are the ones that
  // matter.
  private valueRenderer(field: string) {
    return () => html`<span>${field}${this.highlight}</span>`;
  }
}
