import { columnBodyRenderer } from '@vaadin/grid/lit';
import { html, LitElement } from 'lit';
import { state } from 'lit/decorators.js';

// The base is declared in this same file, so there is no import to follow.
// Scoping the member map to the enclosing class made this case invisible until
// `classNamed` resolved it locally: the child's own class body has none of the
// base's reactive fields, so the renderer was compared against an empty set and
// passed with no dependency at all.
class FixtureBase extends LitElement {
  @state() protected rowLabel = '';
}

export class Fixture extends FixtureBase {
  render() {
    return html`<vaadin-grid-column
      ${columnBodyRenderer(this.nameRenderer, [])}
    ></vaadin-grid-column>`;
  }

  private nameRenderer = () => html`<span>${this.rowLabel}</span>`;
}
