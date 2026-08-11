import { columnBodyRenderer } from '@vaadin/grid/lit';
import { html } from 'lit';
import { FixtureBase } from './base';

export class Fixture extends FixtureBase {
  render() {
    return html`<vaadin-grid-column
      ${columnBodyRenderer(this.rowRenderer, [])}
    ></vaadin-grid-column>`;
  }

  private rowRenderer = () => html`<span>${this.environmentId}</span>`;
}
