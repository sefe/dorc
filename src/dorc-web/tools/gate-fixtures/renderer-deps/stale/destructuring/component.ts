import { columnBodyRenderer } from '@vaadin/grid/lit';
import { html, LitElement } from 'lit';
import { state } from 'lit/decorators.js';

export class Fixture extends LitElement {
  @state() private selectedId = 0;

  render() {
    return html`<vaadin-grid-column
      ${columnBodyRenderer(this.rowRenderer, [])}
    ></vaadin-grid-column>`;
  }

  private rowRenderer = () => {
    const { selectedId } = this;
    return html`<span>${selectedId}</span>`;
  };
}
