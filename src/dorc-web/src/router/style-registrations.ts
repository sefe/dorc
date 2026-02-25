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
      background: orange;
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
      width: min(90vw, 650px);
      max-height: 85vh;
      max-height: 85dvh;
    }
  `
);

registerStyles(
  'vaadin-combo-box',
  css`
    :host {
      width: 100%;
      max-width: 600px;
    }
  `
);
