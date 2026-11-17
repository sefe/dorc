// Fixture: passing canonical dorc:* icon usage. Input for tools/gate-check.mjs, not app code.
import { html } from 'lit';
import '@vaadin/icon';
import { dorcIcon } from '../../../../src/icons/dorc-icons.js';

export function renderCleanIcons() {
  const ownerIcon = dorcIcon('environment-owner');
  return html`
    <vaadin-button theme="icon small" aria-label="Edit">
      <vaadin-icon icon="dorc:edit" aria-hidden="true"></vaadin-icon>
    </vaadin-button>
    <vaadin-icon icon="dorc:environments"></vaadin-icon>
    <vaadin-icon icon=${ownerIcon}></vaadin-icon>
  `;
}
