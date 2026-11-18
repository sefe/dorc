// Fixture: passing canonical dorc:* icon usage. Input for tools/gate-check.mjs, not app code.
import { html } from 'lit';
import '@vaadin/icon';
import { dorcIcon } from '../../../../src/icons/dorc-icons.js';

export function renderCleanIcons(emphasize = false) {
  const ownerIcon = dorcIcon('environment-owner');
  return html`
    <vaadin-button theme="icon small" aria-label="Edit">
      <vaadin-icon
        icon="dorc:edit"
        aria-hidden="true"
        style=${'width: 16px; height: 16px'}
      ></vaadin-icon>
    </vaadin-button>
    <vaadin-icon
      icon="dorc:environments"
      style=${'color: var(--dorc-link-color)'}
    ></vaadin-icon>
    <vaadin-icon
      icon=${ownerIcon}
      style="${emphasize ? 'color: var(--dorc-badge-fg)' : 'width: 16px'}"
    ></vaadin-icon>
  `;
}
