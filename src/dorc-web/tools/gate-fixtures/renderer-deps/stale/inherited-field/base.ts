import { LitElement } from 'lit';
import { property } from 'lit/decorators.js';

export class FixtureBase extends LitElement {
  @property({ type: Number }) environmentId = 0;
}
