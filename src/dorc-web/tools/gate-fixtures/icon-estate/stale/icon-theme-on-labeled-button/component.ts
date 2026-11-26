import { html } from 'lit';
import '@vaadin/icon';

export const view = () => html`
  <vaadin-button theme="icon">
    <vaadin-icon slot="prefix" icon="dorc:access-control"></vaadin-icon>
    Environment Access...
  </vaadin-button>
`;
