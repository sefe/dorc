// Fixture: partial dynamic dorc:edit${suffix} slug. Input for tools/gate-check.mjs, not app code.
import { html } from 'lit';
import '@vaadin/icon';

export function renderPartialDynamicSlug(suffix: string) {
  return html`<vaadin-icon icon="dorc:edit${suffix}"></vaadin-icon>`;
}
