// Fixture: ad-hoc inline <svg> outside dorc-icons.ts. Input for tools/gate-check.mjs, not app code.
import { html } from 'lit';

export const renderInlineSvg = () =>
  html`<svg viewBox="0 0 24 24"><path d="M0 0h24v24H0z"></path></svg>`;
