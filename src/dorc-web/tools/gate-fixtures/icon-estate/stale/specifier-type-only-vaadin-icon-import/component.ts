// Fixture: specifier-level { type Icon } @vaadin/icon import. Input for tools/gate-check.mjs, not app code.
import { html } from 'lit';
import { type Icon } from '@vaadin/icon';

export type IconRef = Icon;

export function renderSpecifierTypeOnlyImport() {
  return html`<vaadin-icon icon="dorc:edit"></vaadin-icon>`;
}
