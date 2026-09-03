import { LitElement } from 'lit';

export class Fixture extends LitElement {
  firstUpdated() {
    const col = this.shadowRoot?.querySelector('vaadin-grid-column');
    (col as unknown as Record<string, unknown>)['headerRenderer'] =
      this.headerRenderer;
  }

  private headerRenderer() {
    return null;
  }
}
