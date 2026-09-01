import { LitElement } from 'lit';

export class Fixture extends LitElement {
  firstUpdated() {
    const column = this.shadowRoot?.querySelector('vaadin-grid-column');
    // `this` binds to the column, and the cell only repaints on
    // requestContentUpdate() — the two defects the directives removed.
    (column as unknown as { renderer: unknown }).renderer = this.rowRenderer;
  }

  private rowRenderer() {
    return null;
  }
}
