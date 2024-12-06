import { html } from 'lit';

// TODO: Review this issue https://github.com/vaadin/vaadin-router/issues/408

export const renderPageNotFound = () => {
  import('../pages/page-not-found');

  return html`<page-not-found></page-not-found>`;
};
