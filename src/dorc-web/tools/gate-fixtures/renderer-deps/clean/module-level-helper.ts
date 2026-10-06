// Module-level renderers: no component to read state from, so `[]` is right.
// Both declaration forms, because they are separate branches in
// `isModuleLevelFunction` and only the arrow-const one was fixtured — with the
// FunctionDeclaration branch disabled the whole suite stayed green while the
// real tree went red on two `.template.ts` files.
import { html } from 'lit';

export const formatRow = () => html`<span>static</span>`;

export function formatHeader() {
  return html`<span>static header</span>`;
}
