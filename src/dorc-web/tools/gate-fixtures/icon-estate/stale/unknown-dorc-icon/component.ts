// Fixture: unregistered `dorc:*` icon slug. Input for tools/gate-check.mjs, not app code.
import { html } from 'lit';
import '@vaadin/icon';

export const renderUnknownIcon = () =>
  html`<vaadin-icon icon="dorc:does-not-exist"></vaadin-icon>`;
