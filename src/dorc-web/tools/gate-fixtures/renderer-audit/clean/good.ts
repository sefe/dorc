// Fixture: shapes the audit must NOT flag. Input for tools/gate-check.mjs.
import { columnBodyRenderer } from '@vaadin/grid/lit';
import { html, LitElement } from 'lit';

export class Fixture extends LitElement {
  // Prose describing the very form this gate forbids. Written out in full,
  // interpolation and all, because a bare `column.renderer = ...` has no `${`
  // and so could only ever reach the AST path — which is how the regex half
  // went untested. This line reads:
  //   .renderer="${this.rowRenderer}"
  // and the string below carries the same shape. Both must be ignored.
  private readonly note = '.renderer="${this.rowRenderer}" as a string';

  render() {
    return html`<vaadin-grid-column
      ${columnBodyRenderer(this.rowRenderer, [])}
    ></vaadin-grid-column>`;
  }

  private rowRenderer = () => html`<span>${this.note}</span>`;
}
