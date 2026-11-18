import { html } from 'lit';
import '@vaadin/icon';

export const view = () => html`
  <vaadin-button>
    <vaadin-icon icon="dorc:plus"></vaadin-icon>
    Add Server...
  </vaadin-button>
`;
