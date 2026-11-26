// Fixture: retired @vaadin/icons side-effect import. Input for tools/gate-check.mjs, not app code.
import '@vaadin/icons';
import { html } from 'lit';

export const renderBadImport = () =>
  html`<vaadin-icon icon="dorc:edit"></vaadin-icon>`;
