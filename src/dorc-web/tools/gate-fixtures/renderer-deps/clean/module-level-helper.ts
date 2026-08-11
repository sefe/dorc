// A module-level renderer: no component to read state from, so `[]` is right.
import { html } from 'lit';

export const formatRow = () => html`<span>static</span>`;
