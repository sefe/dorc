import { css } from 'lit';
import { registerStyles } from '@vaadin/vaadin-themable-mixin/vaadin-themable-mixin.js';

// Note: Grid cell styling has been migrated to use cellPartNameGenerator with ::part() CSS
// selectors in individual components (Vaadin 25 migration).

registerStyles(
  'vaadin-notification-card',
  css`
    :host([slot^='bottom-start'][theme~='success']) [part='content'] {
      padding: var(--lumo-space-s);
    }
  `
);

registerStyles(
  'vaadin-notification-card',
  css`
    :host([slot^='bottom-start'][theme~='warning']) [part='overlay'] {
      background: var(--dorc-badge-text);
    }
  `
);

registerStyles(
  'vaadin-tab',
  css`
      :host([orientation^='vertical']) {
          min-height: var(--lumo-size-s);
      }
  `
);

registerStyles(
  'vaadin-dialog-overlay',
  css`
    [part='overlay'] {
      background: var(--lumo-base-color);
      color: var(--lumo-body-text-color);
    }
  `
);

// Row hover highlight for grids that opt in via theme="hover-highlight"
registerStyles(
  'vaadin-grid',
  css`
    :host([theme~='hover-highlight']) [part~='row']:hover [part~='cell'] {
      background-color: var(--lumo-primary-color-10pct);
    }
    :host([theme~='hover-highlight']) [part~='row'] {
      cursor: pointer;
    }
  `
);

// Visible hover for context menu / menu-bar dropdown items
registerStyles(
  'vaadin-context-menu-item',
  css`
    :host(:hover) {
      background-color: var(--lumo-primary-color-10pct);
      cursor: pointer;
    }
  `
);
