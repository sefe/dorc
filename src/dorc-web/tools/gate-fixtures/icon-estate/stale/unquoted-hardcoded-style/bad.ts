import { html } from 'lit';
import '@vaadin/icon';

export const view = () =>
  html`<vaadin-icon
    icon="dorc:edit"
    style=${'width: 16px; color: crimson'}
  ></vaadin-icon>`;
