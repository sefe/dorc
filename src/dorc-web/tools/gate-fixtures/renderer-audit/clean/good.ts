// Fixture: shapes the audit must NOT flag. Input for tools/gate-check.mjs.
import { columnBodyRenderer } from '@vaadin/grid/lit';
import { html, LitElement } from 'lit';

export class Fixture extends LitElement {
  // The word `renderer` in prose: `column.renderer = ...` written in a comment
  // is not a binding. The audit used to be a regex and flagged this.
  private readonly note = "grid.renderer = 'this is a string, not code'";

  render() {
    return html`<vaadin-grid-column
      ${columnBodyRenderer(this.rowRenderer, [])}
    ></vaadin-grid-column>`;
  }

  private rowRenderer = () => html`<span>${this.note}</span>`;
}
