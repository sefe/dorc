import { columnBodyRenderer } from '@vaadin/grid/lit';
import { html, LitElement } from 'lit';
import { state } from 'lit/decorators.js';

export class Fixture extends LitElement {
  @state() private readonlyFlag = false;

  private tags = ['a'];

  render() {
    return html`<vaadin-grid-column
      ${columnBodyRenderer(this.rowRenderer, [])}
    ></vaadin-grid-column>`;
  }

  // `this.tagTemplate` is invoked by map during the render, so its reads are
  // render-time reads.
  private rowRenderer = () => html`${this.tags.map(this.tagTemplate)}`;

  private tagTemplate = (tag: string) =>
    html`<span ?hidden="${this.readonlyFlag}">${tag}</span>`;
}
