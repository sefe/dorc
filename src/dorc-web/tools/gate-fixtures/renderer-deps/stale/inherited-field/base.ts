import { LitElement } from 'lit';
import { property } from 'lit/decorators.js';

export class FixtureBase extends LitElement {
  @property({ type: Number }) environmentId = 0;
}

// `extends FixtureMixin(FixtureBase)` is the shape 20+ real components use,
// and it was the one heritage arm with no fixture: dropping the CallExpression
// handling left every case green.
export const FixtureMixin = <T extends new (...args: never[]) => LitElement>(
  superClass: T
) =>
  class extends superClass {
    @property({ type: Boolean }) narrow = false;
  };
