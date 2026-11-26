import { html } from 'lit';
import '@vaadin/icon';

export const view = (cond: boolean) =>
  html`<vaadin-icon
    icon="dorc:edit"
    style="${cond ? 'color: red' : 'color: blue'}"
  ></vaadin-icon>`;
