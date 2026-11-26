// Fixture: retired <paper-*> and <iron-*> elements. Input for tools/gate-check.mjs, not app code.
import { html } from 'lit';

export function renderLegacyPolymerElements() {
  return html`
    <paper-item>Database Error</paper-item>
    <iron-collapse opened></iron-collapse>
  `;
}
