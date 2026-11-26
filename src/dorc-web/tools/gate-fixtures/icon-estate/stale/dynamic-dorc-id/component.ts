// Fixture: untyped dynamic `dorc:${...}` interpolation. Input for tools/gate-check.mjs, not app code.
import { html } from 'lit';
import '@vaadin/icon';

export const renderDynamicIcon = (kind: string) =>
  html`<vaadin-icon icon=${`dorc:${kind}`}></vaadin-icon>`;
