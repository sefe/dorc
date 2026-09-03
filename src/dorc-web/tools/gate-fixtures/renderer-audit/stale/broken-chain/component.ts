import { LitElement } from 'lit';

export class Fixture extends LitElement {
  firstUpdated() {
    // Prettier splits the chain across lines; a single-line regex misses it.
    (
      this.shadowRoot?.querySelector('vaadin-grid') as unknown as {
        rowDetailsRenderer: unknown;
      }
    ).rowDetailsRenderer = this.detailsRenderer;
  }

  private detailsRenderer() {
    return null;
  }
}
