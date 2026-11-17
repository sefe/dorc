// Fixture: retired @polymer/* package import. Input for tools/gate-check.mjs, not app code.
import { html } from 'lit';
import '@polymer/paper-listbox/paper-listbox.js';

export function renderPolymerImport() {
  return html`<div>Legacy import</div>`;
}
