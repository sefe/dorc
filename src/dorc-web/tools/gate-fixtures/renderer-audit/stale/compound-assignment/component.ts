import { LitElement } from 'lit';

export class Fixture extends LitElement {
  firstUpdated() {
    const col = this.shadowRoot?.querySelector(
      'vaadin-grid-column'
    ) as unknown as {
      renderer: unknown;
    };
    // Reaches exactly the same property as `=`, and used to pass untouched.
    col.renderer ??= this.rowRenderer;
  }

  private rowRenderer() {
    return null;
  }
}
