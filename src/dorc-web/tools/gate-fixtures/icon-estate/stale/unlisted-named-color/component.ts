// Fixture: unlisted CSS named color on <vaadin-icon>. Input for tools/gate-check.mjs, not app code.
import { html } from 'lit';
import '@vaadin/icon';

export function renderUnlistedNamedColor() {
  return html`<vaadin-icon icon="dorc:edit" style="color: yellow"></vaadin-icon>`;
}
