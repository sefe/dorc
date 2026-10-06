// Two renderer-bearing classes in one file. Their members must not be pooled:
// with whole-file scoping, `Sibling`'s `flag` read leaks into `First`'s member
// map and the gate fabricates a report against a class that is entirely
// correct. No file in src has this shape today, so only a fixture can hold the
// line.
import { columnBodyRenderer } from '@vaadin/grid/lit';
import { html, LitElement } from 'lit';
import { state } from 'lit/decorators.js';

export class First extends LitElement {
  render() {
    return html`<vaadin-grid-column
      ${columnBodyRenderer(this.rowRenderer, [])}
    ></vaadin-grid-column>`;
  }

  // Reads nothing reactive of its own. Under whole-file scoping this resolved
  // against Sibling's `rowRenderer` instead and was reported as stale.
  private rowRenderer = () => html`<span>static</span>`;
}

export class Sibling extends LitElement {
  @state() private flag = false;

  render() {
    return html`<vaadin-grid-column
      ${columnBodyRenderer(this.rowRenderer, [this.flag])}
    ></vaadin-grid-column>`;
  }

  private rowRenderer = () => html`<span ?hidden="${this.flag}"></span>`;
}
