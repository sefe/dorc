// Fixture: every rule renderer-deps applies, in its passing form.
// Not compiled or imported by the app — it is input for tools/gate-check.mjs.
import {
  columnBodyRenderer,
  columnHeaderRenderer,
  dialogRenderer
} from '@vaadin/grid/lit';
import { html } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { LitElement } from 'lit';

import { formatHeader, formatRow } from './module-level-helper';

@customElement('fixture-good')
export class FixtureGood extends LitElement {
  @property({ type: Boolean }) readonly = false;

  @state() private selectedId = 0;

  @state() private tags: string[] = [];

  private plain = 'not reactive';

  @state() private deep = '';

  render() {
    return html`
      <vaadin-grid>
        <vaadin-grid-column
          ${columnBodyRenderer(this.rowRenderer, [this.readonly])}
        ></vaadin-grid-column>

        <!-- destructuring from the host is a read -->
        <vaadin-grid-column
          ${columnBodyRenderer(this.destructuringRenderer, [this.selectedId])}
        ></vaadin-grid-column>

        <!-- a member passed as a call argument runs during the render -->
        <vaadin-grid-column
          ${columnBodyRenderer(this.mappingRenderer, [this.tags, this.readonly])}
        ></vaadin-grid-column>

        <!-- two helper hops away -->
        <vaadin-grid-column
          ${columnBodyRenderer(this.twoHopRenderer, [this.deep])}
        ></vaadin-grid-column>

        <!-- an undecorated field cannot drive an update either way -->
        <vaadin-grid-column
          ${columnBodyRenderer(this.plainFieldRenderer, [])}
        ></vaadin-grid-column>

        <!-- a module-level function has no component state to read -->
        <vaadin-grid-column
          ${columnBodyRenderer(formatRow, [])}
        ></vaadin-grid-column>

        <!-- the other module-level form: a function declaration -->
        <vaadin-grid-column
          ${columnHeaderRenderer(formatHeader, [])}
        ></vaadin-grid-column>

        <!-- a dependency written as a nested access still declares its root -->
        <vaadin-grid-column
          ${columnBodyRenderer(this.nestedDepRenderer, [this.filters.name])}
        ></vaadin-grid-column>

        <!-- a renderer declared as a method, the dominant shape in src -->
        <vaadin-grid-column
          ${columnBodyRenderer(this.methodRenderer, [this.selectedId])}
        ></vaadin-grid-column>

        <!-- a handler runs long after the render that installed it -->
        <vaadin-grid-column
          ${columnBodyRenderer(this.handlerRenderer, [])}
        ></vaadin-grid-column>

        <!-- a write is not a read -->
        <vaadin-grid-column
          ${columnBodyRenderer(this.writingRenderer, [])}
        ></vaadin-grid-column>
      </vaadin-grid>

      <vaadin-dialog ${dialogRenderer(this.dialogBody, [this.selectedId])}>
      </vaadin-dialog>
    `;
  }

  private rowRenderer = () => html`<span ?hidden="${this.readonly}"></span>`;

  private destructuringRenderer = () => {
    const { selectedId } = this;
    return html`<span>${selectedId}</span>`;
  };

  private mappingRenderer = () => html`${this.tags.map(this.tagTemplate)}`;

  private tagTemplate = (tag: string) =>
    html`<span ?hidden="${this.readonly}">${tag}</span>`;

  private twoHopRenderer = () => html`<span>${this.firstHop()}</span>`;

  private firstHop() {
    return this.secondHop();
  }

  private secondHop() {
    return this.deep;
  }

  private plainFieldRenderer = () => html`<span>${this.plain}</span>`;

  private handlerRenderer = () =>
    html`<button @click="${() => this.onClick()}"></button>`;

  private onClick() {
    return this.selectedId;
  }

  // A write is not a read. This assigns to a *reactive* field on purpose: with
  // `this.plain` (undecorated) the case was vacuous, because stateReads would
  // never have reported it either way.
  private writingRenderer = () => {
    this.selectedId = 0;
    return html`<span></span>`;
  };

  @state() private filters = { name: '' };

  private nestedDepRenderer = () => html`<span>${this.filters.name}</span>`;

  private methodRenderer() {
    return html`<span>${this.selectedId}</span>`;
  }

  private dialogBody = () => html`<span>${this.selectedId}</span>`;
}
