import { LitElement } from 'lit';

export class Fixture extends LitElement {
  firstUpdated() {
    const col = this.shadowRoot?.querySelector('vaadin-grid-column');
    col!.renderer = this.rowRenderer;
  }

  private rowRenderer() {
    return null;
  }
}
