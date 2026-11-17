// Fixture: non-dorc iconset prefix (`vaadin:trash`). Input for tools/gate-check.mjs, not app code.
import { html } from 'lit';
import '@vaadin/icon';

export const renderLegacyNamespace = () =>
  html`<vaadin-icon icon="vaadin:trash"></vaadin-icon>`;
