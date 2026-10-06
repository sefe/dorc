import { columnBodyRenderer } from '@vaadin/grid/lit';
import { html, LitElement } from 'lit';
import { state } from 'lit/decorators.js';

export class Fixture extends LitElement {
  @state() private nameFilter = '';

  private get filteredRows() {
    return [this.nameFilter];
  }

  render() {
    return html`<vaadin-grid-column
      ${columnBodyRenderer(this.rowRenderer, [])}
    ></vaadin-grid-column>`;
  }

  private rowRenderer = () => html`<span>${this.filteredRows}</span>`;
}
