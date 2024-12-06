import { css } from 'lit';
import { registerStyles } from '@vaadin/vaadin-themable-mixin/vaadin-themable-mixin.js';

registerStyles(
  'vaadin-grid',
  css`
    .insert-type {
      background-color: #b1ffb7;
    }
    .delete-type {
      background-color: #ffd9d9;
    }
  `
);

registerStyles(
  'vaadin-notification-card',
  css`
    :host([slot^='bottom-start'][theme~='success']) [part='content'] {
      padding: var(--lumo-space-s);
    }
  `
);

registerStyles(
  'vaadin-grid',
  css`
    .success {
      background-color: #b1ffb7;
    }

    .failure {
      background-color: #ffd9d9;
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
  'vaadin-grid',
  css`
    .variable-value-error {
      background-color: #ffddb7;
    }
  `
);
