// Exercises the audit's ANALYSIS layer, not just its gate. `--check` asserts
// nothing but `bindings.length`, so resolveTarget, sideEffects, collectMembers,
// followBoundField and thisReads could all be stubbed out and every other case
// stayed green. This file drives them and gate-check asserts the JSON.
import { html, LitElement } from 'lit';
import { state } from 'lit/decorators.js';

export class Fixture extends LitElement {
  @state() private readonlyFlag = false;

  private plain = '';

  // `_bound` forwards to `rowRenderer` — followBoundField must see through it.
  private _boundRowRenderer = this.rowRenderer.bind(this);

  render() {
    return html`<vaadin-grid-column
      .renderer="${this._boundRowRenderer}"
    ></vaadin-grid-column>`;
  }

  // Reads one reactive field and one undecorated one, and has a side effect,
  // so the classifier has something to report on every axis.
  rowRenderer() {
    this.dispatchEvent(new CustomEvent('rendered'));
    return html`<span ?hidden="${this.readonlyFlag}">${this.plain}</span>`;
  }
}
