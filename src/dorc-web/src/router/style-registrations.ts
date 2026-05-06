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
      width: min(90vw, 650px);
      max-height: 85vh;
      max-height: 85dvh;
    }

    /*
     * log-dialog renders an Ace editor at 80vw; the 650px default would clip
     * it. Opt-in via theme="log-viewer" on the vaadin-dialog.
     */
    :host([theme~='log-viewer']) [part='overlay'] {
      width: min(95vw, 1400px);
    }
  `
);

// Mobile-only: stretch combo-boxes to fill the row so they don't get squeezed.
// On desktop, leave sizing to the local component styles (some pages set
// `min-width: 490px` etc. that would conflict with a global `width: 100%`).
registerStyles(
  'vaadin-combo-box',
  css`
    @media (max-width: 768px) {
      :host {
        width: 100%;
        max-width: 600px;
      }
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

registerStyles(
  'vaadin-button',
  css`
    :host([theme~='icon']) {
      min-width: 44px;
      min-height: 44px;
    }
  `
);

