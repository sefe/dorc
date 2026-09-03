import { html, LitElement } from 'lit';

export class Fixture extends LitElement {
  // The primary form the audit exists to forbid, and the one with no stale
  // fixture until now: neutering the binding regex left every case green.
  render() {
    return html`<vaadin-grid-column
      .renderer="${this.rowRenderer}"
    ></vaadin-grid-column>`;
  }

  private rowRenderer = () => null;
}
