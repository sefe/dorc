// Fixture: interpolated attribute before style="color: red" on <vaadin-icon>. Input for tools/gate-check.mjs, not app code.
import { html } from 'lit';
import '@vaadin/icon';
import { dorcIcon } from '../../../../../src/icons/dorc-icons.js';

export function renderInterpolatedAttrColor() {
  const icon = dorcIcon('edit');
  return html`<vaadin-icon icon=${icon} style="color: red"></vaadin-icon>`;
}
