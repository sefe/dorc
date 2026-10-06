import { columnBodyRenderer } from '@vaadin/grid/lit';
import { html } from 'lit';
import { FixtureBase, FixtureMixin } from './base';

export class Fixture extends FixtureMixin(FixtureBase) {
  render() {
    return html`<vaadin-grid-column
      ${columnBodyRenderer(this.rowRenderer, [])}
    ></vaadin-grid-column>`;
  }

  // Reads one field from each half: the base class supplies `environmentId`,
  // the mixin supplies `narrow`.
  private rowRenderer = () =>
    html`<span ?hidden="${this.narrow}">${this.environmentId}</span>`;
}
